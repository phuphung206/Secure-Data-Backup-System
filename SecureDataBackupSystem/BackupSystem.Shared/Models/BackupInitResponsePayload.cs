using System;

namespace BackupSystem.Shared.Models
{
    /// <summary>
    /// Phản hồi từ Server khi nhận yêu cầu khởi tạo backup file.
    /// Hỗ trợ cơ chế tiếp tục truyền (Resume) từ chunk bị gián đoạn.
    /// </summary>
    public sealed class BackupInitResponsePayload
    {
        public string TransferId { get; set; } = string.Empty;
        public bool IsAccepted { get; set; }
        public string Message { get; set; } = string.Empty;
        public int ResumeFromChunkIndex { get; set; } = 0; // Hỗ trợ Resume nếu đã có chunk cũ
        public long BytesAlreadyReceived { get; set; } = 0;
    }
}

