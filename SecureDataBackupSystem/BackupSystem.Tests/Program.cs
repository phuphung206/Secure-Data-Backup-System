using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BackupSystem.Client.NetworkClient;
using BackupSystem.Server.NetworkServer;
using BackupSystem.Shared.Chunking;
using BackupSystem.Shared.Models;
using BackupSystem.Shared.Network;

namespace BackupSystem.Tests
{
    internal class Program
    {
        private static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
========================================================================
   🛡️ KIỂM THỬ TỰ ĐỘNG - TV3 NETWORK & CONCURRENCY SYSTEM 🛡️
   Week 1: TCP Packet Protocol & Chunking
   Week 2: Multithreaded TCP Client-Server & Chunked File Assembly
   Week 3: BackgroundWorker Synchronization & Progress Bar (No UI Freeze)
========================================================================");
            Console.ResetColor();

            int totalTests = 0;
            int passedTests = 0;

            async Task RunTest(string testName, Func<Task> testFunc)
            {
                totalTests++;
                Console.Write($"\n[CHẠY TEST {totalTests}] {testName}... ");
                var sw = Stopwatch.StartNew();
                try
                {
                    await testFunc();
                    sw.Stop();
                    passedTests++;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[PASSED] ({sw.ElapsedMilliseconds} ms)");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[FAILED] ({sw.ElapsedMilliseconds} ms)");
                    Console.WriteLine($"Lỗi: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                    Console.ResetColor();
                }
            }

            // TEST 1: Cấu trúc gói tin, framing & CRC-32 (Week 1)
            await RunTest("Week 1 - TCP Packet Protocol & CRC-32 Framing Round-trip", async () =>
            {
                byte[] samplePayload = Encoding.UTF8.GetBytes("Test Payload Data for Secure Data Backup System - SDBS Protocol");
                var packet = new NetworkPacket(PacketType.BackupInitRequest, samplePayload, flags: 0x03);

                using (var ms = new MemoryStream())
                {
                    await packet.WriteToStreamAsync(ms);
                    ms.Position = 0;

                    var deserialized = await NetworkPacket.ReadFromStreamAsync(ms);
                    if (deserialized == null) throw new Exception("Không đọc được gói tin.");
                    if (deserialized.Magic != NetworkProtocolConstants.MagicNumber) throw new Exception("Sai Magic Number.");
                    if (deserialized.Type != PacketType.BackupInitRequest) throw new Exception("Sai PacketType.");
                    if (deserialized.Flags != 0x03) throw new Exception("Sai Flags.");
                    if (deserialized.PayloadLength != samplePayload.Length) throw new Exception("Sai PayloadLength.");

                    string text = Encoding.UTF8.GetString(deserialized.Payload);
                    if (text != "Test Payload Data for Secure Data Backup System - SDBS Protocol")
                        throw new Exception("Nội dung Payload không khớp.");
                }
            });

            // TEST 2: Phát hiện toàn vẹn sai checksum CRC-32 (Week 1)
            await RunTest("Week 1 - Kiểm tra tính toàn vẹn: Phát hiện gói tin bị can thiệp Checksum", async () =>
            {
                byte[] rawPayload = Encoding.UTF8.GetBytes("Sensitive Data Content");
                var packet = new NetworkPacket(PacketType.FileChunkData, rawPayload);
                byte[] bytes = packet.ToBytes();

                // Cố tình sửa đổi 1 byte trong payload trên đường truyền
                bytes[bytes.Length - 1] ^= 0xFF;

                using (var ms = new MemoryStream(bytes))
                {
                    try
                    {
                        await NetworkPacket.ReadFromStreamAsync(ms);
                        throw new Exception("Đáng lẽ phải ném InvalidDataException do sai Checksum.");
                    }
                    catch (InvalidDataException)
                    {
                        // Mong đợi bắt lỗi này
                    }
                }
            });

