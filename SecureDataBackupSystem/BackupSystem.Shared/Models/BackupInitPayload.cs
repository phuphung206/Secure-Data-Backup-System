using System;

namespace BackupSystem.Shared.Models
{
    /// <summary>
    /// Gói tin khởi tạo phiên truyền file sao lưu từ Client lên Server.
    /// Mang đầy đủ Metadata để Server cấp phát không gian và chuẩn bị bộ ghép file (FileAssembler).
    /// </summary>
    public sealed class BackupInitPayload
    {
        public string TransferId { get; set; } = Guid.NewGuid().ToString("N");
        public string SourceKey { get; set; } = string.Empty;
        public string BackupId { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public int ChunkSize { get; set; }
        public int TotalChunks { get; set; }
        public string ExpectedSha256 { get; set; } = string.Empty;
        public bool IsEncrypted { get; set; }
        public bool IsBaseline { get; set; }
        public string ClientMachineName { get; set; } = Environment.MachineName;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}

