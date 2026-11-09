using System;

namespace BackupSystem.Shared.Models
{
    /// <summary>
    /// Dữ liệu báo cáo tiến độ truyền file qua mạng theo thời gian thực (Real-time Transfer Progress).
    /// Dùng để đồng bộ an toàn từ BackgroundWorker / Task sang thanh Progress Bar và giao diện người dùng.
    /// </summary>
    public sealed class TransferProgressReport
    {
        public string TransferId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public int CurrentChunk { get; set; }
        public int TotalChunks { get; set; }
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public int ProgressPercentage { get; set; }
        public double TransferSpeedBytesPerSec { get; set; }
        public double EstimatedRemainingSeconds { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public bool HasError { get; set; }

        public string FormattedSpeed
        {
            get
            {
                if (TransferSpeedBytesPerSec >= 1024 * 1024)
                    return $"{TransferSpeedBytesPerSec / (1024.0 * 1024.0):F2} MB/s";
                if (TransferSpeedBytesPerSec >= 1024)
                    return $"{TransferSpeedBytesPerSec / 1024.0:F1} KB/s";
                return $"{TransferSpeedBytesPerSec:F0} B/s";
            }
        }

        public string FormattedProgress
        {
            get
            {
                return $"{FormatBytes(BytesTransferred)} / {FormatBytes(TotalBytes)} ({ProgressPercentage}%)";
            }
        }

        public string FormattedTimeRemaining
        {
            get
            {
                if (EstimatedRemainingSeconds <= 0 || double.IsInfinity(EstimatedRemainingSeconds) || double.IsNaN(EstimatedRemainingSeconds))
                    return "00:00";

                var ts = TimeSpan.FromSeconds(EstimatedRemainingSeconds);
                if (ts.TotalHours >= 1)
                    return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
                return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
            }
        }

        public string FormattedChunkStatus => TotalChunks > 0 ? $"Gói tin {CurrentChunk}/{TotalChunks}" : "0/0";

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }
    }
}

