using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackupSystem.Shared.Models;
using BackupSystem.Shared.Network;

namespace BackupSystem.Client.NetworkClient
{
    public sealed class RemoteRestoreOptions
    {
        public string ServerHost { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = NetworkProtocolConstants.DefaultPort;
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "123456";

        /// <summary>Mật khẩu AES-256 đã dùng khi sao lưu (chỉ cần nếu phiên bản có file mã hóa).</summary>
        public string EncryptionPassword { get; set; } = string.Empty;

        /// <summary>Cho phép ghi đè file đã tồn tại ở thư mục đích.</summary>
        public bool OverwriteExisting { get; set; }
    }

    public sealed class RestoreSummary
    {
        public int VersionId { get; set; }
        public int RestoredFiles { get; set; }
        public long RestoredBytes { get; set; }
        public string DestinationRoot { get; set; } = string.Empty;
        public List<string> RestoredPaths { get; } = new List<string>();
    }

    /// <summary>
    /// Khôi phục dữ liệu về đúng một thời điểm (phiên bản) từ Server:
    /// tải từng file trong snapshot → giải mã AES-256 → giải nén GZip → kiểm tra SHA-256 gốc → ghi ra thư mục đích
    /// với đúng cấu trúc thư mục con. Có thể khôi phục toàn bộ thư mục hoặc chỉ một/vài file.
    /// </summary>
    public static class RemoteRestoreService
    {
        public static async Task<List<BackupVersion>> ListVersionsAsync(
            RemoteRestoreOptions options,
            string rootClientPath = "",
            bool includeFailed = false,
            CancellationToken cancellationToken = default)
        {
            using (var client = await ConnectAsync(options, cancellationToken).ConfigureAwait(false))
            {
                return await client.ListVersionsAsync(rootClientPath, includeFailed, cancellationToken).ConfigureAwait(false);
            }
        }

