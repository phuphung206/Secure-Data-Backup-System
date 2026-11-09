using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BackupSystem.Shared.Models;
using BackupSystem.Shared.Network;

namespace BackupSystem.Client.NetworkClient
{
    /// <summary>
    /// Tham số đầu vào cho tác vụ truyền file ngầm qua mạng bằng BackgroundWorker.
    /// </summary>
    public sealed class NetworkBackupWorkerArgs
    {
        public string ServerHost { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = NetworkProtocolConstants.DefaultPort;
        public string SourcePath { get; set; } = string.Empty;
        public string BackupId { get; set; } = Guid.NewGuid().ToString("N");
        public string SourceKey { get; set; } = string.Empty;
        public int ChunkSize { get; set; } = NetworkProtocolConstants.DefaultChunkSize;
        public List<string> FilesToBackup { get; set; } = new List<string>();
        public bool IsSingleFile { get; set; }
    }

    /// <summary>
    /// Kết quả trả về khi kết thúc tiến trình BackgroundWorker.
    /// </summary>
    public sealed class NetworkBackupWorkerResult
    {
        public bool Success { get; set; }
        public bool IsCancelled { get; set; }
        public int TransferredFilesCount { get; set; }
        public long TotalBytesTransferred { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Error { get; set; }
    }

    /// <summary>
    /// [Week 3] Bộ điều phối BackgroundWorker đồng bộ với Progress Bar chống treo giao diện UI khi truyền file nặng.
    /// Tách rời hoàn toàn luồng I/O mạng TCP khỏi Main UI Thread, tự động marshal dữ liệu tiến độ về UI thread an toàn.
    /// </summary>
    public sealed class NetworkBackupWorker : BackgroundWorker
    {
        private CancellationTokenSource? _cts;
        private TcpBackupClient? _client;

        public event Action<TransferProgressReport>? TransferProgressUpdated;
        public event Action<NetworkBackupWorkerResult>? TransferCompleted;

        public NetworkBackupWorker()
        {
            WorkerReportsProgress = true;
            WorkerSupportsCancellation = true;
        }

        /// <summary>
        /// Bắt đầu tác vụ sao lưu truyền file mạng dưới nền (Background Thread).
        /// </summary>
        public void StartBackup(NetworkBackupWorkerArgs args)
        {
            if (IsBusy)
                throw new InvalidOperationException("Tiến trình sao lưu mạng đang bận thực hiện tác vụ trước.");

            _cts = new CancellationTokenSource();
            RunWorkerAsync(args);
        }

        /// <summary>
        /// Yêu cầu dừng/hủy tác vụ sao lưu ngay lập tức mà không làm treo UI.
        /// </summary>
        public void CancelBackup()
        {
            if (WorkerSupportsCancellation && IsBusy)
            {
                CancelAsync();
                _cts?.Cancel();
                _client?.Disconnect();
            }
        }

