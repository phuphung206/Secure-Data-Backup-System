
using System;
using System.IO;
using System.Windows.Forms;

namespace SecureDataBackupSystem
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Kiểm tra chương trình có chạy theo lịch không
            if (args.Length > 0 && args[0] == "--scheduled-backup")
            {
                try
                {
                    // Đọc cấu hình lịch (schedule_config.json, tương thích schedule_path.txt cũ)
                    ScheduleConfig config = ScheduleConfig.Load();
                    if (config == null)
                        throw new Exception("Chưa có cấu hình lịch sao lưu!");

                    if (config.UploadToServer)
                    {
                        // Sao lưu ngầm lên Server: nén, mã hóa AES-256, AI chống Ransomware và ghi nhận phiên bản (TriggerType = SCHEDULER)
                        BackupService.RunScheduledNetworkBackup(config.SourcePath, config.Incremental);
                    }
                    else
                    {
                        // Gọi chức năng sao lưu cục bộ không cần mở Form
                        BackupService.RunScheduledBackup(config.SourcePath);
                    }
                }
                catch (Exception ex)
                {
                    // Ghi lỗi vào file để kiểm tra khi chạy ngầm
                    string logFolder = Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.ApplicationData),
                        "SecureDataBackupSystem"
                    );

                    Directory.CreateDirectory(logFolder);

                    File.AppendAllText(
                        Path.Combine(logFolder, "schedule_error.log"),
                        DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
                        + " - " + ex.Message + Environment.NewLine
                    );

                    // Trả mã lỗi cho Windows Task Scheduler
                    Environment.ExitCode = 1;
                }

                return;
            }

            // Chạy giao diện bình thường
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new FormHome());
        }
    }
}
