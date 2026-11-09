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
        private static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "Secure Data Backup Server (TCP Socket)";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
========================================================================
       🛡️ SECURE DATA BACKUP SYSTEM - TCP BACKUP SERVER 🛡️
    [TV3 — Network & Concurrency: TCP Sockets & Chunking Engine]
========================================================================");
            Console.ResetColor();

            int port = NetworkProtocolConstants.DefaultPort;
            if (args.Length > 0 && int.TryParse(args[0], out int customPort))
            {
                port = customPort;
            }

            string storageDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerStorage");
            if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
            {
                storageDir = Path.GetFullPath(args[1]);
            }

            Console.WriteLine($"[CẤU HÌNH] Cổng dịch vụ : {port}");
            Console.WriteLine($"[CẤU HÌNH] Kho lưu trữ : {storageDir}");
            Console.WriteLine($"[CẤU HÌNH] Kích thước chunk mặc định: {NetworkProtocolConstants.DefaultChunkSize / 1024} KB\n");

            using (var server = new TcpBackupServer(storageDir))
            {
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
        }
    }
}

