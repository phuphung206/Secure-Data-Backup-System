using System;
using System.Collections.Generic;

namespace BackupSystem.Shared.Models
{
    /// <summary>
    /// Yêu cầu tải / khôi phục một file cụ thể từ Server về Client.
    /// </summary>
    public sealed class RestoreInitRequestPayload
    {
        public string BackupId { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;

        /// <summary>
        /// Khôi phục theo phiên bản: nếu VersionId > 0 thì RelativePath là đường dẫn GỐC trong snapshot,
        /// Server tra cứu metadata để tìm đúng file vật lý (kể cả file kế thừa từ phiên bản cũ hơn).
        /// </summary>
        public int VersionId { get; set; }
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
        public bool IsEncrypted { get; set; }
        public bool IsCompressed { get; set; }
        public string OriginalSha256 { get; set; } = string.Empty;
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

    /// <summary>
    /// Thông tin một file đã sao lưu trên Server dùng để hiển thị danh sách khôi phục từ xa.
    /// </summary>
    public sealed class RemoteBackupFileEntry
    {
        public string BackupId { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime LastModifiedUtc { get; set; }
    }

    /// <summary>
    /// Danh sách các file sao lưu hiện có trên Server.
    /// </summary>
    public sealed class RestoreListResponsePayload
    {
        public List<RemoteBackupFileEntry> Files { get; set; } = new List<RemoteBackupFileEntry>();
    }
}

