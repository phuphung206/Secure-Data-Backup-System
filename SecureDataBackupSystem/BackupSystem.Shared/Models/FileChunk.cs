using System;

namespace BackupSystem.Shared.Models
{
    /// <summary>
    /// Đại diện cho một lát cắt (chunk) dữ liệu của file trong cơ chế Chunking.
    /// Cho phép truyền các file dung lượng hàng trăm MB đến hàng GB mà không làm tràn bộ nhớ RAM.
    /// </summary>
    public sealed class FileChunk
    {
        public string TransferId { get; set; } = string.Empty;
        public int ChunkIndex { get; set; }
        public int TotalChunks { get; set; }
        public long Offset { get; set; }
        public int Length { get; set; }
        public string ChunkSha256 { get; set; } = string.Empty;
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public bool IsLastChunk => ChunkIndex >= TotalChunks - 1;

        public FileChunk()
        {
        }

        public FileChunk(string transferId, int chunkIndex, int totalChunks, long offset, byte[] data, string chunkSha256 = "")
        {
            TransferId = transferId;
            ChunkIndex = chunkIndex;
            TotalChunks = totalChunks;
            Offset = offset;
            Data = data ?? Array.Empty<byte>();
            Length = Data.Length;
            ChunkSha256 = chunkSha256;
        }
    }
}

