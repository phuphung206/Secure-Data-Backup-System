using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    }

    /// <summary>
    /// [Week 3] Bộ điều phối BackgroundWorker đồng bộ với Progress Bar chống treo giao diện UI khi truyền file nặng.
    /// Tích hợp Full/Incremental Backup, lọc file, mã hóa AES-256 trước khi gửi và AI Chống Ransomware.
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

        public void StartBackup(NetworkBackupWorkerArgs args)
        {
            if (IsBusy)
                throw new InvalidOperationException("Tiến trình sao lưu mạng đang bận thực hiện tác vụ trước.");

            _cts = new CancellationTokenSource();
            RunWorkerAsync(args);
        }

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
                // Bước 1: Quét và thu thập danh sách file ứng viên
                ReportProgress(0, new TransferProgressReport
                {
                    StatusMessage = "Đang quét danh sách file và kiểm tra bộ lọc...",
                    ProgressPercentage = 0
                });

                var candidateFiles = ScanCandidateFiles(args);
                if (candidateFiles.Count == 0)
                {
                    result.Success = true;
                    result.Message = "Không tìm thấy file nào phù hợp với bộ lọc để sao lưu.";
                    e.Result = result;
                    return;
                }

                // Bước 2: AI Chống Ransomware (Anomaly Detection)
                if (args.EnableAiRansomwareProtection)
                {
                    ReportProgress(1, new TransferProgressReport
                    {
                        StatusMessage = "🤖 AI đang phân tích độ bất thường & quét mã độc Ransomware...",
                        ProgressPercentage = 1
                    });

                    var rawFilePaths = candidateFiles.Select(f => f.FullPath).ToList();
                    var aiResult = RansomwareProtectionService.AnalyzeFiles(args.SourcePath, rawFilePaths);

                    if (aiResult.IsRansomwareDetected)
                    {
                        result.Success = false;
                        result.RansomwareThreat = aiResult;
                        result.Message = $"⛔ CHẶN SAO LƯU: Phát hiện nguy cơ Ransomware (Điểm bất thường: {aiResult.AnomalyScore:F2})!\n{aiResult.Message}";

                        ReportProgress(0, new TransferProgressReport
                        {
                            StatusMessage = "⛔ PHÁT HIỆN RANSOMWARE! Đã ngắt tiến trình sao lưu để bảo vệ Server.",
                            ProgressPercentage = 0
                        });

                        throw new RansomwareThreatDetectedException(aiResult);
                    }
                }

                // Bước 3: Lọc Incremental Backup (bỏ qua các file không thay đổi so với bản trước)
                int skippedCount = 0;
                var filesToTransfer = new List<(string FullPath, string RelativePath)>();
                var localState = LoadClientState(args.SourcePath);

                if (args.IsIncremental && localState != null && localState.Count > 0)
                {
                    ReportProgress(3, new TransferProgressReport
                    {
                        StatusMessage = "Đang so sánh mã băm SHA-256 để chọn file thay đổi (Incremental Backup)...",
                        ProgressPercentage = 3
                    });

                    foreach (var file in candidateFiles)
                    {
                        var fi = new FileInfo(file.FullPath);
                        if (localState.TryGetValue(file.RelativePath, out var savedMeta))
                        {
                            // Kiểm tra dung lượng và thời gian sửa đổi trước
                            if (savedMeta.SizeBytes == fi.Length && savedMeta.LastWriteTimeUtc == fi.LastWriteTimeUtc)
                            {
                                skippedCount++;
                                continue;
                            }

                            // Nếu thời gian khác, kiểm tra mã băm SHA-256
                            string currentSha = ComputeFileSha256(file.FullPath);
                            if (string.Equals(currentSha, savedMeta.Sha256, StringComparison.OrdinalIgnoreCase))
                            {
                                skippedCount++;
                                continue;
                            }
                        }

                        // File mới hoặc có thay đổi nội dung
                        filesToTransfer.Add(file);
                    }

                    if (filesToTransfer.Count == 0)
                    {
                        result.Success = true;
                        result.SkippedFilesCount = skippedCount;
                        result.Message = $"Dữ liệu không có thay đổi kể từ lần sao lưu trước ({skippedCount} file đã được bỏ qua để tiết kiệm băng thông).";
                        e.Result = result;
                        return;
                    }
                }
                else
                {
                    // Full backup: sao lưu tất cả file ứng viên
                    filesToTransfer = candidateFiles;
                }

                result.SkippedFilesCount = skippedCount;

                // Bước 4: Kết nối tới TCP Server
                ReportProgress(5, new TransferProgressReport
                {
                    StatusMessage = $"Đang kết nối tới máy chủ {args.ServerHost}:{args.ServerPort}...",
                    ProgressPercentage = 5
                });

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

                // Tính tổng dung lượng cần truyền
                long totalBytesAllFiles = 0;
                foreach (var item in filesToTransfer)
                {
                    totalBytesAllFiles += new FileInfo(item.FullPath).Length;
                }

                int completedFiles = 0;
                long totalBytesTransferredAccumulated = 0;
                var updatedState = localState ?? new Dictionary<string, FileSnapshotMeta>(StringComparer.OrdinalIgnoreCase);

                // Bước 5: Vòng lặp truyền từng file qua Socket TCP
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
                    var originalFi = new FileInfo(currentItem.FullPath);
                    long currentFileLength = originalFi.Length;

                    string fileToSendPath = currentItem.FullPath;
                    string relativePathToSend = currentItem.RelativePath;
                    string? tempEncryptedPath = null;

                    // Nếu bật mã hóa Client-Side AES-256
                    if (args.EncryptBeforeTransfer && !string.IsNullOrWhiteSpace(args.EncryptionPassword))
                    {
                        tempEncryptedPath = Path.Combine(Path.GetTempPath(), $"sdb_enc_{Guid.NewGuid():N}.enc");
                        AesEncryptionHelper.EncryptFile(currentItem.FullPath, tempEncryptedPath, args.EncryptionPassword);
                        fileToSendPath = tempEncryptedPath;
                        relativePathToSend = currentItem.RelativePath + ".enc";
                        currentFileLength = new FileInfo(tempEncryptedPath).Length;
                    }

                    try
                    {
                        var fileProgress = new Progress<TransferProgressReport>(report =>
                        {
                            long overallBytes = totalBytesTransferredAccumulated + report.BytesTransferred;
                            int overallPercent = totalBytesAllFiles > 0
                                ? (int)((overallBytes * 100) / totalBytesAllFiles)
                                : 100;

                            string encTag = args.EncryptBeforeTransfer ? "[AES-256] " : string.Empty;
                            report.StatusMessage = $"[File {i + 1}/{filesToTransfer.Count}] {encTag}{report.FileName}: {report.StatusMessage}";
                            report.ProgressPercentage = Math.Min(100, overallPercent);

                            ReportProgress(report.ProgressPercentage, report);
                        });

                        bool fileOk = _client.UploadFileAsync(
                            fileToSendPath,
                            relativePathToSend,
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

                        // Cập nhật snapshot cho Incremental tracking
                        updatedState[currentItem.RelativePath] = new FileSnapshotMeta
                        {
                            SizeBytes = originalFi.Length,
                            LastWriteTimeUtc = originalFi.LastWriteTimeUtc,
                            Sha256 = ComputeFileSha256(currentItem.FullPath)
                        };
                    }
                    finally
                    {
                        if (tempEncryptedPath != null && File.Exists(tempEncryptedPath))
                        {
                            try { File.Delete(tempEncryptedPath); } catch { }
                        }
                    }
                }

                // Lưu lại state để phục vụ lần Incremental tiếp theo
                SaveClientState(args.SourcePath, updatedState);

                result.Success = true;
                result.TransferredFilesCount = completedFiles;
                result.TotalBytesTransferred = totalBytesTransferredAccumulated;
                string encInfo = args.EncryptBeforeTransfer ? " (Đã mã hóa AES-256)" : string.Empty;
                string skipInfo = skippedCount > 0 ? $" (Đã bỏ qua {skippedCount} file không đổi)" : string.Empty;
                result.Message = $"Sao lưu thành công {completedFiles} file{encInfo}{skipInfo} ({TransferProgressReport.FormatBytes(totalBytesTransferredAccumulated)}) qua mạng TCP!";
                e.Result = result;
            }
            catch (OperationCanceledException)
            {
                e.Cancel = true;
                result.IsCancelled = true;
                result.Message = "Tác vụ đã được hủy bởi người dùng.";
                e.Result = result;
            }
            catch (RansomwareThreatDetectedException ex)
            {
                result.Success = false;
                result.Error = ex;
                result.RansomwareThreat = ex.DetectionResult;
                result.Message = ex.Message;
                e.Result = result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex;
                result.Message = $"Lỗi sao lưu: {ex.Message}";
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

        private List<(string FullPath, string RelativePath)> ScanCandidateFiles(NetworkBackupWorkerArgs args)
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
                string[] allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);

                foreach (var file in allFiles)
                {
                    string relative = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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

        private static bool IsFileAllowed(string relativeOrFileName, string allowedExtensions, string excludedPatterns)
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

        private static string ComputeFileSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] hash = sha.ComputeHash(fs);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private static string GetStateFilePath(string sourcePath)
        {
            string hash = BitConverter.ToString(SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(sourcePath.ToLowerInvariant())))
                .Replace("-", "").Substring(0, 16);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string stateDir = Path.Combine(appData, "SecureDataBackup", "ClientState");
            if (!Directory.Exists(stateDir)) Directory.CreateDirectory(stateDir);
            return Path.Combine(stateDir, $"state_{hash}.json");
        }

        private static Dictionary<string, FileSnapshotMeta>? LoadClientState(string sourcePath)
        {
            try
            {
                string stateFile = GetStateFilePath(sourcePath);
                if (File.Exists(stateFile))
                {
                    string json = File.ReadAllText(stateFile);
                    return JsonSerializer.Deserialize<Dictionary<string, FileSnapshotMeta>>(json);
                }
            }
            catch { }
            return null;
        }

        private static void SaveClientState(string sourcePath, Dictionary<string, FileSnapshotMeta> state)
        {
            try
            {
                string stateFile = GetStateFilePath(sourcePath);
                string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(stateFile, json);
            }
            catch { }
        }

        private sealed class FileSnapshotMeta
        {
            public long SizeBytes { get; set; }
            public DateTime LastWriteTimeUtc { get; set; }
            public string Sha256 { get; set; } = string.Empty;
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
