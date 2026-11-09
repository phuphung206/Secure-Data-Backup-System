using System;

namespace BackupSystem.Shared.Models
{
    public class FileMetadata
    {
        public int FileId { get; set; }
        public int VersionId { get; set; }
        public string RelativePath { get; set; } = string.Empty;
        public string FileExtension { get; set; } = string.Empty;
        public string Sha256Hash { get; set; } = string.Empty;
        public long OriginalSizeBytes { get; set; }
        public long EncryptedSizeBytes { get; set; }
        public string AesIv { get; set; } = string.Empty;
        public int TotalChunks { get; set; } = 1; // Tổng số mảnh gói tin TCP đã chia
        public string ChangeType { get; set; } = "MODIFIED"; // ADDED | MODIFIED | UNCHANGED | DELETED
        public DateTime LastModifiedTime { get; set; }
        public string ServerPhysicalPath { get; set; } = string.Empty;
    }
}
