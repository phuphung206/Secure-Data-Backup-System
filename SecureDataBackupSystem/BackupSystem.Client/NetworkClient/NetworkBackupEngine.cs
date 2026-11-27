using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BackupSystem.Client.Security;
using BackupSystem.Shared.Models;
using BackupSystem.Shared.Network;

namespace BackupSystem.Client.NetworkClient
{
    /// <summary>
    /// Lõi xử lý sao lưu qua mạng (đồng bộ, chạy trên luồng nền). Được dùng chung bởi:
    ///  - <see cref="NetworkBackupWorker"/> (BackgroundWorker cho giao diện WinForms),
    ///  - <see cref="NetworkBackupTaskRunner"/> (async/await),
    ///  - Lịch sao lưu tự động (Task Scheduler) và Real-time Monitoring (FileSystemWatcher).
    ///
    /// Quy trình một lần backup:
    ///   1. Quét file + lọc định dạng      2. AI chống Ransomware
    ///   3. Kết nối + đăng nhập            4. Mở phiên bản (Version) trên Server, nhận snapshot phiên bản trước
    ///   5. Incremental: so sánh với snapshot trên Server (kích thước/thời gian → SHA-256)
    ///   6. Mỗi file: Nén GZip → Mã hóa AES-256 → chia chunk → gửi TCP
    ///   7. Đóng phiên bản: gửi danh sách file hiện có để Server dựng snapshot (kế thừa file không đổi, loại file đã xóa)
    /// </summary>
    public sealed class NetworkBackupEngine
    {
        private readonly object _sync = new object();
        private TcpBackupClient? _client;
        private CancellationTokenSource? _cts;

        public event Action<string>? OnLog;

        /// <summary>
        /// Hủy tác vụ đang chạy (an toàn khi gọi từ luồng khác).
        /// </summary>
        public void Cancel()
        {
            lock (_sync)
            {
                try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
                _client?.Disconnect();
            }
        }

