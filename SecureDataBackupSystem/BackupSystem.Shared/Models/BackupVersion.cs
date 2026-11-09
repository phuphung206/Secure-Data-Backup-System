using System;

namespace BackupSystem.Shared.Models
{
    public class BackupVersion
    {
        public int VersionId { get; set; }
        public int UserId { get; set; }
        public int? ConfigId { get; set; }
        public int? ParentVersionId { get; set; } // Trỏ về Version trước nếu là INCREMENTAL
        public string BackupType { get; set; } = "INCREMENTAL"; // FULL | INCREMENTAL
        public string TriggerType { get; set; } = "MANUAL"; // MANUAL | SCHEDULER | WATCHER
        public string RootClientPath { get; set; } = string.Empty;
        public int TotalFiles { get; set; }
        public long TotalSizeBytes { get; set; }
        public string Status { get; set; } = "IN_PROGRESS";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
