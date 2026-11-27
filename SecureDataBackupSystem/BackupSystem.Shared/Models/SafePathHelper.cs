using System;
using System.IO;
using System.Text.RegularExpressions;

namespace BackupSystem.Shared.Models
{
    /// <summary>
    /// Kiểm tra và chuẩn hóa đường dẫn nhận từ mạng để chống tấn công Path Traversal ("..\..\Windows").
    /// Mọi đường dẫn tương đối do Client gửi lên đều phải đi qua lớp này trước khi ghép với thư mục gốc.
    /// </summary>
    public static class SafePathHelper
    {
        private static readonly Regex BackupIdPattern = new Regex("^[A-Za-z0-9_.-]{1,128}$", RegexOptions.Compiled);

        /// <summary>
        /// BackupId chỉ được chứa chữ, số và các ký tự _ . - (không có dấu phân cách thư mục, không phải "." hoặc "..").
        /// </summary>
        public static bool IsValidBackupId(string? backupId)
        {
            if (string.IsNullOrWhiteSpace(backupId))
                return false;
            if (backupId == "." || backupId == "..")
                return false;
            return BackupIdPattern.IsMatch(backupId);
        }

        /// <summary>
        /// Chuẩn hóa đường dẫn tương đối (dùng '/' làm phân cách) và từ chối các đường dẫn nguy hiểm:
        /// đường dẫn tuyệt đối, ổ đĩa (C:), UNC, thành phần "..", ký tự không hợp lệ.
        /// </summary>
        public static string NormalizeRelativePath(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidDataException("Đường dẫn tương đối không được để trống.");

            string unified = relativePath!.Replace('\\', '/');

            if (unified.StartsWith("/", StringComparison.Ordinal) || unified.IndexOf(':') >= 0)
                throw new InvalidDataException($"Đường dẫn '{relativePath}' không được là đường dẫn tuyệt đối.");

            char[] invalidChars = Path.GetInvalidFileNameChars();
            string[] parts = unified.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                throw new InvalidDataException("Đường dẫn tương đối không hợp lệ.");

            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length == 0 || trimmed == "." || trimmed == "..")
                    throw new InvalidDataException($"Đường dẫn '{relativePath}' chứa thành phần không hợp lệ ('{part}').");

                if (part.IndexOfAny(invalidChars) >= 0)
                    throw new InvalidDataException($"Đường dẫn '{relativePath}' chứa ký tự không hợp lệ.");
            }

            return string.Join("/", parts);
        }

        /// <summary>
        /// Ghép thư mục gốc với các thành phần đường dẫn và bảo đảm kết quả vẫn nằm BÊN TRONG thư mục gốc.
        /// </summary>
        public static string CombineUnderRoot(string root, params string[] relativeParts)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("Thư mục gốc không hợp lệ.", nameof(root));

            string rootFull = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string combined = rootFull;
            foreach (string part in relativeParts)
            {
                string normalized = NormalizeRelativePath(part)
                    .Replace('/', Path.DirectorySeparatorChar);
                combined = Path.Combine(combined, normalized);
            }

            string full = Path.GetFullPath(combined);
            if (!IsUnderRoot(rootFull, full))
                throw new InvalidDataException("Đường dẫn vượt ra ngoài thư mục lưu trữ cho phép.");

            return full;
        }

        /// <summary>
        /// Kiểm tra một đường dẫn tuyệt đối có nằm trong thư mục gốc hay không.
        /// </summary>
        public static bool IsUnderRoot(string root, string fullPath)
        {
            string rootFull = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(fullPath);

            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return candidate.StartsWith(rootFull, comparison);
        }
    }
}
