using System;
using System.IO;
using System.Security.Cryptography;

namespace SecureDataBackupSystem
{
    internal static class BackupFileEncryptionService
    {
        private static readonly byte[] Magic = { (byte)'S', (byte)'D', (byte)'B', (byte)'F', 1 };
        private static readonly byte[] PasswordMagic = { (byte)'S', (byte)'D', (byte)'B', (byte)'P', 1 };
        private const int SaltSizeBytes = 16;
        private const int Pbkdf2Iterations = 210000;
        private const int FrameLengthSize = 4;

        internal static bool IsEncryptedFile(string path)
        {
            return HasHeader(path, Magic);
        }

        internal static bool IsPasswordEncryptedFile(string path)
        {
            return HasHeader(path, PasswordMagic);
        }

        internal static void EncryptFile(string source, string destination)
        {
            KeyProtectionService.UseOrCreateKey(key =>
            {
                EncryptFileWithKey(source, destination, key, Magic);
                return true;
            });
        }

        internal static void EncryptFileWithExistingKey(
            string source,
            string destination)
        {
            KeyProtectionService.UseExistingKey(key =>
            {
                EncryptFileWithKey(source, destination, key, Magic);
                return true;
            });
        }

        internal static void EncryptFileWithPassword(
            string source,
            string destination,
            string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Cần nhập mật khẩu mã hóa.", "password");

            byte[] salt = new byte[SaltSizeBytes];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                random.GetBytes(salt);

            byte[] key = null;
            byte[] header = new byte[PasswordMagic.Length + salt.Length];
            try
            {
                using (Rfc2898DeriveBytes derive = new Rfc2898DeriveBytes(
                    password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256))
                {
                    key = derive.GetBytes(32);
                }

                Buffer.BlockCopy(PasswordMagic, 0, header, 0, PasswordMagic.Length);
                Buffer.BlockCopy(salt, 0, header, PasswordMagic.Length, salt.Length);
                EncryptFileWithKey(source, destination, key, header);
            }
            finally
            {
                Array.Clear(salt, 0, salt.Length);
                Array.Clear(header, 0, header.Length);
                if (key != null)
                    Array.Clear(key, 0, key.Length);
            }
        }

        private static void EncryptFileWithKey(
            string source,
            string destination,
            byte[] key,
            byte[] header)
        {
            using (FileStream input = File.OpenRead(source))
            using (FileStream output = File.Create(destination))
            {
                output.Write(header, 0, header.Length);
                byte[] buffer = new byte[EncryptionService.MaxChunkSizeBytes];
                try
                {
                    long chunkIndex = 0;
                    int read;
                    while ((read = ReadChunk(input, buffer)) > 0)
                    {
                        byte[] plaintext = new byte[read];
                        try
                        {
                            Buffer.BlockCopy(buffer, 0, plaintext, 0, read);
                            WriteFrame(output, EncryptionService.EncryptChunk(
                                plaintext, key, CreateAssociatedData(chunkIndex, false)));
                        }
                        finally
                        {
                            Array.Clear(plaintext, 0, plaintext.Length);
                            Array.Clear(buffer, 0, read);
                        }
                        chunkIndex++;
                    }

                    WriteFrame(output, EncryptionService.EncryptChunk(
                        new byte[0], key, CreateAssociatedData(chunkIndex, true)));
                }
                finally
                {
                    Array.Clear(buffer, 0, buffer.Length);
                }
            }
        }

        internal static void DecryptFile(string source, string destination)
        {
            KeyProtectionService.UseExistingKey(key =>
            {
                DecryptProtectedFile(source, destination, key);
                return true;
            });
        }

        internal static void DecryptFileWithPassword(
            string source,
            string destination,
            string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Cần nhập mật khẩu giải mã.", "password");

            using (FileStream input = File.OpenRead(source))
            {
                byte[] header = new byte[PasswordMagic.Length];
                ReadExactly(input, header, header.Length);
                ValidateHeader(header, PasswordMagic);

                byte[] salt = new byte[SaltSizeBytes];
                ReadExactly(input, salt, salt.Length);
                byte[] key = null;
                try
                {
                    using (Rfc2898DeriveBytes derive = new Rfc2898DeriveBytes(
                        password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256))
                    {
                        key = derive.GetBytes(32);
                    }

                    using (FileStream output = File.Create(destination))
                        DecryptChunks(input, output, key);
                }
                finally
                {
                    Array.Clear(header, 0, header.Length);
                    Array.Clear(salt, 0, salt.Length);
                    if (key != null)
                        Array.Clear(key, 0, key.Length);
                }
            }
        }

