using System;

namespace BackupSystem.Shared.Models
{
    /// <summary>
    /// Yêu cầu tải / khôi phục một file cụ thể từ Server về Client.
    /// </summary>
    public sealed class RestoreInitRequestPayload
    {
        public string BackupId { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Phản hồi của Server cho yêu cầu khôi phục file.
    /// </summary>
    public sealed class RestoreInitResponsePayload
    {
        public string TransferId { get; set; } = Guid.NewGuid().ToString("N");
        public bool FileExists { get; set; }
        public string RelativePath { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public int ChunkSize { get; set; }
        public int TotalChunks { get; set; }
        public string FileSha256 { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Client yêu cầu Server gửi chunk cụ thể trong phiên Restore.
    /// </summary>
    public sealed class RestoreChunkRequestPayload
    {
        public string TransferId { get; set; } = string.Empty;
        public int ChunkIndex { get; set; }
    }

    /// <summary>
    /// Client xác nhận đã nhận trọn vẹn file khôi phục và mã SHA-256 trùng khớp hoàn hảo.
    /// </summary>
    public sealed class RestoreCompleteAckPayload
    {
        public string TransferId { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string ClientComputedSha256 { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}

