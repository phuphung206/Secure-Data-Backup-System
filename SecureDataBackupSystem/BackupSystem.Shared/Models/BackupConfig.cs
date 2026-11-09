using System;

namespace BackupSystem.Shared.Models
{
    public class BackupConfig
    {
        public int ConfigId { get; set; }
        public int UserId { get; set; }
        public string SourceFolderPath { get; set; } = string.Empty;
        public string AllowedExtensions { get; set; } = "*.*";
        public string ExcludedPatterns { get; set; } = ".tmp;.log;bin;obj";
        public string ScheduleCron { get; set; } = "DAILY_00:00"; // DAILY | WEEKLY | CRON
        public bool EnableRealtimeWatch { get; set; } = true;
        public bool IsActive { get; set; } = true;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