        protected override void OnDoWork(DoWorkEventArgs e)
        {
            var args = e.Argument as NetworkBackupWorkerArgs;
            if (args == null)
            {
                throw new ArgumentException("Tham số NetworkBackupWorkerArgs không hợp lệ.");
            }

            var result = new NetworkBackupWorkerResult();
            _client = new TcpBackupClient();

            try
            {
                // Báo cáo kết nối
                ReportProgress(0, new TransferProgressReport
                {
                    StatusMessage = $"Đang kết nối tới máy chủ {args.ServerHost}:{args.ServerPort}...",
                    ProgressPercentage = 0
                });

                // Thực hiện kết nối tới Server trong luồng nền
                bool connected = _client.ConnectAsync(args.ServerHost, args.ServerPort, NetworkProtocolConstants.DefaultTimeoutMs, _cts!.Token).GetAwaiter().GetResult();
                if (!connected)
                {
                    throw new IOException($"Không thể kết nối tới Server {args.ServerHost}:{args.ServerPort}. Vui lòng kiểm tra Server đã bật chưa.");
                }

                if (CancellationPending || _cts.IsCancellationRequested)
                {
                    e.Cancel = true;
                    result.IsCancelled = true;
                    result.Message = "Người dùng đã hủy tiến trình sao lưu.";
                    e.Result = result;
                    return;
                }

                // Chuẩn bị danh sách file cần sao lưu
                var filesToTransfer = new List<(string FullPath, string RelativePath)>();
                if (args.IsSingleFile || File.Exists(args.SourcePath))
                {
                    filesToTransfer.Add((Path.GetFullPath(args.SourcePath), Path.GetFileName(args.SourcePath)));
                }
                else if (Directory.Exists(args.SourcePath))
                {
                    string sourceDir = Path.GetFullPath(args.SourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string[] allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
                    foreach (var file in allFiles)
                    {
                        string relative = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        filesToTransfer.Add((file, relative));
                    }
                }
                else if (args.FilesToBackup != null && args.FilesToBackup.Count > 0)
                {
                    foreach (var file in args.FilesToBackup)
                    {
                        if (File.Exists(file))
                            filesToTransfer.Add((Path.GetFullPath(file), Path.GetFileName(file)));
                    }
                }

                if (filesToTransfer.Count == 0)
                {
                    result.Success = true;
                    result.Message = "Không tìm thấy file nào cần sao lưu.";
                    e.Result = result;
                    return;
                }

                long totalBytesAllFiles = 0;
                foreach (var item in filesToTransfer)
                {
                    totalBytesAllFiles += new FileInfo(item.FullPath).Length;
                }

                int completedFiles = 0;
                long totalBytesTransferredAccumulated = 0;

                // Vòng lặp truyền từng file qua Socket TCP
                for (int i = 0; i < filesToTransfer.Count; i++)
                {
                    if (CancellationPending || _cts.IsCancellationRequested)
                    {
                        e.Cancel = true;
                        result.IsCancelled = true;
                        result.Message = "Đã hủy tác vụ truyền file mạng.";
                        e.Result = result;
                        return;
                    }

                    var currentItem = filesToTransfer[i];
                    long currentFileLength = new FileInfo(currentItem.FullPath).Length;

                    // Tạo bộ nhận tiến trình IProgress chuyển tiếp về BackgroundWorker.ReportProgress
                    var fileProgress = new Progress<TransferProgressReport>(report =>
                    {
                        // Tính toán tổng tiến độ trên toàn bộ phiên sao lưu
                        long overallBytes = totalBytesTransferredAccumulated + report.BytesTransferred;
                        int overallPercent = totalBytesAllFiles > 0
                            ? (int)((overallBytes * 100) / totalBytesAllFiles)
                            : 100;

                        report.StatusMessage = $"[File {i + 1}/{filesToTransfer.Count}] {report.FileName}: {report.StatusMessage}";
                        report.ProgressPercentage = Math.Min(100, overallPercent);

                        // Đưa dữ liệu sang UI thread qua cơ chế ngầm an toàn của BackgroundWorker
                        ReportProgress(report.ProgressPercentage, report);
                    });

                    // Thực thi truyền file phân đoạn (Chunking) qua mạng
                    bool fileOk = _client.UploadFileAsync(
                        currentItem.FullPath,
                        currentItem.RelativePath,
                        args.BackupId,
                        args.SourceKey,
                        args.ChunkSize,
                        fileProgress,
                        _cts.Token).GetAwaiter().GetResult();

                    if (!fileOk)
                    {
                        throw new IOException($"Lỗi khi truyền file: {currentItem.RelativePath}");
                    }

                    completedFiles++;
                    totalBytesTransferredAccumulated += currentFileLength;
                }

                result.Success = true;
                result.TransferredFilesCount = completedFiles;
                result.TotalBytesTransferred = totalBytesTransferredAccumulated;
                result.Message = $"Sao lưu thành công toàn bộ {completedFiles} file ({TransferProgressReport.FormatBytes(totalBytesTransferredAccumulated)}) qua mạng TCP!";
                e.Result = result;
            }
            catch (OperationCanceledException)
            {
                e.Cancel = true;
                result.IsCancelled = true;
                result.Message = "Tác vụ đã được hủy bởi người dùng.";
                e.Result = result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex;
                result.Message = $"Lỗi truyền file: {ex.Message}";
                e.Result = result;
                throw;
            }
            finally
            {
                _client?.Disconnect();
                _client?.Dispose();
                _client = null;
            }
        }

        protected override void OnProgressChanged(ProgressChangedEventArgs e)
        {
            base.OnProgressChanged(e);

            if (e.UserState is TransferProgressReport report)
            {
                TransferProgressUpdated?.Invoke(report);
            }
        }

        protected override void OnRunWorkerCompleted(RunWorkerCompletedEventArgs e)
        {
            base.OnRunWorkerCompleted(e);

            NetworkBackupWorkerResult result;
            if (e.Error != null)
            {
                result = new NetworkBackupWorkerResult
                {
                    Success = false,
                    Error = e.Error,
                    Message = e.Error.Message
                };
            }
            else if (e.Cancelled)
            {
                result = new NetworkBackupWorkerResult
                {
                    Success = false,
                    IsCancelled = true,
                    Message = "Tác vụ đã bị hủy."
                };
            }
            else
            {
                result = e.Result as NetworkBackupWorkerResult ?? new NetworkBackupWorkerResult { Success = true };
            }

            TransferCompleted?.Invoke(result);
            _cts?.Dispose();
            _cts = null;
        }
    }
}