            // TEST 3: Cắt nhỏ file lớn và ghép lại (FileChunker & FileAssembler) (Week 1 & 2)
            await RunTest("Week 1 - Cơ chế Chunking: Cắt file 3 MB thành các chunk 64 KB và ráp lại 100% SHA-256", async () =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "SDB_Chunk_Test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                try
                {
                    string sourceFile = Path.Combine(tempDir, "large_sample.bin");
                    string assembledFile = Path.Combine(tempDir, "assembled_output.bin");

                    // Tạo file 3 MB dữ liệu ngẫu nhiên
                    byte[] randomData = new byte[3 * 1024 * 1024];
                    new Random(42).NextBytes(randomData);
                    File.WriteAllBytes(sourceFile, randomData);

                    string originalSha256 = FileChunker.ComputeFileSha256(sourceFile);
                    int chunkSize = 64 * 1024; // 64 KB
                    int expectedChunks = FileChunker.CalculateTotalChunks(randomData.Length, chunkSize);

                    var assembler = new FileAssembler("TID_001", assembledFile, randomData.Length, expectedChunks, originalSha256);
                    int count = 0;
                    foreach (var chunk in FileChunker.SliceFile(sourceFile, "TID_001", chunkSize))
                    {
                        count++;
                        assembler.WriteChunk(chunk);
                    }

                    if (count != expectedChunks)
                        throw new Exception($"Số chunk tạo ra ({count}) không khớp kỳ vọng ({expectedChunks}).");

                    string assembledSha256 = assembler.FinalizeAndVerify();
                    assembler.Dispose();

                    if (!string.Equals(originalSha256, assembledSha256, StringComparison.OrdinalIgnoreCase))
                        throw new Exception($"SHA-256 không khớp! Gốc: {originalSha256}, Ráp: {assembledSha256}");

                    long assembledLen = new FileInfo(assembledFile).Length;
                    if (assembledLen != randomData.Length)
                        throw new Exception($"Dung lượng không khớp! Gốc: {randomData.Length}, Ráp: {assembledLen}");
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            });

            // TEST 4: Kết nối TCP Client-Server và truyền file chunked thực tế qua localhost (Week 2)
            await RunTest("Week 2 - TCP Client-Server: Truyền file đa luồng qua Socket và lưu trữ trên Server", async () =>
            {
                string testRoot = Path.Combine(Path.GetTempPath(), "SDB_Server_Test_" + Guid.NewGuid().ToString("N"));
                string serverStorage = Path.Combine(testRoot, "ServerStorage");
                string clientData = Path.Combine(testRoot, "ClientData");
                Directory.CreateDirectory(serverStorage);
                Directory.CreateDirectory(clientData);

                int testPort = 5990;
                var server = new TcpBackupServer(serverStorage);
                var client = new TcpBackupClient();
                server.OnLog += msg => Console.WriteLine("[SERVER] " + msg);
                client.OnLog += msg => Console.WriteLine("[CLIENT] " + msg);

                try
                {
                    server.Start(testPort);
                    await Task.Delay(200);

                    // Tạo file dữ liệu 2 MB phía client
                    string clientFilePath = Path.Combine(clientData, "document.dat");
                    byte[] fileData = new byte[2 * 1024 * 1024];
                    new Random(123).NextBytes(fileData);
                    File.WriteAllBytes(clientFilePath, fileData);
                    string clientSha256 = FileChunker.ComputeFileSha256(clientFilePath);

                    // Kết nối Client -> Server
                    bool connected = await client.ConnectAsync("127.0.0.1", testPort);
                    if (!connected) throw new Exception("Client không thể kết nối tới Server.");

                    // Upload file qua TCP Socket sử dụng Chunking
                    string backupId = "backup_v1";
                    string relativePath = "docs/document.dat";

                    bool uploadOk = await client.UploadFileAsync(
                        clientFilePath,
                        relativePath,
                        backupId,
                        chunkSize: 64 * 1024);

                    if (!uploadOk) throw new Exception("UploadFileAsync trả về false.");

                    // Kiểm tra file trên Server
                    string expectedServerPath = Path.Combine(serverStorage, "BackupStore", backupId, "docs", "document.dat");
                    if (!File.Exists(expectedServerPath))
                        throw new Exception($"Không tìm thấy file trên server tại: {expectedServerPath}");

                    string serverSha256 = FileChunker.ComputeFileSha256(expectedServerPath);
                    if (!string.Equals(clientSha256, serverSha256, StringComparison.OrdinalIgnoreCase))
                        throw new Exception($"Mã SHA-256 trên Server ({serverSha256}) không khớp mã của Client ({clientSha256})!");
                }
                finally
                {
                    client.Disconnect();
                    client.Dispose();
                    server.Stop();
                    server.Dispose();
                    try { Directory.Delete(testRoot, true); } catch { }
                }
            });

