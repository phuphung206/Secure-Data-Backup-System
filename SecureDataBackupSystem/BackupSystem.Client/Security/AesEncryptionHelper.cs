using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BackupSystem.Client.Security
{
    /// <summary>
    /// Module mã hóa bảo mật AES-256 Client-Side.
    /// Dữ liệu được mã hóa ngay tại máy người dùng trước khi truyền qua mạng TCP hoặc lưu lên Server.
    /// Kẻ tấn công bắt gói tin trên đường truyền hoặc xâm nhập máy chủ lưu trữ cũng không thể giải mã nếu không có khóa của Client.
    /// </summary>
    public static class AesEncryptionHelper
    {
        private static readonly byte[] HeaderMagic = Encoding.ASCII.GetBytes("SDBAES256"); // 9 bytes
        private const int KeySizeBytes = 32; // 256 bits
        private const int IvSizeBytes = 16;  // 128 bits
        private const int SaltSizeBytes = 16;
        private const int Pbkdf2Iterations = 100000;

        /// <summary>
        /// Kiểm tra file có phải định dạng mã hóa AES-256 của hệ thống hay không.
        /// </summary>
        public static bool IsEncrypted(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < HeaderMagic.Length + SaltSizeBytes + IvSizeBytes) return false;
                    byte[] header = new byte[HeaderMagic.Length];
                    fs.Read(header, 0, header.Length);
                    for (int i = 0; i < HeaderMagic.Length; i++)
                    {
                        if (header[i] != HeaderMagic[i]) return false;
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Mã hóa file bằng thuật toán AES-256 với khóa phái sinh từ mật khẩu qua PBKDF2 (100,000 iterations).
        /// </summary>
        public static void EncryptFile(string inputPath, string outputPath, string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Mật khẩu mã hóa không được để trống.", nameof(password));

            if (!File.Exists(inputPath))
                throw new FileNotFoundException("File nguồn không tồn tại.", inputPath);

            byte[] salt = new byte[SaltSizeBytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            using (var deriveBytes = new Rfc2898DeriveBytes(password, salt, Pbkdf2Iterations))
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Key = deriveBytes.GetBytes(KeySizeBytes);
                aes.GenerateIV();

                string? outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                {
                    Directory.CreateDirectory(outDir);
                }

                using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    // Ghi Header Magic + Salt + IV
                    outStream.Write(HeaderMagic, 0, HeaderMagic.Length);
                    outStream.Write(salt, 0, salt.Length);
                    outStream.Write(aes.IV, 0, aes.IV.Length);

                    using (var cryptoStream = new CryptoStream(outStream, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    using (var inStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        inStream.CopyTo(cryptoStream);
                    }
                }
            }
        }

        /// <summary>
        /// Giải mã file AES-256 bằng mật khẩu người dùng.
        /// </summary>
        public static void DecryptFile(string inputPath, string outputPath, string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Vui lòng nhập mật khẩu giải mã.", nameof(password));

            if (!File.Exists(inputPath))
                throw new FileNotFoundException("File mã hóa không tồn tại.", inputPath);

            using (var inStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] header = new byte[HeaderMagic.Length];
                if (inStream.Read(header, 0, header.Length) != header.Length)
                    throw new InvalidDataException("File mã hóa không đúng định dạng.");

                for (int i = 0; i < HeaderMagic.Length; i++)
                {
                    if (header[i] != HeaderMagic[i])
                        throw new InvalidDataException("Magic header không khớp.");
                }

                byte[] salt = new byte[SaltSizeBytes];
                inStream.Read(salt, 0, salt.Length);

                byte[] iv = new byte[IvSizeBytes];
                inStream.Read(iv, 0, iv.Length);

                using (var deriveBytes = new Rfc2898DeriveBytes(password, salt, Pbkdf2Iterations))
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Key = deriveBytes.GetBytes(KeySizeBytes);
                    aes.IV = iv;

                    string? outDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    {
                        Directory.CreateDirectory(outDir);
                    }

                    using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var cryptoStream = new CryptoStream(inStream, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        cryptoStream.CopyTo(outStream);
                    }
                }
            }
        }
    }
}
