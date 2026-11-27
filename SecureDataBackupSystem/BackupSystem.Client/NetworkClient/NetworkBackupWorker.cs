using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using BackupSystem.Client.Security;
using BackupSystem.Shared.Models;
using BackupSystem.Shared.Network;

namespace BackupSystem.Client.NetworkClient
{
    /// <summary>
    /// Tham số đầu vào cho tác vụ truyền file ngầm qua mạng bằng BackgroundWorker.
    /// Hỗ trợ đầy đủ Full/Incremental Backup, Lọc file, Mã hóa AES-256 và AI Chống Ransomware.
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

        // Tính năng nâng cao theo yêu cầu đề tài
        public bool IsIncremental { get; set; }
        public string AllowedExtensions { get; set; } = "*.*";
        public string ExcludedPatterns { get; set; } = string.Empty;
        public bool EncryptBeforeTransfer { get; set; }
        public string EncryptionPassword { get; set; } = string.Empty;
        public bool EnableAiRansomwareProtection { get; set; } = true;

        // Nén GZip trước khi mã hóa/gửi để tiết kiệm băng thông (tự bỏ qua định dạng đã nén sẵn như .zip/.jpg/.mp4)
        public bool EnableCompression { get; set; } = true;

        // Tài khoản trên Server (bảng Users). Mặc định là tài khoản khởi tạo sẵn admin/123456.
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "123456";

        // Nguồn kích hoạt phiên bản: MANUAL | SCHEDULER | WATCHER
        public string TriggerType { get; set; } = "MANUAL";
    }

    /// <summary>
    /// Kết quả trả về khi kết thúc tiến trình BackgroundWorker.
    /// </summary>
    public sealed class NetworkBackupWorkerResult
    {
        public bool Success { get; set; }
        public bool IsCancelled { get; set; }
        public int TransferredFilesCount { get; set; }
        public int SkippedFilesCount { get; set; }
        public long TotalBytesTransferred { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Error { get; set; }
        public RansomwareDetectionResult? RansomwareThreat { get; set; }

        // Thông tin phiên bản (Version Control) do Server cấp
        public int VersionId { get; set; }
        public string BackupType { get; set; } = string.Empty;
        public int SnapshotFilesCount { get; set; }
        public int CompressedFilesCount { get; set; }
        public long OriginalBytes { get; set; }
    }

    /// <summary>
    /// [Week 3] Bộ điều phối BackgroundWorker đồng bộ với Progress Bar chống treo giao diện UI khi truyền file nặng.
    /// Toàn bộ logic (lọc file, AI chống Ransomware, Incremental theo phiên bản trên Server, nén GZip, mã hóa AES-256,
    /// chunking) nằm trong <see cref="NetworkBackupEngine"/>; lớp này chỉ chạy engine trên luồng nền và
    /// chuyển tiến trình về luồng giao diện.
    /// </summary>
    public sealed class NetworkBackupWorker : BackgroundWorker
    {
        private NetworkBackupEngine? _engine;
        private CancellationTokenSource? _cts;

        public event Action<TransferProgressReport>? TransferProgressUpdated;
        public event Action<NetworkBackupWorkerResult>? TransferCompleted;
        public event Action<string>? LogMessage;

        public NetworkBackupWorker()
        {
            WorkerReportsProgress = true;
            WorkerSupportsCancellation = true;
        }

        public void StartBackup(NetworkBackupWorkerArgs args)
        {
            if (IsBusy)
                throw new InvalidOperationException("Tiến trình sao lưu mạng đang bận thực hiện tác vụ trước.");

            _cts = new CancellationTokenSource();
            _engine = new NetworkBackupEngine();
            _engine.OnLog += msg => LogMessage?.Invoke(msg);
            RunWorkerAsync(args);
        }

        public void CancelBackup()
        {
            if (WorkerSupportsCancellation && IsBusy)
            {
                CancelAsync();
                try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
                _engine?.Cancel();
            }
        }

        protected override void OnDoWork(DoWorkEventArgs e)
        {
            var args = e.Argument as NetworkBackupWorkerArgs;
            if (args == null)
            {
                throw new ArgumentException("Tham số NetworkBackupWorkerArgs không hợp lệ.");
            }

            var engine = _engine ?? new NetworkBackupEngine();
            var token = _cts?.Token ?? CancellationToken.None;

            NetworkBackupWorkerResult result = engine.Run(
                args,
                report => ReportProgress(report.ProgressPercentage, report),
                token);

            if (result.IsCancelled || CancellationPending)
            {
                result.IsCancelled = true;
                e.Cancel = true;
            }

            e.Result = result;
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
            _engine = null;
        }
    }
}
