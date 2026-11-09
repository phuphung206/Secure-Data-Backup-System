using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BackupSystem.Shared.Models;
using BackupSystem.Shared.Network;

namespace BackupSystem.Client.NetworkClient
{
    /// <summary>
    /// Bộ điều khiển sao lưu mạng dựa trên async/await và Task (Task-based Asynchronous Pattern).
    /// Cung cấp giải pháp thay thế hiện đại tương thích song song với BackgroundWorker.
    /// </summary>
    public sealed class NetworkBackupTaskRunner
    {
        private CancellationTokenSource? _cts;
        private TcpBackupClient? _client;

        public bool IsRunning { get; private set; }

        public async Task<NetworkBackupWorkerResult> RunBackupAsync(
            NetworkBackupWorkerArgs args,
            IProgress<TransferProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (IsRunning)
                throw new InvalidOperationException("Một tác vụ truyền file khác đang thực thi.");

            IsRunning = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _client = new TcpBackupClient();

            var result = new NetworkBackupWorkerResult();

            try
            {
                progress?.Report(new TransferProgressReport
                {
                    StatusMessage = $"Đang kết nối tới máy chủ {args.ServerHost}:{args.ServerPort}...",
                    ProgressPercentage = 0
                });

                bool connected = await _client.ConnectAsync(args.ServerHost, args.ServerPort, NetworkProtocolConstants.DefaultTimeoutMs, _cts.Token).ConfigureAwait(false);
                if (!connected)
                {
                    throw new IOException($"Không thể kết nối tới Server {args.ServerHost}:{args.ServerPort}. Vui lòng kiểm tra Server.");
                }

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

                if (filesToTransfer.Count == 0)
                {
                    result.Success = true;
                    result.Message = "Không có file nào cần sao lưu.";
                    return result;
                }

                long totalBytesAllFiles = 0;
                foreach (var item in filesToTransfer)
                {
                    totalBytesAllFiles += new FileInfo(item.FullPath).Length;
                }

                int completedFiles = 0;
                long totalBytesTransferredAccumulated = 0;

                for (int i = 0; i < filesToTransfer.Count; i++)
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    var currentItem = filesToTransfer[i];
                    long currentFileLength = new FileInfo(currentItem.FullPath).Length;

                    var fileProgress = new Progress<TransferProgressReport>(report =>
                    {
                        long overallBytes = totalBytesTransferredAccumulated + report.BytesTransferred;
                        int overallPercent = totalBytesAllFiles > 0
                            ? (int)((overallBytes * 100) / totalBytesAllFiles)
                            : 100;

                        report.StatusMessage = $"[File {i + 1}/{filesToTransfer.Count}] {report.FileName}: {report.StatusMessage}";
                        report.ProgressPercentage = Math.Min(100, overallPercent);

                        progress?.Report(report);
                    });

                    bool ok = await _client.UploadFileAsync(
                        currentItem.FullPath,
                        currentItem.RelativePath,
                        args.BackupId,
                        args.SourceKey,
                        args.ChunkSize,
                        fileProgress,
                        _cts.Token).ConfigureAwait(false);

                    if (!ok)
                        throw new IOException($"Lỗi truyền file: {currentItem.RelativePath}");

                    completedFiles++;
                    totalBytesTransferredAccumulated += currentFileLength;
                }

                result.Success = true;
                result.TransferredFilesCount = completedFiles;
                result.TotalBytesTransferred = totalBytesTransferredAccumulated;
                result.Message = $"Đã truyền thành công {completedFiles} file qua mạng TCP.";
                return result;
            }
            catch (OperationCanceledException)
            {
                result.IsCancelled = true;
                result.Message = "Tác vụ đã bị hủy bởi người dùng.";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex;
                result.Message = $"Lỗi: {ex.Message}";
                return result;
            }
            finally
            {
                IsRunning = false;
                _client?.Disconnect();
                _client?.Dispose();
                _client = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        public void Cancel()
        {
            _cts?.Cancel();
            _client?.Disconnect();
        }
    }
}