        public NetworkBackupWorkerResult Run(
            NetworkBackupWorkerArgs args,
            Action<TransferProgressReport>? reportProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            var result = new NetworkBackupWorkerResult();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var client = new TcpBackupClient();
            client.OnLog += msg => OnLog?.Invoke(msg);

            lock (_sync)
            {
                _cts = cts;
                _client = client;
            }

            CancellationToken token = cts.Token;
            int openVersionId = 0;
            string backupId = SafePathHelper.IsValidBackupId(args.BackupId) ? args.BackupId : Guid.NewGuid().ToString("N");

            void Report(int percent, string message)
            {
                reportProgress?.Invoke(new TransferProgressReport
                {
                    ProgressPercentage = Math.Max(0, Math.Min(100, percent)),
                    StatusMessage = message
                });
            }

            try
            {
                // Bước 1: Quét và lọc file theo định dạng
                Report(0, "Đang quét danh sách file và kiểm tra bộ lọc...");
                var candidateFiles = ScanCandidateFiles(args);
                if (candidateFiles.Count == 0)
                {
                    result.Success = true;
                    result.Message = "Không tìm thấy file nào phù hợp với bộ lọc để sao lưu.";
                    return result;
                }

                // Bước 2: AI chống Ransomware (Anomaly Detection) - chặn trước khi chạm tới Server
                if (args.EnableAiRansomwareProtection)
                {
                    Report(1, "🤖 AI đang phân tích độ bất thường & quét mã độc Ransomware...");
                    var aiResult = RansomwareProtectionService.AnalyzeFiles(
                        args.SourcePath, candidateFiles.Select(f => f.FullPath).ToList());

                    if (aiResult.IsRansomwareDetected)
                    {
                        Report(0, "⛔ PHÁT HIỆN RANSOMWARE! Đã ngắt tiến trình sao lưu để bảo vệ Server.");
                        throw new RansomwareThreatDetectedException(aiResult);
                    }
                }

                token.ThrowIfCancellationRequested();

                // Bước 3: Kết nối + đăng nhập
                Report(2, $"Đang kết nối tới máy chủ {args.ServerHost}:{args.ServerPort}...");
                bool connected = client.ConnectAsync(args.ServerHost, args.ServerPort, NetworkProtocolConstants.DefaultTimeoutMs, token)
                    .GetAwaiter().GetResult();
                if (!connected)
                    throw new IOException($"Không thể kết nối tới Server {args.ServerHost}:{args.ServerPort}. Vui lòng kiểm tra Server đã bật chưa.");

                var auth = client.AuthenticateAsync(args.Username, args.Password, token).GetAwaiter().GetResult();
                if (!auth.IsSuccess)
                    throw new UnauthorizedAccessException("Đăng nhập Server thất bại: " + auth.Message);

                // Bước 4: Mở phiên bản mới trên Server
                string rootKey = BuildRootClientPath(args);
                var session = client.StartBackupSessionAsync(new BackupSessionStartPayload
                {
                    BackupId = backupId,
                    BackupType = args.IsIncremental ? "INCREMENTAL" : "FULL",
                    TriggerType = string.IsNullOrWhiteSpace(args.TriggerType) ? "MANUAL" : args.TriggerType,
                    RootClientPath = rootKey,
                    ClientMachineName = Environment.MachineName
                }, token).GetAwaiter().GetResult();

                if (!session.IsAccepted)
                    throw new InvalidOperationException("Server từ chối mở phiên bản backup: " + session.Message);

                openVersionId = session.VersionId;
                result.VersionId = session.VersionId;
                result.BackupType = session.BackupType;
                Log($"[PHIÊN BẢN] {session.Message}");

                // Bước 5: Chọn file cần gửi (Incremental so sánh với snapshot của phiên bản trước trên Server)
                var parentFiles = new Dictionary<string, VersionFileEntry>(StringComparer.Ordinal);
                foreach (var entry in session.ParentFiles ?? new List<VersionFileEntry>())
                    parentFiles[entry.RelativePath] = entry;

                bool incremental = string.Equals(session.BackupType, "INCREMENTAL", StringComparison.OrdinalIgnoreCase);
                if (args.IsIncremental && !incremental)
                    Log("[INCREMENTAL] Server chưa có phiên bản hoàn chỉnh nào của thư mục này → chuyển sang Full Backup.");

                Report(3, incremental
                    ? "Đang so sánh với phiên bản trước trên Server (kích thước, thời gian, SHA-256)..."
                    : "Full Backup: chuẩn bị gửi toàn bộ file...");

                var plan = new List<PlannedFile>();
                int skippedCount = 0;
                foreach (var file in candidateFiles)
                {
                    token.ThrowIfCancellationRequested();
                    var fi = new FileInfo(file.FullPath);
                    parentFiles.TryGetValue(file.RelativePath, out VersionFileEntry? previous);
                    string? sha = null;

                    if (incremental && previous != null)
                    {
                        bool sameSizeAndTime = previous.OriginalSizeBytes == fi.Length &&
                            previous.LastModifiedTime.ToUniversalTime() == fi.LastWriteTimeUtc;
                        if (sameSizeAndTime)
                        {
                            skippedCount++;
                            continue;
                        }

                        sha = BackupFileTransformer.ComputeSha256(file.FullPath);
                        if (string.Equals(sha, previous.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            skippedCount++;
                            continue;
                        }
                    }

                    plan.Add(new PlannedFile
                    {
                        FullPath = file.FullPath,
                        RelativePath = file.RelativePath,
                        Sha256 = sha,
                        ChangeType = previous == null ? "ADDED" : "MODIFIED",
                        SizeBytes = fi.Length,
                        LastWriteTimeUtc = fi.LastWriteTimeUtc
                    });
                }

                result.SkippedFilesCount = skippedCount;

                // Bước 6: Nén → Mã hóa → Truyền từng file
                long totalOriginalBytes = Math.Max(1, plan.Sum(p => Math.Max(1, p.SizeBytes)));
                long doneOriginalBytes = 0;
                long payloadBytesSent = 0;
                long originalBytesSent = 0;
                int completed = 0;

                for (int i = 0; i < plan.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var item = plan[i];
                    long weight = Math.Max(1, item.SizeBytes);

                    string tags = (args.EnableCompression ? "[GZip] " : string.Empty) +
                                  (args.EncryptBeforeTransfer ? "[AES-256] " : string.Empty);
                    Report((int)(doneOriginalBytes * 100 / totalOriginalBytes),
                        $"[File {i + 1}/{plan.Count}] {tags}Đang chuẩn bị {item.RelativePath}...");

                    item.Sha256 ??= BackupFileTransformer.ComputeSha256(item.FullPath);

                    using (var prepared = BackupFileTransformer.Prepare(
                        item.FullPath,
                        item.RelativePath,
                        args.EnableCompression,
                        args.EncryptBeforeTransfer,
                        args.EncryptionPassword))
                    {
                        int index = i;
                        long baseDone = doneOriginalBytes;
                        var fileProgress = new SynchronousProgress<TransferProgressReport>(report =>
                        {
                            double fraction = report.TotalBytes > 0
                                ? (double)report.BytesTransferred / report.TotalBytes
                                : (report.IsCompleted ? 1.0 : 0.0);
                            int overall = (int)((baseDone + fraction * weight) * 100 / totalOriginalBytes);
                            report.ProgressPercentage = Math.Min(100, Math.Max(0, overall));
                            report.StatusMessage = $"[File {index + 1}/{plan.Count}] {tags}{report.FileName}: {report.StatusMessage}";
                            reportProgress?.Invoke(report);
                        });

                        bool ok = client.UploadFileAsync(
                            prepared.PayloadPath,
                            prepared.StoredRelativePath,
                            backupId,
                            args.SourceKey,
                            args.ChunkSize,
                            fileProgress,
                            token,
                            new BackupInitPayload
                            {
                                VersionId = openVersionId,
                                IsEncrypted = prepared.IsEncrypted,
                                IsCompressed = prepared.IsCompressed,
                                OriginalRelativePath = item.RelativePath,
                                OriginalSha256 = item.Sha256 ?? string.Empty,
                                OriginalSizeBytes = item.SizeBytes,
                                OriginalLastWriteTimeUtc = item.LastWriteTimeUtc,
                                ChangeType = item.ChangeType
                            }).GetAwaiter().GetResult();

                        if (!ok)
                            throw new IOException($"Lỗi khi truyền file: {item.RelativePath}");

                        if (prepared.IsCompressed)
                            result.CompressedFilesCount++;
                        payloadBytesSent += prepared.PayloadSizeBytes;
                    }

                    completed++;
                    originalBytesSent += item.SizeBytes;
                    doneOriginalBytes += weight;
                }

                // Bước 7: Đóng phiên bản, Server dựng snapshot hoàn chỉnh
                token.ThrowIfCancellationRequested();
                Report(100, "Đang chốt phiên bản trên Server...");
                var end = client.EndBackupSessionAsync(new BackupSessionEndPayload
                {
                    BackupId = backupId,
                    VersionId = openVersionId,
                    IsSuccess = true,
                    CurrentRelativePaths = candidateFiles.Select(f => f.RelativePath).ToList()
                }, token).GetAwaiter().GetResult();
                openVersionId = 0;

                if (!end.IsSuccess)
                    throw new InvalidOperationException("Server không chốt được phiên bản: " + end.Message);

                Report(100, $"Hoàn tất phiên bản #{end.VersionId}: {end.UploadedFiles} file gửi mới, {end.CarriedOverFiles} file kế thừa.");

                result.Success = true;
                result.TransferredFilesCount = completed;
                result.TotalBytesTransferred = payloadBytesSent;
                result.OriginalBytes = originalBytesSent;
                result.SnapshotFilesCount = end.TotalFiles;

                string encInfo = args.EncryptBeforeTransfer ? ", mã hóa AES-256" : string.Empty;
                string zipInfo = result.CompressedFilesCount > 0
                    ? $", nén GZip {result.CompressedFilesCount} file ({TransferProgressReport.FormatBytes(originalBytesSent)} → {TransferProgressReport.FormatBytes(payloadBytesSent)})"
                    : string.Empty;
                string skipInfo = skippedCount > 0 ? $", bỏ qua {skippedCount} file không đổi" : string.Empty;
                result.Message = completed == 0
                    ? $"Dữ liệu không có thay đổi kể từ lần sao lưu trước ({skippedCount} file đã được bỏ qua để tiết kiệm băng thông). Đã ghi nhận phiên bản #{end.VersionId}."
                    : $"Sao lưu thành công {completed} file{encInfo}{zipInfo}{skipInfo} qua mạng TCP. Phiên bản #{end.VersionId} ({end.TotalFiles} file).";
                return result;
            }
            catch (RansomwareThreatDetectedException ex)
            {
                result.Success = false;
                result.Error = ex;
                result.RansomwareThreat = ex.DetectionResult;
                result.Message = $"⛔ CHẶN SAO LƯU: Phát hiện nguy cơ Ransomware (Điểm bất thường: {ex.DetectionResult.AnomalyScore:F2})!\n{ex.DetectionResult.Message}";
                return result;
            }
            catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested)
            {
                result.IsCancelled = true;
                result.Message = "Tác vụ đã được hủy bởi người dùng.";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex;
                result.Message = $"Lỗi sao lưu: {ex.Message}";
                return result;
            }
            finally
            {
                // Phiên bản dở dang: báo Server hủy (nếu còn kết nối) để không bị dùng làm bản cha cho Incremental
                if (openVersionId > 0 && client.IsConnected)
                {
                    try
                    {
                        using (var abortCts = new CancellationTokenSource(3000))
                        {
                            client.EndBackupSessionAsync(new BackupSessionEndPayload
                            {
                                BackupId = backupId,
                                VersionId = openVersionId,
                                IsSuccess = false,
                                Message = result.Message
                            }, abortCts.Token).GetAwaiter().GetResult();
                        }
                    }
                    catch
                    {
                        // Server sẽ tự đánh dấu FAILED khi phiên kết nối đóng
                    }
                }

                lock (_sync)
                {
                    _client = null;
                    _cts = null;
                }
                client.Disconnect();
                client.Dispose();
                cts.Dispose();
            }
        }

