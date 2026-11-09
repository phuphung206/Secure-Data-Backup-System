using System;

namespace BackupSystem.Shared.Models
{
    /// <summary>
    /// Gói tin ACK Server phản hồi sau khi nhận và ghi thành công một chunk xuống đĩa.
    /// Giúp Client xác thực từng bước (Step-by-step Verification) chống mất gói.
    /// </summary>
    public sealed class FileChunkAckPayload
    {
        public string TransferId { get; set; } = string.Empty;
        public int ChunkIndex { get; set; }
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}

