using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using BackupSystem.Client.Security;

namespace BackupSystem.Client.NetworkClient
{
    /// <summary>
    /// Kết quả chuẩn bị một file trước khi gửi: đường dẫn file thực sự sẽ truyền (đã nén/mã hóa nếu bật)
    /// và các cờ cho biết phép biến đổi nào đã được áp dụng.
    /// </summary>
    public sealed class PreparedBackupFile : IDisposable
    {
        private readonly List<string> _tempFiles = new List<string>();

        public string SourcePath { get; internal set; } = string.Empty;
        public string PayloadPath { get; internal set; } = string.Empty;
        public string StoredRelativePath { get; internal set; } = string.Empty;
        public bool IsCompressed { get; internal set; }
        public bool IsEncrypted { get; internal set; }
        public long OriginalSizeBytes { get; internal set; }
        public long PayloadSizeBytes { get; internal set; }

        internal void TrackTemp(string path) => _tempFiles.Add(path);

        public void Dispose()
        {
            foreach (string temp in _tempFiles)
            {
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch
                {
                }
            }
            _tempFiles.Clear();
        }
    }

    /// <summary>
    /// Pipeline biến đổi file ở phía Client: Nén GZip → Mã hóa AES-256 → (gửi qua mạng).
    /// Khi khôi phục thì chạy ngược lại: Giải mã → Giải nén → kiểm tra SHA-256 của file gốc.
    /// Nén trước rồi mới mã hóa vì dữ liệu đã mã hóa gần như ngẫu nhiên, không thể nén được nữa.
    /// </summary>
    public static class BackupFileTransformer
    {
        public const string CompressedSuffix = ".gz";
        public const string EncryptedSuffix = ".enc";

        /// <summary>
        /// Các định dạng vốn đã nén sẵn: nén thêm chỉ tốn CPU mà không giảm dung lượng.
        /// </summary>
        private static readonly HashSet<string> AlreadyCompressedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".gz", ".tgz", ".bz2", ".xz", ".zst", ".cab",
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic",
            ".mp3", ".aac", ".ogg", ".flac", ".m4a",
            ".mp4", ".mkv", ".avi", ".mov", ".webm",
            ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp", ".apk", ".jar", ".epub",
            ".enc"
        };

        /// <summary>
        /// Chỉ giữ bản nén nếu nhỏ hơn ít nhất 3% so với bản gốc.
        /// </summary>
        private const double MinimumSavingRatio = 0.03;

        public static bool ShouldCompress(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            return !AlreadyCompressedExtensions.Contains(ext);
        }

        /// <summary>
        /// Chuẩn bị file để gửi: nén (nếu có lợi) rồi mã hóa (nếu bật). File tạm sẽ bị xóa khi Dispose.
        /// </summary>
        public static PreparedBackupFile Prepare(
            string sourcePath,
            string relativePath,
            bool enableCompression,
            bool encrypt,
            string? password)
        {
            if (encrypt && string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Cần mật khẩu để mã hóa AES-256 trước khi gửi.", nameof(password));

            var prepared = new PreparedBackupFile
            {
                SourcePath = sourcePath,
                PayloadPath = sourcePath,
                StoredRelativePath = relativePath,
                OriginalSizeBytes = new FileInfo(sourcePath).Length
            };

            try
            {
                if (enableCompression && prepared.OriginalSizeBytes > 0 && ShouldCompress(sourcePath))
                {
                    string gzPath = NewTempPath(".gz");
                    prepared.TrackTemp(gzPath);
                    CompressFile(sourcePath, gzPath);

                    long compressedSize = new FileInfo(gzPath).Length;
                    if (compressedSize < prepared.OriginalSizeBytes * (1.0 - MinimumSavingRatio))
                    {
                        prepared.PayloadPath = gzPath;
                        prepared.IsCompressed = true;
                        prepared.StoredRelativePath += CompressedSuffix;
                    }
                }

                if (encrypt)
                {
                    string encPath = NewTempPath(".enc");
                    prepared.TrackTemp(encPath);
                    AesEncryptionHelper.EncryptFile(prepared.PayloadPath, encPath, password!);
                    prepared.PayloadPath = encPath;
                    prepared.IsEncrypted = true;
                    prepared.StoredRelativePath += EncryptedSuffix;
                }

                prepared.PayloadSizeBytes = new FileInfo(prepared.PayloadPath).Length;
                return prepared;
            }
            catch
            {
                prepared.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Biến đổi ngược file tải về từ Server: giải mã → giải nén → kiểm tra SHA-256 gốc → ghi ra đích.
        /// </summary>
        public static void Restore(
            string downloadedPath,
            string destinationPath,
            bool isEncrypted,
            bool isCompressed,
            string? password,
            string? expectedOriginalSha256 = null)
        {
            var temps = new List<string>();
            try
            {
                string current = downloadedPath;

                if (isEncrypted)
                {
                    if (string.IsNullOrWhiteSpace(password))
                        throw new InvalidOperationException("File được mã hóa AES-256, vui lòng nhập mật khẩu giải mã.");

                    string decrypted = NewTempPath(".dec");
                    temps.Add(decrypted);
                    AesEncryptionHelper.DecryptFile(current, decrypted, password!);
                    current = decrypted;
                }

                if (isCompressed)
                {
                    string decompressed = NewTempPath(".raw");
                    temps.Add(decompressed);
                    DecompressFile(current, decompressed);
                    current = decompressed;
                }

                if (!string.IsNullOrWhiteSpace(expectedOriginalSha256))
                {
                    string actual = ComputeSha256(current);
                    if (!string.Equals(actual, expectedOriginalSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            $"SHA-256 của file khôi phục không khớp bản gốc (kỳ vọng {expectedOriginalSha256}, thực tế {actual}).");
                }

                string? destDir = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);

                if (string.Equals(current, downloadedPath, StringComparison.OrdinalIgnoreCase))
                    File.Copy(current, destinationPath);
                else
                    File.Move(current, destinationPath);
            }
            finally
            {
                foreach (string temp in temps)
                {
                    try
                    {
                        if (File.Exists(temp)) File.Delete(temp);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static void CompressFile(string sourcePath, string outputPath)
        {
            using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            {
                input.CopyTo(gzip, 81920);
            }
        }

        public static void DecompressFile(string compressedPath, string outputPath)
        {
            using (var input = new FileStream(compressedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                gzip.CopyTo(output, 81920);
            }
        }

        public static string ComputeSha256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] hash = sha.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string NewTempPath(string extension)
        {
            return Path.Combine(Path.GetTempPath(), "sdb_" + Guid.NewGuid().ToString("N") + extension);
        }
    }
}