        /// <summary>
        /// Khóa nhận diện thư mục gốc trên Server: "&lt;Tên máy&gt;|&lt;đường dẫn đầy đủ&gt;" (hoặc SourceKey nếu được chỉ định).
        /// Các phiên bản của cùng thư mục được nối thành chuỗi Full → Incremental → Incremental...
        /// </summary>
        public static string BuildRootClientPath(NetworkBackupWorkerArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.SourceKey))
                return args.SourceKey;

            string full = string.IsNullOrWhiteSpace(args.SourcePath)
                ? string.Empty
                : Path.GetFullPath(args.SourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Environment.MachineName + "|" + full;
        }

        public static List<(string FullPath, string RelativePath)> ScanCandidateFiles(NetworkBackupWorkerArgs args)
        {
            var results = new List<(string FullPath, string RelativePath)>();

            if (args.IsSingleFile || File.Exists(args.SourcePath))
            {
                string fullPath = Path.GetFullPath(args.SourcePath);
                string fileName = Path.GetFileName(fullPath);
                if (IsFileAllowed(fileName, args.AllowedExtensions, args.ExcludedPatterns))
                {
                    results.Add((fullPath, fileName));
                }
            }
            else if (Directory.Exists(args.SourcePath))
            {
                string sourceDir = Path.GetFullPath(args.SourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(sourceDir.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    if (IsFileAllowed(relative, args.AllowedExtensions, args.ExcludedPatterns))
                    {
                        results.Add((file, relative));
                    }
                }
            }
            else if (args.FilesToBackup != null && args.FilesToBackup.Count > 0)
            {
                foreach (var file in args.FilesToBackup)
                {
                    if (File.Exists(file))
                    {
                        string fileName = Path.GetFileName(file);
                        if (IsFileAllowed(fileName, args.AllowedExtensions, args.ExcludedPatterns))
                        {
                            results.Add((Path.GetFullPath(file), fileName));
                        }
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Lọc theo định dạng: AllowedExtensions ("*.docx;*.pdf", ".txt", "cs") và ExcludedPatterns ("*.tmp;bin;obj").
        /// </summary>
        public static bool IsFileAllowed(string relativeOrFileName, string allowedExtensions, string excludedPatterns)
        {
            string ext = Path.GetExtension(relativeOrFileName);

            // Kiểm tra excluded patterns trước
            if (!string.IsNullOrWhiteSpace(excludedPatterns))
            {
                var exPatterns = excludedPatterns.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var pattern in exPatterns)
                {
                    string p = pattern.Trim();
                    if (string.IsNullOrEmpty(p)) continue;

                    if (p.StartsWith("*.") && ext.Equals(p.Substring(1), StringComparison.OrdinalIgnoreCase))
                        return false;

                    if (relativeOrFileName.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                        return false;
                }
            }

            // Kiểm tra allowed extensions
            if (string.IsNullOrWhiteSpace(allowedExtensions) || allowedExtensions.Trim() == "*.*" || allowedExtensions.Trim() == "*")
            {
                return true;
            }

            var allowedList = allowedExtensions.Split(new[] { ';', ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in allowedList)
            {
                string pattern = item.Trim();
                if (pattern == "*.*" || pattern == "*") return true;

                if (pattern.StartsWith("*."))
                {
                    if (ext.Equals(pattern.Substring(1), StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (pattern.StartsWith("."))
                {
                    if (ext.Equals(pattern, StringComparison.OrdinalIgnoreCase)) return true;
                }
                else
                {
                    if (ext.Equals("." + pattern, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            return false;
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        private sealed class PlannedFile
        {
            public string FullPath { get; set; } = string.Empty;
            public string RelativePath { get; set; } = string.Empty;
            public string? Sha256 { get; set; }
            public string ChangeType { get; set; } = "MODIFIED";
            public long SizeBytes { get; set; }
            public DateTime LastWriteTimeUtc { get; set; }
        }

        /// <summary>
        /// IProgress gọi callback ngay trên luồng hiện tại (Progress&lt;T&gt; mặc định post sang ThreadPool,
        /// làm thứ tự báo cáo bị đảo khi chạy trong BackgroundWorker).
        /// </summary>
        private sealed class SynchronousProgress<T> : IProgress<T>
        {
            private readonly Action<T> _handler;
            public SynchronousProgress(Action<T> handler) { _handler = handler; }
            public void Report(T value) => _handler(value);
        }
    }
}