        private static void DecryptProtectedFile(
            string source,
            string destination,
            byte[] key)
        {
            using (FileStream input = File.OpenRead(source))
            using (FileStream output = File.Create(destination))
            {
                byte[] header = new byte[Magic.Length];
                ReadExactly(input, header, header.Length);
                ValidateHeader(header, Magic);
                DecryptChunks(input, output, key);
            }
        }

        private static void DecryptChunks(Stream input, Stream output, byte[] key)
        {
            long chunkIndex = 0;
            while (true)
            {
                byte[] lengthBytes = new byte[FrameLengthSize];
                ReadExactly(input, lengthBytes, lengthBytes.Length);
                int frameLength = BitConverter.ToInt32(lengthBytes, 0);
                int maximumLength = 5 + 12 + EncryptionService.MaxChunkSizeBytes + 16;
                if (frameLength < 5 + 12 + 16 || frameLength > maximumLength)
                    throw new InvalidDataException("Encrypted backup chunk has an invalid length.");

                byte[] frame = new byte[frameLength];
                ReadExactly(input, frame, frame.Length);
                byte[] plaintext = null;
                bool isFinal = false;
                try
                {
                    try
                    {
                        plaintext = EncryptionService.DecryptChunk(
                            frame, key, CreateAssociatedData(chunkIndex, false));
                    }
                    catch (CryptographicException)
                    {
                        plaintext = EncryptionService.DecryptChunk(
                            frame, key, CreateAssociatedData(chunkIndex, true));
                        isFinal = true;
                    }

                    if (isFinal)
                    {
                        if (plaintext.Length != 0 || input.Position != input.Length)
                            throw new InvalidDataException("Encrypted backup end marker is invalid.");
                        return;
                    }
                    if (plaintext.Length == 0 || plaintext.Length > EncryptionService.MaxChunkSizeBytes)
                        throw new InvalidDataException("Encrypted backup chunk has an invalid size.");
                    output.Write(plaintext, 0, plaintext.Length);
                    chunkIndex++;
                }
                finally
                {
                    if (plaintext != null)
                        Array.Clear(plaintext, 0, plaintext.Length);
                    Array.Clear(frame, 0, frame.Length);
                }
            }
        }

        private static bool HasHeader(string path, byte[] expectedHeader)
        {
            if (!File.Exists(path))
                return false;

            byte[] header = new byte[expectedHeader.Length];
            using (FileStream input = File.OpenRead(path))
            {
                int total = 0;
                while (total < header.Length)
                {
                    int read = input.Read(header, total, header.Length - total);
                    if (read == 0)
                        return false;
                    total += read;
                }
            }

            for (int i = 0; i < expectedHeader.Length; i++)
            {
                if (header[i] != expectedHeader[i])
                    return false;
            }
            return true;
        }

        private static void ValidateHeader(byte[] header, byte[] expectedHeader)
        {
            for (int i = 0; i < expectedHeader.Length; i++)
            {
                if (header[i] != expectedHeader[i])
                    throw new InvalidDataException("Encrypted backup header is invalid.");
            }
        }
        private static byte[] CreateAssociatedData(long chunkIndex, bool isFinal)
        {
            byte[] data = new byte[9];
            Buffer.BlockCopy(BitConverter.GetBytes(chunkIndex), 0, data, 0, 8);
            data[8] = isFinal ? (byte)1 : (byte)0;
            return data;
        }

        private static int ReadChunk(Stream input, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = input.Read(buffer, total, buffer.Length - total);
                if (read == 0)
                    break;
                total += read;
            }
            return total;
        }

        private static void WriteFrame(Stream output, byte[] frame)
        {
            try
            {
                byte[] length = BitConverter.GetBytes(frame.Length);
                output.Write(length, 0, length.Length);
                output.Write(frame, 0, frame.Length);
            }
            finally
            {
                Array.Clear(frame, 0, frame.Length);
            }
        }

        private static void ReadExactly(Stream input, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = input.Read(buffer, total, count - total);
                if (read == 0)
                    throw new EndOfStreamException("Encrypted backup is incomplete.");
                total += read;
            }
        }
    }
}
