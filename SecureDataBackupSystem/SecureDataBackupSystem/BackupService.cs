using System;
using System.IO;

namespace SecureDataBackupSystem
{
    public static class BackupService
    {
        private static readonly string backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "SecureDataBackup");

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

            // Tác vụ theo lịch phải dùng khóa đã tạo cho tài khoản Windows này.
            // Kiểm tra khóa trước khi tạo thư mục đích sao lưu.
            KeyProtectionService.UseExistingKey(key => true);

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
