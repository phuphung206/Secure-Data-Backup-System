using System;

namespace BackupSystem.Shared.Models
{
    public class SecurityLog
    {
        public int LogId { get; set; }
        public int UserId { get; set; }
        public string EventType { get; set; } = "RANSOMWARE_BLOCKED";
        public double AnomalyScore { get; set; }
        public int AffectedFilesCount { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
