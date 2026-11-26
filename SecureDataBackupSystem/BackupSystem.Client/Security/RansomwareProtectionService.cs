using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BackupSystem.Shared.Network;

namespace BackupSystem.Client.Security
{
    public sealed class RansomwareDetectionResult
    {
        public bool IsRansomwareDetected { get; set; }
        public double AnomalyScore { get; set; }
        public int ScannedFilesCount { get; set; }
        public int SuspiciousFilesCount { get; set; }
        public int HighEntropyFilesCount { get; set; }
        public double AverageEntropy { get; set; }
        public string Message { get; set; } = string.Empty;

        // Tương thích snake_case từ JSON của Python
        public bool is_ransomware_detected { get => IsRansomwareDetected; set => IsRansomwareDetected = value; }
        public double anomaly_score { get => AnomalyScore; set => AnomalyScore = value; }
        public int scanned_files_count { get => ScannedFilesCount; set => ScannedFilesCount = value; }
        public int suspicious_files_count { get => SuspiciousFilesCount; set => SuspiciousFilesCount = value; }
        public int high_entropy_files_count { get => HighEntropyFilesCount; set => HighEntropyFilesCount = value; }
        public double average_entropy { get => AverageEntropy; set => AverageEntropy = value; }
        public string message { get => Message; set => Message = value; }
    }

    public sealed class RansomwareThreatDetectedException : Exception
    {
        public RansomwareDetectionResult DetectionResult { get; }

        public RansomwareThreatDetectedException(RansomwareDetectionResult result)
            : base($"[CẢNH BÁO RANSOMWARE] Phát hiện dấu hiệu tấn công bất thường (Anomaly Score: {result.AnomalyScore:F2})! Quá trình sao lưu đã bị chặn ngay lập tức để bảo vệ dữ liệu sạch trên Server.")
        {
            DetectionResult = result;
        }
    }

    /// <summary>
    /// Dịch vụ AI Anomaly Detection & Chống Ransomware.
    /// Tích hợp module Python AI (hoặc fallback thuật toán Shannon Entropy C#),
    /// tự động chặn quá trình sao lưu nếu máy tính có dấu hiệu bị mã hóa hoặc đổi đuôi hàng loạt.
    /// </summary>
    public static class RansomwareProtectionService
    {
        private static readonly HashSet<string> KnownRansomwareExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".locked", ".crypto", ".wnry", ".crypted", ".enc", ".ransom",
            ".locky", ".cerber", ".wannacry", ".crypt", ".payme", ".darkside",
            ".revil", ".conti", ".blackmatter", ".stop", ".djvu", ".phobos",
            ".dharma", ".ryuk", ".makop", ".mallox", ".zepto", ".thor"
        };

        /// <summary>
        /// Phân tích độ bất thường của thư mục hoặc danh sách file trước khi tiến hành sao lưu.
        /// </summary>
        public static RansomwareDetectionResult AnalyzeFiles(string targetPath, IEnumerable<string>? specificFiles = null)
        {
            // 1. Thử chạy qua script Python AI
            try
            {
                var pythonResult = RunPythonDetector(targetPath, specificFiles);
                if (pythonResult != null)
                {
                    return pythonResult;
                }
            }
            catch
            {
                // Nếu môi trường chưa cài python hoặc gặp lỗi, tự động chuyển sang Fallback C# Engine
            }

            // 2. Chạy Native C# Shannon Entropy Engine (100% độc lập, không phụ thuộc môi trường)
            return RunNativeCSharpDetector(targetPath, specificFiles);
        }

        private static RansomwareDetectionResult? RunPythonDetector(string targetPath, IEnumerable<string>? specificFiles)
        {
            // Tìm script ransomware_detector.py
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string scriptPath = Path.Combine(baseDir, "ransomware_detector.py");

            if (!File.Exists(scriptPath))
            {
                // Thử tìm trong thư mục source code
                string devPath = Path.Combine(baseDir, "..", "..", "..", "BackupSystem.AI", "ransomware_detector.py");
                if (File.Exists(devPath))
                    scriptPath = Path.GetFullPath(devPath);
                else
                    return null;
            }

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{scriptPath}\" \"{targetPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using (var process = Process.Start(psi))
            {
                if (process == null) return null;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000); // Chờ tối đa 5 giây

                if (!string.IsNullOrWhiteSpace(output))
                {
                    return PacketSerializationHelper.DeserializeJson<RansomwareDetectionResult>(Encoding.UTF8.GetBytes(output));
                }
            }