        public static async Task<List<VersionFileEntry>> GetVersionFilesAsync(
            RemoteRestoreOptions options,
            int versionId,
            CancellationToken cancellationToken = default)
        {
            using (var client = await ConnectAsync(options, cancellationToken).ConfigureAwait(false))
            {
                return await client.GetVersionFilesAsync(versionId, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Khôi phục phiên bản. <paramref name="onlyRelativePaths"/> = null → khôi phục toàn bộ thư mục.
        /// </summary>
        public static async Task<RestoreSummary> RestoreVersionAsync(
            RemoteRestoreOptions options,
            int versionId,
            string destinationRoot,
            IEnumerable<string>? onlyRelativePaths = null,
            IProgress<TransferProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(destinationRoot))
                throw new ArgumentException("Chưa chọn thư mục khôi phục.", nameof(destinationRoot));

            using (var client = await ConnectAsync(options, cancellationToken).ConfigureAwait(false))
            {
                var files = await client.GetVersionFilesAsync(versionId, cancellationToken).ConfigureAwait(false);

                if (onlyRelativePaths != null)
                {
                    var wanted = new HashSet<string>(onlyRelativePaths.Select(p => p.Replace('\\', '/')), StringComparer.Ordinal);
                    files = files.Where(f => wanted.Contains(f.RelativePath)).ToList();
                }

                if (files.Count == 0)
                    throw new InvalidOperationException($"Phiên bản #{versionId} không có file nào phù hợp để khôi phục.");

                return await RestoreFilesAsync(client, versionId, files, destinationRoot, options, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        public static async Task<RestoreSummary> RestoreFilesAsync(
            TcpBackupClient client,
            int versionId,
            IList<VersionFileEntry> files,
            string destinationRoot,
            RemoteRestoreOptions options,
            IProgress<TransferProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string root = Path.GetFullPath(destinationRoot);
            Directory.CreateDirectory(root);

            // Kiểm tra trước toàn bộ đường dẫn đích (chống Path Traversal, không ghi đè ngoài ý muốn)
            var targets = new List<(VersionFileEntry Entry, string Destination)>();
            foreach (var entry in files)
            {
                string destination = SafePathHelper.CombineUnderRoot(root, entry.RelativePath);
                if (File.Exists(destination) && !options.OverwriteExisting)
                    throw new IOException($"File đã tồn tại ở thư mục khôi phục: {destination}");
                targets.Add((entry, destination));
            }

            if (targets.Any(t => t.Entry.IsEncrypted) && string.IsNullOrWhiteSpace(options.EncryptionPassword))
                throw new InvalidOperationException("Phiên bản này chứa file mã hóa AES-256, vui lòng nhập mật khẩu giải mã.");

            var summary = new RestoreSummary { VersionId = versionId, DestinationRoot = root };
            long totalBytes = Math.Max(1, targets.Sum(t => Math.Max(1, t.Entry.StoredSizeBytes)));
            long doneBytes = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (entry, destination) = targets[i];
                long weight = Math.Max(1, entry.StoredSizeBytes);
                string tempDownload = Path.Combine(Path.GetTempPath(), "sdb_restore_" + Guid.NewGuid().ToString("N") + ".tmp");

                try
                {
                    int index = i;
                    long baseDone = doneBytes;
                    var fileProgress = new Progress<TransferProgressReport>(report =>
                    {
                        double fraction = report.TotalBytes > 0 ? (double)report.BytesTransferred / report.TotalBytes : 0;
                        report.ProgressPercentage = (int)Math.Min(100, (baseDone + fraction * weight) * 100 / totalBytes);
                        report.StatusMessage = $"[{index + 1}/{targets.Count}] {entry.RelativePath}: {report.StatusMessage}";
                        progress?.Report(report);
                    });

                    var info = await client.DownloadVersionFileAsync(versionId, entry.RelativePath, tempDownload, fileProgress, cancellationToken)
                        .ConfigureAwait(false);

                    string expectedSha = !string.IsNullOrWhiteSpace(info.OriginalSha256) ? info.OriginalSha256 : entry.Sha256;
                    BackupFileTransformer.Restore(
                        tempDownload,
                        destination,
                        info.IsEncrypted || entry.IsEncrypted,
                        info.IsCompressed || entry.IsCompressed,
                        options.EncryptionPassword,
                        expectedSha);

                    // Khôi phục cả thời gian sửa đổi gốc để lần Incremental sau nhận diện đúng
                    if (entry.LastModifiedTime > DateTime.MinValue.AddDays(1))
                    {
                        try { File.SetLastWriteTimeUtc(destination, entry.LastModifiedTime.ToUniversalTime()); } catch { }
                    }

                    summary.RestoredFiles++;
                    summary.RestoredBytes += entry.OriginalSizeBytes;
                    summary.RestoredPaths.Add(destination);
                }
                catch (System.Security.Cryptography.CryptographicException ex)
                {
                    throw new InvalidDataException($"Không giải mã được '{entry.RelativePath}': mật khẩu sai hoặc dữ liệu bị thay đổi.", ex);
                }
                finally
                {
                    try { if (File.Exists(tempDownload)) File.Delete(tempDownload); } catch { }
                }

                doneBytes += weight;
            }

            progress?.Report(new TransferProgressReport
            {
                ProgressPercentage = 100,
                IsCompleted = true,
                StatusMessage = $"Đã khôi phục {summary.RestoredFiles} file từ phiên bản #{versionId}."
            });

            return summary;
        }

        private static async Task<TcpBackupClient> ConnectAsync(RemoteRestoreOptions options, CancellationToken cancellationToken)
        {
            var client = new TcpBackupClient();
            try
            {
                bool connected = await client.ConnectAsync(options.ServerHost, options.ServerPort, NetworkProtocolConstants.DefaultTimeoutMs, cancellationToken)
                    .ConfigureAwait(false);
                if (!connected)
                    throw new IOException($"Không thể kết nối tới Server {options.ServerHost}:{options.ServerPort}.");

                var auth = await client.AuthenticateAsync(options.Username, options.Password, cancellationToken).ConfigureAwait(false);
                if (!auth.IsSuccess)
                    throw new UnauthorizedAccessException("Đăng nhập Server thất bại: " + auth.Message);

                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }
}
