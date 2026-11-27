using System;
using System.IO;

namespace SecureDataBackupSystem
{
    public static class BackupService
    {
        private static readonly string backupRoot = BackupSystem.Shared.Models.BackupPaths.GetBackupRoot();

        /// <summary>
        /// Sao lưu theo lịch lên Server (dùng cấu hình kết nối đã lưu ở màn hình Sao lưu mạng).
        /// Mỗi lần chạy tạo một phiên bản mới trên Server với TriggerType = SCHEDULER.
        /// </summary>
        public static void RunScheduledNetworkBackup(string sourcePath, bool incremental)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                (!File.Exists(sourcePath) && !Directory.Exists(sourcePath)))
                throw new Exception("File hoặc thư mục cần sao lưu không tồn tại!");

            if (!ClientSettingsStore.Exists)
                throw new Exception("Chưa có cấu hình Server. Hãy chạy Sao lưu mạng một lần để lưu cấu hình.");

            var settings = ClientSettingsStore.Load();
            var args = settings.ToWorkerArgs(Path.GetFullPath(sourcePath), incremental, "SCHEDULER");
            var result = new BackupSystem.Client.NetworkClient.NetworkBackupEngine().Run(args);

            string mode = incremental ? "Lịch → Server (Incremental)" : "Lịch → Server (Full)";
            string status = result.Success
                ? $"Thành công - phiên bản #{result.VersionId} ({result.TransferredFilesCount} file gửi)"
                : (result.RansomwareThreat != null ? "Chặn bởi AI Ransomware" : "Thất bại: " + result.Message);
            AppendHistory(mode, sourcePath, status);

            if (!result.Success)
                throw new Exception(result.Message);
        }

        private static void AppendHistory(string mode, string sourcePath, string status)
        {
            string historyPath = Path.Combine(backupRoot, "backup_history.txt");
            Directory.CreateDirectory(backupRoot);
            string line = string.Join("\t",
                DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                mode,
                sourcePath,
                status.Replace("\t", " ").Replace("\r", " ").Replace("\n", " "));
            File.AppendAllText(historyPath, line + Environment.NewLine);
        }

        // Hàm thực hiện Full Backup theo lịch
        public static void RunScheduledBackup(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new Exception("Chưa chọn đường dẫn sao lưu!");

            sourcePath = Path.GetFullPath(sourcePath);
            string root = Path.GetFullPath(backupRoot)
                .TrimEnd(Path.DirectorySeparatorChar);

            if (sourcePath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                root.StartsWith(
                    sourcePath.TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(
                    "Không thể sao lưu thư mục chứa dữ liệu Backup!");
            }

            bool isDirectory = Directory.Exists(sourcePath);
            if (!isDirectory && !File.Exists(sourcePath))
                throw new Exception("File hoặc thư mục không tồn tại!");

            string destinationRoot = isDirectory
                ? Path.Combine(backupRoot, new DirectoryInfo(sourcePath).Name)
                : backupRoot;

            if (!isDirectory && sourcePath.Equals(
                Path.GetFullPath(Path.Combine(
                    destinationRoot, Path.GetFileName(sourcePath))),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Nguồn và đích trùng nhau!");
            }

            // Tự động dùng khóa DPAPI hiện có hoặc tạo mới nếu tài khoản Windows này chưa có khóa.
            KeyProtectionService.UseOrCreateKey(key => true);

            BackupPipeline.Run(
                sourcePath,
                destinationRoot,
                null,
                true,
                (source, destination) => true,
                (source, destination) =>
                {
                    string temporaryPath = destination + "." +
                        Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        BackupFileEncryptionService.EncryptFileWithExistingKey(
                            source, temporaryPath);
                        if (File.Exists(destination))
                            File.Replace(temporaryPath, destination, null);
                        else
                            File.Move(temporaryPath, destination);
                    }
                    finally
                    {
                        if (File.Exists(temporaryPath))
                            File.Delete(temporaryPath);
                    }
                },
                null);

            string historyPath = Path.Combine(backupRoot, "backup_history.txt");
            string line = string.Join("\t",
                DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                "Scheduled Full Backup",
                sourcePath,
                "Thành công");
            File.AppendAllText(historyPath, line + Environment.NewLine);
        }
    }
}