            return null;
        }

        private static RansomwareDetectionResult RunNativeCSharpDetector(string targetPath, IEnumerable<string>? specificFiles)
        {
            var filesList = new List<string>();

            if (specificFiles != null && specificFiles.Any())
            {
                filesList.AddRange(specificFiles.Where(File.Exists));
            }
            else if (File.Exists(targetPath))
            {
                filesList.Add(targetPath);
            }
            else if (Directory.Exists(targetPath))
            {
                filesList.AddRange(Directory.EnumerateFiles(targetPath, "*", SearchOption.AllDirectories).Take(500));
            }

            if (filesList.Count == 0)
            {
                return new RansomwareDetectionResult
                {
                    IsRansomwareDetected = false,
                    Message = "Không có file nào cần phân tích."
                };
            }

            int totalFiles = filesList.Count;
            int suspiciousExtCount = 0;
            int highEntropyCount = 0;
            double totalEntropy = 0;

            foreach (var file in filesList)
            {
                string ext = Path.GetExtension(file);
                if (!string.IsNullOrEmpty(ext) && KnownRansomwareExtensions.Contains(ext))
                {
                    suspiciousExtCount++;
                }

                // Kiểm tra đuôi kép (ví dụ: baocao.docx.locked)
                string fileName = Path.GetFileName(file);
                string[] parts = fileName.Split('.');
                if (parts.Length >= 3 && KnownRansomwareExtensions.Contains("." + parts[parts.Length - 1]))
                {
                    suspiciousExtCount++;
                }

                double entropy = CalculateShannonEntropy(file);
                totalEntropy += entropy;

                if (entropy >= 7.65)
                {
                    highEntropyCount++;
                }
            }

            double avgEntropy = totalEntropy / totalFiles;
            double suspiciousExtRatio = (double)suspiciousExtCount / totalFiles;
            double highEntropyRatio = (double)highEntropyCount / totalFiles;

            double effectiveEntropyRatio = highEntropyCount >= 3 ? highEntropyRatio : (highEntropyRatio * 0.5);
            double anomalyScore = (effectiveEntropyRatio * 0.35) + (suspiciousExtRatio * 0.55);
            if (avgEntropy > 7.3 && (highEntropyCount >= 3 || suspiciousExtCount > 0))
            {
                anomalyScore += 0.10;
            }
            anomalyScore = Math.Min(1.0, Math.Round(anomalyScore, 4));

            bool isDetected = false;
            var reasons = new List<string>();

            if (suspiciousExtCount >= 3 || (totalFiles > 0 && suspiciousExtRatio >= 0.20))
            {
                isDetected = true;
                reasons.Add($"Phát hiện {suspiciousExtCount} file có đuôi mã hóa độc hại của Ransomware.");
            }

            if (highEntropyCount >= 5 && highEntropyRatio >= 0.35)
            {
                isDetected = true;
                reasons.Add($"Phát hiện {highEntropyCount}/{totalFiles} file có Shannon Entropy cực cao (> 7.65) - dấu hiệu bị mã hóa hàng loạt!");
            }

            if (anomalyScore >= 0.50)
            {
                isDetected = true;
            }

            return new RansomwareDetectionResult
            {
                IsRansomwareDetected = isDetected,
                AnomalyScore = anomalyScore,
                ScannedFilesCount = totalFiles,
                SuspiciousFilesCount = suspiciousExtCount,
                HighEntropyFilesCount = highEntropyCount,
                AverageEntropy = Math.Round(avgEntropy, 2),
                Message = isDetected
                    ? "CẢNH BÁO NGUY HIỂM: " + string.Join(" ", reasons)
                    : $"An toàn. Đã phân tích {totalFiles} file (Entropy trung bình: {avgEntropy:F2}, Anomaly Score: {anomalyScore:F2})."
            };
        }

        public static double CalculateShannonEntropy(string filePath, int sampleBytes = 65536)
        {
            if (!File.Exists(filePath)) return 0.0;

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0) return 0.0;

                byte[] buffer = new byte[Math.Min(sampleBytes, (int)fileInfo.Length)];
                int bytesRead;

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bytesRead = fs.Read(buffer, 0, buffer.Length);
                }

                if (bytesRead == 0) return 0.0;

                int[] counts = new int[256];
                for (int i = 0; i < bytesRead; i++)
                {
                    counts[buffer[i]]++;
                }

                double entropy = 0.0;
                for (int i = 0; i < 256; i++)
                {
                    if (counts[i] > 0)
                    {
                        double p = (double)counts[i] / bytesRead;
                        entropy -= p * Math.Log(p, 2);
                    }
                }

                return entropy;
            }
            catch
            {
                return 0.0;
            }
        }
    }
}
