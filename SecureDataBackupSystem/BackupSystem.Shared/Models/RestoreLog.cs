using System;

namespace BackupSystem.Shared.Models
{
    public class RestoreLog
    {
        public int RestoreId { get; set; }
        public int UserId { get; set; }
        public int VersionId { get; set; }
        public string RestoreScope { get; set; } = "FULL_FOLDER"; // FULL_FOLDER | SINGLE_FILE
        public string TargetFilePath { get; set; } = string.Empty;
        public string Status { get; set; } = "COMPLETED"; // IN_PROGRESS | COMPLETED | FAILED
        public DateTime RestoredAt { get; set; } = DateTime.Now;
    }
}
