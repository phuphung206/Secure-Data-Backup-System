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
    ///
    /// Định dạng v2 (mặc định khi mã hóa) - mã hóa có xác thực (Encrypt-then-MAC):
    ///   "SDBAESv2" (8) | PBKDF2 iterations (int32 LE, 4) | Salt (16) | IV (16) | Ciphertext AES-256-CBC | HMAC-SHA256 (32)
    ///   - Khóa 64 byte phái sinh bằng PBKDF2-HMAC-SHA256: 32 byte cho AES-256, 32 byte cho HMAC.
    ///   - HMAC bao phủ toàn bộ header + ciphertext và được kiểm tra TRƯỚC khi giải mã, nên sai mật khẩu
    ///     hoặc dữ liệu bị sửa trên Server/đường truyền đều bị phát hiện chắc chắn (không phụ thuộc padding).
    ///   - Dùng các API có sẵn trên cả .NET Framework 4.7.2 (WinForms) và .NET 10 (AesGcm không có trên 4.7.2).
    ///
    /// Định dạng cũ "SDBAES256" (AES-CBC không xác thực) vẫn được hỗ trợ khi GIẢI MÃ để khôi phục bản backup cũ.
    /// </summary>
    public static class AesEncryptionHelper
    {
        private static readonly byte[] HeaderMagicV2 = Encoding.ASCII.GetBytes("SDBAESv2");   // 8 bytes
        private static readonly byte[] HeaderMagicLegacy = Encoding.ASCII.GetBytes("SDBAES256"); // 9 bytes

        private const int AesKeySizeBytes = 32;   // 256 bits
        private const int MacKeySizeBytes = 32;
        private const int IvSizeBytes = 16;       // 128 bits
        private const int SaltSizeBytes = 16;
        private const int TagSizeBytes = 32;      // HMAC-SHA256
        private const int LegacyPbkdf2Iterations = 100000;
        private const int MinIterations = 10000;
        private const int MaxIterations = 5000000;
        private const int BufferSize = 81920;

        /// <summary>Số vòng PBKDF2 dùng cho file mới (có thể giảm trong kiểm thử).</summary>
        public static int Pbkdf2Iterations { get; set; } = 200000;

        private const int HeaderSizeV2 = 8 + 4 + SaltSizeBytes + IvSizeBytes;

        /// <summary>
        /// Kiểm tra file có phải định dạng mã hóa AES-256 của hệ thống hay không (v2 hoặc định dạng cũ).
        /// </summary>
        public static bool IsEncrypted(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] header = new byte[HeaderMagicLegacy.Length];
                    int read = ReadFully(fs, header, 0, header.Length);

                    if (read >= HeaderMagicV2.Length && StartsWith(header, HeaderMagicV2))
                        return fs.Length >= HeaderSizeV2 + TagSizeBytes;

                    if (read == HeaderMagicLegacy.Length && StartsWith(header, HeaderMagicLegacy))
                        return fs.Length >= HeaderMagicLegacy.Length + SaltSizeBytes + IvSizeBytes;

                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Mã hóa file bằng AES-256-CBC + HMAC-SHA256 (Encrypt-then-MAC), khóa phái sinh từ mật khẩu qua PBKDF2-SHA256.
        /// </summary>
        public static void EncryptFile(string inputPath, string outputPath, string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Mật khẩu mã hóa không được để trống.", nameof(password));

            if (!File.Exists(inputPath))
                throw new FileNotFoundException("File nguồn không tồn tại.", inputPath);

            int iterations = Pbkdf2Iterations;
            if (iterations < MinIterations || iterations > MaxIterations)
                throw new InvalidOperationException("Số vòng PBKDF2 không hợp lệ.");

            byte[] salt = RandomBytes(SaltSizeBytes);
            byte[] iv = RandomBytes(IvSizeBytes);
            DeriveKeys(password, salt, iterations, out byte[] aesKey, out byte[] macKey);

            EnsureParentDirectory(outputPath);

            try
            {
                using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                using (var hmac = new HMACSHA256(macKey))
                {
                    byte[] header = new byte[HeaderSizeV2];
                    Buffer.BlockCopy(HeaderMagicV2, 0, header, 0, HeaderMagicV2.Length);
                    WriteInt32LittleEndian(header, HeaderMagicV2.Length, iterations);
                    Buffer.BlockCopy(salt, 0, header, 12, SaltSizeBytes);
                    Buffer.BlockCopy(iv, 0, header, 12 + SaltSizeBytes, IvSizeBytes);

                    outStream.Write(header, 0, header.Length);
                    hmac.TransformBlock(header, 0, header.Length, null, 0);

                    using (var aes = CreateAes(aesKey, iv))
                    using (var encryptor = aes.CreateEncryptor())
                    using (var macStream = new MacTeeStream(outStream, hmac))
                    using (var cryptoStream = new CryptoStream(macStream, encryptor, CryptoStreamMode.Write))
                    using (var inStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        inStream.CopyTo(cryptoStream, BufferSize);
                        cryptoStream.FlushFinalBlock();
                    }

                    hmac.TransformFinalBlock(new byte[0], 0, 0);
                    byte[] tag = hmac.Hash!;
                    outStream.Write(tag, 0, tag.Length);
                    outStream.Flush(true);
                }
            }
            catch
            {
                TryDelete(outputPath);
                throw;
            }
            finally
            {
                Array.Clear(aesKey, 0, aesKey.Length);
                Array.Clear(macKey, 0, macKey.Length);
            }
        }

        /// <summary>
        /// Giải mã file AES-256 bằng mật khẩu người dùng.
        /// Ném <see cref="CryptographicException"/> khi sai mật khẩu hoặc dữ liệu đã bị thay đổi.
        /// </summary>
        public static void DecryptFile(string inputPath, string outputPath, string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Vui lòng nhập mật khẩu giải mã.", nameof(password));

            if (!File.Exists(inputPath))
                throw new FileNotFoundException("File mã hóa không tồn tại.", inputPath);

            using (var inStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] probe = new byte[HeaderMagicLegacy.Length];
                int probeRead = ReadFully(inStream, probe, 0, probe.Length);
                inStream.Seek(0, SeekOrigin.Begin);

                if (probeRead >= HeaderMagicV2.Length && StartsWith(probe, HeaderMagicV2))
                {
                    DecryptV2(inStream, outputPath, password);
                    return;
                }

                if (probeRead == HeaderMagicLegacy.Length && StartsWith(probe, HeaderMagicLegacy))
                {
                    DecryptLegacy(inStream, outputPath, password);
                    return;
                }

                throw new InvalidDataException("File mã hóa không đúng định dạng của hệ thống (magic header không khớp).");
            }
        }

        private static void DecryptV2(FileStream inStream, string outputPath, string password)
        {
            long totalLength = inStream.Length;
            if (totalLength < HeaderSizeV2 + TagSizeBytes)
                throw new InvalidDataException("File mã hóa bị cắt cụt hoặc không hợp lệ.");

            byte[] header = new byte[HeaderSizeV2];
            if (ReadFully(inStream, header, 0, header.Length) != header.Length)
                throw new InvalidDataException("Không đọc được header file mã hóa.");

            int iterations = ReadInt32LittleEndian(header, HeaderMagicV2.Length);
            if (iterations < MinIterations || iterations > MaxIterations)
                throw new InvalidDataException("Tham số PBKDF2 trong file mã hóa không hợp lệ.");

            byte[] salt = new byte[SaltSizeBytes];
            byte[] iv = new byte[IvSizeBytes];
            Buffer.BlockCopy(header, 12, salt, 0, SaltSizeBytes);
            Buffer.BlockCopy(header, 12 + SaltSizeBytes, iv, 0, IvSizeBytes);

            long cipherLength = totalLength - HeaderSizeV2 - TagSizeBytes;
            if (cipherLength <= 0 || cipherLength % 16 != 0)
                throw new InvalidDataException("Độ dài dữ liệu mã hóa không hợp lệ.");

            DeriveKeys(password, salt, iterations, out byte[] aesKey, out byte[] macKey);
            try
            {
                // Bước 1: Xác thực HMAC trên toàn bộ header + ciphertext TRƯỚC khi giải mã
                byte[] storedTag = new byte[TagSizeBytes];
                inStream.Seek(totalLength - TagSizeBytes, SeekOrigin.Begin);
                if (ReadFully(inStream, storedTag, 0, TagSizeBytes) != TagSizeBytes)
                    throw new InvalidDataException("Không đọc được mã xác thực HMAC.");

                byte[] computedTag;
                using (var hmac = new HMACSHA256(macKey))
                {
                    inStream.Seek(0, SeekOrigin.Begin);
                    byte[] buffer = new byte[BufferSize];
                    long remaining = totalLength - TagSizeBytes;
                    while (remaining > 0)
                    {
                        int toRead = (int)Math.Min(buffer.Length, remaining);
                        int read = inStream.Read(buffer, 0, toRead);
                        if (read <= 0) throw new EndOfStreamException();
                        hmac.TransformBlock(buffer, 0, read, null, 0);
                        remaining -= read;
                    }
                    hmac.TransformFinalBlock(new byte[0], 0, 0);
                    computedTag = hmac.Hash!;
                }

                if (!FixedTimeEquals(storedTag, computedTag))
                    throw new CryptographicException("Mật khẩu không đúng hoặc dữ liệu sao lưu đã bị thay đổi (HMAC không khớp).");

                // Bước 2: Giải mã phần ciphertext
                EnsureParentDirectory(outputPath);
                try
                {
                    inStream.Seek(HeaderSizeV2, SeekOrigin.Begin);
                    using (var aes = CreateAes(aesKey, iv))
                    using (var decryptor = aes.CreateDecryptor())
                    using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var cryptoStream = new CryptoStream(outStream, decryptor, CryptoStreamMode.Write))
                    {
                        byte[] buffer = new byte[BufferSize];
                        long remaining = cipherLength;
                        while (remaining > 0)
                        {
                            int toRead = (int)Math.Min(buffer.Length, remaining);
                            int read = inStream.Read(buffer, 0, toRead);
                            if (read <= 0) throw new EndOfStreamException();
                            cryptoStream.Write(buffer, 0, read);
                            remaining -= read;
                        }
                        cryptoStream.FlushFinalBlock();
                    }
                }
                catch
                {
                    TryDelete(outputPath);
                    throw;
                }
            }
            finally
            {
                Array.Clear(aesKey, 0, aesKey.Length);
                Array.Clear(macKey, 0, macKey.Length);
            }
        }

        /// <summary>
        /// Giải mã định dạng cũ "SDBAES256" (AES-CBC, chưa có MAC) để tương thích các bản backup đã tạo trước đây.
        /// </summary>
        private static void DecryptLegacy(FileStream inStream, string outputPath, string password)
        {
            inStream.Seek(HeaderMagicLegacy.Length, SeekOrigin.Begin);

            byte[] salt = new byte[SaltSizeBytes];
            byte[] iv = new byte[IvSizeBytes];
            if (ReadFully(inStream, salt, 0, salt.Length) != salt.Length ||
                ReadFully(inStream, iv, 0, iv.Length) != iv.Length)
                throw new InvalidDataException("File mã hóa (định dạng cũ) bị cắt cụt.");

            EnsureParentDirectory(outputPath);
            try
            {
                using (var deriveBytes = new Rfc2898DeriveBytes(password, salt, LegacyPbkdf2Iterations))
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Key = deriveBytes.GetBytes(AesKeySizeBytes);
                    aes.IV = iv;

                    using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var cryptoStream = new CryptoStream(inStream, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        cryptoStream.CopyTo(outStream, BufferSize);
                    }
                }
            }
            catch (Exception ex)
            {
                TryDelete(outputPath);
                if (ex is CryptographicException)
                    throw;
                throw new CryptographicException("Mật khẩu không đúng hoặc dữ liệu sao lưu (định dạng cũ) bị hỏng.", ex);
            }
        }

        #region Helpers

        private static void DeriveKeys(string password, byte[] salt, int iterations, out byte[] aesKey, out byte[] macKey)
        {
            using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                byte[] material = kdf.GetBytes(AesKeySizeBytes + MacKeySizeBytes);
                aesKey = new byte[AesKeySizeBytes];
                macKey = new byte[MacKeySizeBytes];
                Buffer.BlockCopy(material, 0, aesKey, 0, AesKeySizeBytes);
                Buffer.BlockCopy(material, AesKeySizeBytes, macKey, 0, MacKeySizeBytes);
                Array.Clear(material, 0, material.Length);
            }
        }

        private static Aes CreateAes(byte[] key, byte[] iv)
        {
            var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;
            return aes;
        }

        private static byte[] RandomBytes(int count)
        {
            byte[] bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return bytes;
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static bool StartsWith(byte[] data, byte[] prefix)
        {
            if (data.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (data[i] != prefix[i]) return false;
            }
            return true;
        }

        private static int ReadFully(Stream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, offset + total, count - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        private static void WriteInt32LittleEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static int ReadInt32LittleEndian(byte[] buffer, int offset)
        {
            return buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24);
        }

        private static void EnsureParentDirectory(string path)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Stream trung gian: ghi dữ liệu ciphertext ra file đồng thời cập nhật HMAC (Encrypt-then-MAC dạng streaming).
        /// </summary>
        private sealed class MacTeeStream : Stream
        {
            private readonly Stream _inner;
            private readonly HMAC _hmac;

            public MacTeeStream(Stream inner, HMAC hmac)
            {
                _inner = inner;
                _hmac = hmac;
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (count <= 0) return;
                _hmac.TransformBlock(buffer, offset, count, null, 0);
                _inner.Write(buffer, offset, count);
            }

            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            // Không đóng stream bên trong: file đầu ra còn phải ghi thêm HMAC tag phía sau.
            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
            }
        }

        #endregion
    }
}