            // TEST 5: Đồng bộ BackgroundWorker và Progress Bar (Week 3)
            await RunTest("Week 3 - BackgroundWorker: Đồng bộ tiến trình, tính toán MB/s và không chặn luồng chính", async () =>
            {
                string testRoot = Path.Combine(Path.GetTempPath(), "SDB_Worker_Test_" + Guid.NewGuid().ToString("N"));
                string serverStorage = Path.Combine(testRoot, "ServerStorage");
                string clientData = Path.Combine(testRoot, "ClientData");
                Directory.CreateDirectory(serverStorage);
                Directory.CreateDirectory(clientData);

                int testPort = 5991;
                var server = new TcpBackupServer(serverStorage);
                server.Start(testPort);
                await Task.Delay(200);

                try
                {
                    string sampleFile = Path.Combine(clientData, "heavy_archive.bin");
                    byte[] data = new byte[3 * 1024 * 1024]; // 3 MB
                    new Random(999).NextBytes(data);
                    File.WriteAllBytes(sampleFile, data);

                    var worker = new NetworkBackupWorker();
                    var progressList = new List<TransferProgressReport>();
                    var tcs = new TaskCompletionSource<NetworkBackupWorkerResult>();

                    worker.TransferProgressUpdated += (report) =>
                    {
                        progressList.Add(report);
                    };

                    worker.TransferCompleted += (result) =>
                    {
                        tcs.SetResult(result);
                    };

                    var args = new NetworkBackupWorkerArgs
                    {
                        ServerHost = "127.0.0.1",
                        ServerPort = testPort,
                        SourcePath = sampleFile,
                        BackupId = "worker_backup",
                        IsSingleFile = true,
                        ChunkSize = 64 * 1024
                    };

                    // Khởi chạy ngầm dưới nền
                    worker.StartBackup(args);

                    var result = await tcs.Task;
                    if (!result.Success)
                        throw new Exception($"BackgroundWorker thất bại: {result.Message}");

                    if (progressList.Count == 0)
                        throw new Exception("Không nhận được sự kiện cập nhật tiến trình Progress.");

                    // Kiểm tra tiến trình đạt 100%
                    var lastReport = progressList[progressList.Count - 1];
                    if (lastReport.ProgressPercentage != 100)
                        throw new Exception($"Tiến độ cuối cùng ({lastReport.ProgressPercentage}%) chưa đạt 100%.");

                    if (result.TransferredFilesCount != 1)
                        throw new Exception("Số lượng file truyền không đúng.");
                }
                finally
                {
                    server.Stop();
                    server.Dispose();
                    try { Directory.Delete(testRoot, true); } catch { }
                }
            });

            // TEST 6: Hủy tác vụ an toàn qua BackgroundWorker (Week 3 Cancellation)
            await RunTest("Week 3 - BackgroundWorker Cancellation: Hủy tác vụ truyền file an toàn và giải phóng tài nguyên", async () =>
            {
                string testRoot = Path.Combine(Path.GetTempPath(), "SDB_Cancel_Test_" + Guid.NewGuid().ToString("N"));
                string serverStorage = Path.Combine(testRoot, "ServerStorage");
                string clientData = Path.Combine(testRoot, "ClientData");
                Directory.CreateDirectory(serverStorage);
                Directory.CreateDirectory(clientData);

                int testPort = 5992;
                var server = new TcpBackupServer(serverStorage);
                server.Start(testPort);
                await Task.Delay(200);

                try
                {
                    // Tạo file lớn 8 MB
                    string sampleFile = Path.Combine(clientData, "huge_file.bin");
                    byte[] data = new byte[8 * 1024 * 1024];
                    new Random(777).NextBytes(data);
                    File.WriteAllBytes(sampleFile, data);

                    var worker = new NetworkBackupWorker();
                    var tcs = new TaskCompletionSource<NetworkBackupWorkerResult>();

                    worker.TransferProgressUpdated += (report) =>
                    {
                        // Khi truyền được ít nhất 10%, tiến hành hủy tác vụ
                        if (report.ProgressPercentage >= 10 && !worker.CancellationPending)
                        {
                            worker.CancelBackup();
                        }
                    };

                    worker.TransferCompleted += (result) =>
                    {
                        tcs.SetResult(result);
                    };

                    var args = new NetworkBackupWorkerArgs
                    {
                        ServerHost = "127.0.0.1",
                        ServerPort = testPort,
                        SourcePath = sampleFile,
                        BackupId = "cancel_test",
                        IsSingleFile = true,
                        ChunkSize = 32 * 1024
                    };

                    worker.StartBackup(args);

                    var result = await tcs.Task;
                    if (!result.IsCancelled)
                        throw new Exception("Kỳ vọng result.IsCancelled == true nhưng lại không.");
                }
                finally
                {
                    server.Stop();
                    server.Dispose();
                    try { Directory.Delete(testRoot, true); } catch { }
                }
            });

            Console.WriteLine("\n========================================================================");
            if (passedTests == totalTests)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"🎉 TẤT CẢ {passedTests}/{totalTests} KIỂM THỬ ĐÃ ĐẠT THÀNH CÔNG RỰC RỠ! 100% HOÀN HẢO!");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"⚠️ KẾT QUẢ: {passedTests}/{totalTests} bài kiểm thử thành công.");
            }
            Console.ResetColor();
            Console.WriteLine("========================================================================\n");

            return passedTests == totalTests ? 0 : 1;
        }
    }
}
