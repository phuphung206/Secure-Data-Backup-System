using System;

namespace BackupSystem.Shared.Models
{
    public class FileMetadata
    {
        public int FileId { get; set; }
        public int VersionId { get; set; }
        public string RelativePath { get; set; } = string.Empty;
        public string Sha256Hash { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTime LastModifiedTime { get; set; }
        public string ServerPhysicalPath { get; set; } = string.Empty;
    }
}
