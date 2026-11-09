using System;

namespace BackupSystem.Shared.Models
{
    public class BackupVersion
    {
        public int VersionId { get; set; }
        public int UserId { get; set; }
        public string BackupType { get; set; } = "INCREMENTAL"; // FULL / INCREMENTAL
        public int TotalFiles { get; set; }
        public long TotalSizeBytes { get; set; }
        public string Status { get; set; } = "COMPLETED";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
