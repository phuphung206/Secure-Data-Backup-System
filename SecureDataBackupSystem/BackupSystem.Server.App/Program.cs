using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BackupSystem.Server.NetworkServer;
using BackupSystem.Shared.Models;
using BackupSystem.Shared.Network;

namespace BackupSystem.Server.App
{
    internal class Program
    {
        private static int RunUserCommand(string[] args)
        {
            string storage = args.Length > 3 ? Path.GetFullPath(args[3]) : BackupPaths.GetBackupRoot();
            var metadata = new BackupSystem.Server.DataAccess.BackupMetadataService(
                BackupSystem.Server.DataAccess.DbConnectionFactory.GetDbFilePathForStorage(storage));
            try
            {
                metadata.Initialize();
                if (args[0].Equals("adduser", StringComparison.OrdinalIgnoreCase))
                {
                    int id = metadata.Users.CreateUser(args[1], args[2]);
                    Console.WriteLine($"Đã tạo tài khoản '{args[1]}' (UserId={id}) trong {metadata.DbFilePath}.");
                }
                else
                {
                    metadata.Users.ChangePassword(args[1], args[2]);
                    Console.WriteLine($"Đã đổi mật khẩu tài khoản '{args[1]}'.");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi: " + ex.Message);
                return 1;
            }
        }

        private static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "Secure Data Backup Server (TCP Socket)";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
========================================================================
       🛡️ SECURE DATA BACKUP SYSTEM - TCP BACKUP SERVER 🛡️
========================================================================");
            Console.ResetColor();

            // Quản lý tài khoản người dùng trên Server:
            //   BackupSystem.Server.App adduser <tên> <mật khẩu> [thư mục lưu trữ]
            //   BackupSystem.Server.App passwd  <tên> <mật khẩu mới> [thư mục lưu trữ]
            if (args.Length >= 3 &&
                (args[0].Equals("adduser", StringComparison.OrdinalIgnoreCase) ||
                 args[0].Equals("passwd", StringComparison.OrdinalIgnoreCase)))
            {
                return RunUserCommand(args);
            }

            int port = NetworkProtocolConstants.DefaultPort;
            if (args.Length > 0 && int.TryParse(args[0], out int customPort))
            {
                port = customPort;
            }

            string storageDir = BackupPaths.GetBackupRoot();
            if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
            {
                storageDir = Path.GetFullPath(args[1]);
            }

            Console.WriteLine($"[CẤU HÌNH] Cổng dịch vụ : {port}");
            Console.WriteLine($"[CẤU HÌNH] Kho lưu trữ : {storageDir}");
            Console.WriteLine($"[CẤU HÌNH] Kích thước chunk mặc định: {NetworkProtocolConstants.DefaultChunkSize / 1024} KB\n");

            using (var server = new TcpBackupServer(storageDir))
            {
                try
                {
                    var metadata = server.InitializeMetadata();
                    Console.WriteLine($"[CƠ SỞ DỮ LIỆU] SQLite Metadata đã sẵn sàng: {metadata.DbFilePath}");
                    Console.WriteLine("[TÀI KHOẢN] Mặc định: admin / 123456 (tạo thêm: BackupSystem.Server.App adduser <tên> <mật khẩu>)\n");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CẢNH BÁO DB] Chưa thể khởi tạo SQLite Database: {ex.Message}\n");
                }

                server.OnLog += (msg) =>
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine(msg);
                    Console.ResetColor();
                };

                server.OnClientConnected += (endpoint, clientId) =>
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\n>>> [+] CLIENT KẾT NỐI: {endpoint} (Mã máy: {clientId})");
                    Console.ResetColor();
                };

                server.OnClientDisconnected += (endpoint, clientId) =>
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n<<< [-] CLIENT NGẮT KẾT NỐI: {endpoint} (Mã máy: {clientId})");
                    Console.ResetColor();
                };

                server.OnTransferStarted += (info) =>
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"\n[NHẬN FILE] Bắt đầu nhận '{info.RelativePath}'");
                    Console.WriteLine($"            Dung lượng: {TransferProgressReport.FormatBytes(info.TotalBytes)} | Chunks: {info.TotalChunks} | SHA-256: {info.ExpectedSha256}");
                    Console.ResetColor();
                };

                server.OnChunkReceived += (transferId, chunkIndex, totalChunks) =>
                {
                    int percent = (int)(((chunkIndex + 1) * 100.0) / totalChunks);
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.Write($"\r[TIẾN ĐỘ] Nhận chunk {chunkIndex + 1}/{totalChunks} ({percent}%)  ");
                    Console.ResetColor();
                };

                server.OnTransferCompleted += (transferId, success, message) =>
                {
                    Console.WriteLine();
                    if (success)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[HOÀN TẤT] {message}");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[THẤT BẠI] {message}");
                    }
                    Console.ResetColor();
                };

                try
                {
                    server.Start(port);
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine("\n[HƯỚNG DẪN] Server đang hoạt động. Nhấn 'Q' hoặc Ctrl+C để dừng Server.\n");
                    Console.ResetColor();

                    while (true)
                    {
                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(true);
                            if (key.Key == ConsoleKey.Q)
                            {
                                Console.WriteLine("\nĐang dừng máy chủ...");
                                break;
                            }
                        }
                        await Task.Delay(200);
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[LỖI SERVER] {ex.Message}");
                    Console.ResetColor();
                }
                finally
                {
                    server.Stop();
                }
            }

            Console.WriteLine("Server đã đóng an toàn. Tạm biệt!");
            return 0;
        }
    }
}

