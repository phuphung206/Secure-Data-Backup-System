using System;

namespace BackupSystem.Shared.Models
{
    /// <summary>
    /// Gói tin Client gửi khi đã truyền xong tất cả các chunk của file.
    /// Yêu cầu Server ráp hoàn thiện, tính mã băm SHA-256 toàn file và đối chiếu.
    /// </summary>
    public sealed class BackupCompletePayload
    {
        public string TransferId { get; set; } = string.Empty;
        public string ExpectedFullSha256 { get; set; } = string.Empty;
        public long ExpectedTotalBytes { get; set; }
    }

    /// <summary>
    /// Server phản hồi kết quả kiểm tra toàn vẹn và chuyển file vào kho lưu trữ chính thức.
    /// </summary>
    public sealed class BackupCompleteResponsePayload
    {
        public string TransferId { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ServerSha256 { get; set; } = string.Empty;
        public string StoredFilePath { get; set; } = string.Empty;
        public long FinalSizeBytes { get; set; }
    }
}

