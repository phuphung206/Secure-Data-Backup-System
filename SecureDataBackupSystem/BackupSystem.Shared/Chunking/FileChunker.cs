using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BackupSystem.Shared.Models;
using BackupSystem.Shared.Network;

namespace BackupSystem.Shared.Chunking
{
    /// <summary>
    /// Module thực hiện cắt nhỏ các file dung lượng lớn thành từng chunk (File Chunking).
    /// Giúp tiết kiệm RAM, chống nghẽn đường truyền và hỗ trợ truyền file dung lượng hàng GB.
    /// </summary>
    public static class FileChunker
    {
        /// <summary>
        /// Tính tổng số chunk dựa vào dung lượng file và kích thước chunk quy định.
        /// </summary>
        public static int CalculateTotalChunks(long fileSizeBytes, int chunkSize)
        {
            if (chunkSize <= 0)
                chunkSize = NetworkProtocolConstants.DefaultChunkSize;

            if (fileSizeBytes <= 0)
                return 1; // File rỗng vẫn có 1 chunk 0 byte

            return (int)((fileSizeBytes + chunkSize - 1) / chunkSize);
        }

        /// <summary>
        /// Duyệt và cắt file thành các FileChunk tuần tự (Streaming Iterator).
        /// Sử dụng bộ nhớ đệm tái sử dụng (Reusable Buffer) để tránh kích hoạt Garbage Collector (LOH).
        /// </summary>
        public static IEnumerable<FileChunk> SliceFile(string filePath, string transferId, int chunkSize = NetworkProtocolConstants.DefaultChunkSize)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Không tìm thấy file để cắt chunk.", filePath);

            if (chunkSize <= 0)
                chunkSize = NetworkProtocolConstants.DefaultChunkSize;

            var fileInfo = new FileInfo(filePath);
            long totalBytes = fileInfo.Length;
            int totalChunks = CalculateTotalChunks(totalBytes, chunkSize);

            byte[] buffer = new byte[chunkSize];

            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize, FileOptions.SequentialScan))
            {
                if (totalBytes == 0)
                {
                    yield return new FileChunk(transferId, 0, 1, 0, Array.Empty<byte>());
                    yield break;
                }

                int chunkIndex = 0;
                long offset = 0;
                int bytesRead;

                while ((bytesRead = fileStream.Read(buffer, 0, chunkSize)) > 0)
                {
                    byte[] chunkData = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, chunkData, 0, bytesRead);

                    yield return new FileChunk(transferId, chunkIndex, totalChunks, offset, chunkData);

                    offset += bytesRead;
                    chunkIndex++;
                }
            }
        }

        /// <summary>
        /// Tính toán nhanh mã SHA-256 của file bằng phương thức Streaming.
        /// </summary>
        public static string ComputeFileSha256(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File không tồn tại để tính SHA-256.", filePath);

            using (var sha = SHA256.Create())
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan))
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
            }
        }
    }
}

