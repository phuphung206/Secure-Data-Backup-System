using System;
using System.IO;

namespace BackupSystem.Shared.Models
{
    /// <summary>
    /// Quản lý thống nhất đường dẫn lưu trữ dữ liệu sao lưu dùng chung giữa Client (WinForms), Server và Console App.
    /// Ưu tiên lưu ngay tại thư mục gốc Solution (SecureDataBackupSystem\SecureDataBackup) để dễ theo dõi trực tiếp,
    /// nếu chạy độc lập ngoài môi trường dev sẽ tự động lưu tại Documents\SecureDataBackup.
    /// </summary>
    public static class BackupPaths
    {
        public static string GetBackupRoot()
        {
            string solutionRoot = FindSolutionRoot(AppDomain.CurrentDomain.BaseDirectory)
                ?? FindSolutionRoot(Directory.GetCurrentDirectory());

            if (!string.IsNullOrWhiteSpace(solutionRoot))
            {
                return Path.GetFullPath(Path.Combine(solutionRoot, "SecureDataBackup"));
            }

            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(docs))
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(profile))
                {
                    docs = Path.Combine(profile, "Documents");
                }
                else
                {
                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    docs = !string.IsNullOrWhiteSpace(localAppData) ? localAppData : Path.GetTempPath();
                }
            }

            return Path.GetFullPath(Path.Combine(docs, "SecureDataBackup"));
        }

        public static string GetHistoryFilePath()
        {
            return Path.Combine(GetBackupRoot(), "backup_history.txt");
        }

        private static string FindSolutionRoot(string startDir)
        {
            if (string.IsNullOrWhiteSpace(startDir) || !Directory.Exists(startDir))
                return null;

            var dir = new DirectoryInfo(startDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SecureDataBackupSystem.slnx")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }

            return null;
        }
    }
}
