using System;
using System.IO;
using System.Security.Cryptography;

namespace SecureDataBackupSystem
{
    internal static class BackupFileEncryptionService
    {
        private static readonly byte[] Magic = { (byte)'S', (byte)'D', (byte)'B', (byte)'F', 1 };
        private const int FrameLengthSize = 4;

        internal static bool IsEncryptedFile(string path)
        {
            if (!File.Exists(path))
                return false;

            byte[] header = new byte[Magic.Length];
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

            for (int i = 0; i < Magic.Length; i++)
            {
                if (header[i] != Magic[i])
                    return false;
            }
            return true;
        }

        internal static void EncryptFile(string source, string destination)
        {
            KeyProtectionService.UseOrCreateKey(key =>
            {
                EncryptFileWithKey(source, destination, key);
                return true;
            });
        }

        internal static void EncryptFileWithExistingKey(
            string source,
            string destination)
        {
            KeyProtectionService.UseExistingKey(key =>
            {
                EncryptFileWithKey(source, destination, key);
                return true;
            });
        }

        private static void EncryptFileWithKey(
            string source,
            string destination,
            byte[] key)
        {
            using (FileStream input = File.OpenRead(source))
            using (FileStream output = File.Create(destination))
            {
                output.Write(Magic, 0, Magic.Length);
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
                using (FileStream input = File.OpenRead(source))
                using (FileStream output = File.Create(destination))
                {
                    byte[] header = new byte[Magic.Length];
                    ReadExactly(input, header, header.Length);
                    for (int i = 0; i < Magic.Length; i++)
                    {
                        if (header[i] != Magic[i])
                            throw new InvalidDataException("Encrypted backup header is invalid.");
                    }

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
                                return true;
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
            });
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
