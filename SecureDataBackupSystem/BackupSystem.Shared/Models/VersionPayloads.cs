using System;
using System.Collections.Generic;

namespace BackupSystem.Shared.Models
{
    /// <summary>
    /// Đăng nhập tài khoản trên Server (bảng Users). Mọi thao tác quản lý phiên bản đều cần đăng nhập.
    /// </summary>
    public sealed class AuthRequestPayload
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string MachineName { get; set; } = Environment.MachineName;
    }

    public sealed class AuthResponsePayload
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
    }

    /// <summary>
    /// Mở một phiên bản backup mới. Server tạo bản ghi BackupVersions và trả về VersionId.
    /// </summary>
    public sealed class BackupSessionStartPayload
    {
        public string BackupId { get; set; } = string.Empty;
        public string BackupType { get; set; } = "FULL";        // FULL | INCREMENTAL
        public string TriggerType { get; set; } = "MANUAL";     // MANUAL | SCHEDULER | WATCHER
        public string RootClientPath { get; set; } = string.Empty;
        public string ClientMachineName { get; set; } = Environment.MachineName;
    }

    public sealed class BackupSessionStartResponsePayload
    {
        public bool IsAccepted { get; set; }
        public string Message { get; set; } = string.Empty;
        public int VersionId { get; set; }
        public int? ParentVersionId { get; set; }
        public string BackupType { get; set; } = "FULL";

        /// <summary>
        /// Snapshot của phiên bản trước (nếu có) để Client so sánh Incremental dựa trên dữ liệu thật trên Server.
        /// </summary>
        public List<VersionFileEntry> ParentFiles { get; set; } = new List<VersionFileEntry>();
    }

    /// <summary>
    /// Đóng phiên bản. CurrentRelativePaths là danh sách TẤT CẢ file đang tồn tại ở Client lúc backup,
    /// Server dùng nó để kế thừa file không đổi từ phiên bản trước và loại bỏ file đã bị xóa.
    /// </summary>
    public sealed class BackupSessionEndPayload
    {
        public string BackupId { get; set; } = string.Empty;
        public int VersionId { get; set; }
        public bool IsSuccess { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public List<string> CurrentRelativePaths { get; set; } = new List<string>();
    }

    public sealed class BackupSessionEndResponsePayload
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public int VersionId { get; set; }
        public string Status { get; set; } = string.Empty;
        public int TotalFiles { get; set; }
        public int UploadedFiles { get; set; }
        public int CarriedOverFiles { get; set; }
    }

    public sealed class VersionListRequestPayload
    {
        /// <summary>Lọc theo thư mục gốc phía Client (để trống = tất cả).</summary>
        public string RootClientPath { get; set; } = string.Empty;
        public bool IncludeFailed { get; set; }
    }

    public sealed class VersionListResponsePayload
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<BackupVersion> Versions { get; set; } = new List<BackupVersion>();
    }

    public sealed class VersionFilesRequestPayload
    {
        public int VersionId { get; set; }
    }

    public sealed class VersionFilesResponsePayload
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public int VersionId { get; set; }
        public List<VersionFileEntry> Files { get; set; } = new List<VersionFileEntry>();
    }

    /// <summary>
    /// Một file trong snapshot của phiên bản (bao gồm cả file được kế thừa từ phiên bản trước).
    /// </summary>
    public sealed class VersionFileEntry
    {
        public string RelativePath { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;          // SHA-256 của file GỐC (trước nén/mã hóa)
        public long OriginalSizeBytes { get; set; }
        public long StoredSizeBytes { get; set; }
        public string ChangeType { get; set; } = string.Empty;      // ADDED | MODIFIED | UNCHANGED
        public bool IsEncrypted { get; set; }
        public bool IsCompressed { get; set; }
        public DateTime LastModifiedTime { get; set; }
        public int SourceVersionId { get; set; }                     // Phiên bản thực sự chứa dữ liệu vật lý
    }
}
