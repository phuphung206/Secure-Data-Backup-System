using System;
using System.Threading;
using System.Threading.Tasks;
using BackupSystem.Shared.Models;

namespace BackupSystem.Client.NetworkClient
{
    /// <summary>
    /// Bộ điều khiển sao lưu mạng dựa trên async/await và Task (Task-based Asynchronous Pattern).
    /// Cung cấp giải pháp thay thế hiện đại tương thích song song với BackgroundWorker;
    /// dùng chung <see cref="NetworkBackupEngine"/> nên có đủ Version Control, nén, mã hóa và AI chống Ransomware.
    /// </summary>
    public sealed class NetworkBackupTaskRunner
    {
        private NetworkBackupEngine? _engine;
        private CancellationTokenSource? _cts;

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
            _engine = new NetworkBackupEngine();

            try
            {
                var engine = _engine;
                var token = _cts.Token;
                return await Task.Run(() => engine.Run(args, report => progress?.Report(report), token)).ConfigureAwait(false);
            }
            finally
            {
                IsRunning = false;
                _engine = null;
                _cts.Dispose();
                _cts = null;
            }
        }

        public void Cancel()
        {
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
            _engine?.Cancel();
        }
    }
}
