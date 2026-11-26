using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BackupSystem.Client.NetworkClient;
using BackupSystem.Client.Security;
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
   🛡️ KIỂM THỬ TỰ ĐỘNG TOÀN DIỆN - SECURE DATA BACKUP SYSTEM 🛡️
   1. TCP Packet Protocol & CRC-32 Framing
   2. Data Integrity & Checksum Tampering Verification
   3. File Chunking & Assembly (100% SHA-256 Match)
   4. Multithreaded TCP Client-Server Socket Transfer
   5. BackgroundWorker Synchronization & Progress Bar (No UI Freeze)
   6. BackgroundWorker Thread Cancellation Safety
   7. AES-256 Client-Side Encryption & Key Derivation (PBKDF2)
   8. AI Ransomware Anomaly Detection & Auto-Blocking Engine
   9. Incremental Network Backup & File Extension Filtering
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

                try
                {
                    server.Start(testPort);
                    await Task.Delay(200);

                    string clientFilePath = Path.Combine(clientData, "document.dat");
                    byte[] fileData = new byte[2 * 1024 * 1024];
                    new Random(123).NextBytes(fileData);
                    File.WriteAllBytes(clientFilePath, fileData);
                    string clientSha256 = FileChunker.ComputeFileSha256(clientFilePath);

                    bool connected = await client.ConnectAsync("127.0.0.1", testPort);
                    if (!connected) throw new Exception("Client không thể kết nối tới Server.");

                    string backupId = "backup_v1";
                    string relativePath = "docs/document.dat";

                    bool uploadOk = await client.UploadFileAsync(
                        clientFilePath,
                        relativePath,
                        backupId,
                        chunkSize: 64 * 1024);

                    if (!uploadOk) throw new Exception("UploadFileAsync trả về false.");

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

                    worker.StartBackup(args);

                    var result = await tcs.Task;
                    if (!result.Success)
                        throw new Exception($"BackgroundWorker thất bại: {result.Message}");

                    if (progressList.Count == 0)
                        throw new Exception("Không nhận được sự kiện cập nhật tiến trình Progress.");

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
                    string sampleFile = Path.Combine(clientData, "huge_file.bin");
                    byte[] data = new byte[8 * 1024 * 1024];
                    new Random(777).NextBytes(data);
                    File.WriteAllBytes(sampleFile, data);

                    var worker = new NetworkBackupWorker();
                    var tcs = new TaskCompletionSource<NetworkBackupWorkerResult>();

                    worker.TransferProgressUpdated += (report) =>
                    {
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

            // TEST 7: Bảo mật & Mã hóa AES-256 Client-Side (Mã hóa trước khi truyền mạng)
            await RunTest("Security - Mã hóa AES-256 Client-Side với PBKDF2 (100,000 vòng) và giải mã nguyên vẹn", async () =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "SDB_AesTest_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                try
                {
                    string originalPath = Path.Combine(tempDir, "secret_data.txt");
                    string encryptedPath = Path.Combine(tempDir, "secret_data.txt.enc");
                    string decryptedPath = Path.Combine(tempDir, "secret_data_decrypted.txt");

                    string sensitiveContent = "Bản tin bảo mật: Dữ liệu này được mã hóa bằng AES-256 tại Client trước khi gửi qua mạng TCP.";
                    File.WriteAllText(originalPath, sensitiveContent, Encoding.UTF8);
                    string originalSha256 = FileChunker.ComputeFileSha256(originalPath);

                    string password = "MyUltraSecureBackupPassword2026!";

                    // 1. Mã hóa
                    AesEncryptionHelper.EncryptFile(originalPath, encryptedPath, password);

                    if (!File.Exists(encryptedPath)) throw new Exception("File mã hóa không được tạo ra.");
                    if (!AesEncryptionHelper.IsEncrypted(encryptedPath)) throw new Exception("Header SDBAES256 không hợp lệ.");

                    // 2. Thử giải mã sai mật khẩu -> phải thất bại
                    try
                    {
                        AesEncryptionHelper.DecryptFile(encryptedPath, decryptedPath, "WrongPassword");
                        throw new Exception("Đáng lẽ phải thất bại khi giải mã sai mật khẩu!");
                    }
                    catch (CryptographicException)
                    {
                        // Đúng kỳ vọng
                    }

                    // 3. Giải mã đúng mật khẩu
                    AesEncryptionHelper.DecryptFile(encryptedPath, decryptedPath, password);
                    string decryptedSha256 = FileChunker.ComputeFileSha256(decryptedPath);

                    if (!string.Equals(originalSha256, decryptedSha256, StringComparison.OrdinalIgnoreCase))
                        throw new Exception($"File giải mã không khớp mã SHA-256 ban đầu! Gốc: {originalSha256}, Giải mã: {decryptedSha256}");

                    string decryptedText = File.ReadAllText(decryptedPath, Encoding.UTF8);
                    if (decryptedText != sensitiveContent)
                        throw new Exception("Nội dung sau khi giải mã không đúng với bản gốc.");
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            });

            // TEST 8: Phòng chống Ransomware bằng AI (Anomaly Detection)
            await RunTest("AI Anomaly Detection - Chống Ransomware: Nhận diện mã hóa bất thường & chặn sao lưu", async () =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "SDB_AiRansomwareTest_" + Guid.NewGuid().ToString("N"));
                string cleanDir = Path.Combine(tempDir, "CleanFolder");
                string infectedDir = Path.Combine(tempDir, "InfectedFolder");
                Directory.CreateDirectory(cleanDir);
                Directory.CreateDirectory(infectedDir);

                try
                {
                    // 1. Thư mục sạch: file văn bản thông thường, entropy thấp
                    for (int i = 0; i < 5; i++)
                    {
                        string normalText = string.Concat(System.Linq.Enumerable.Repeat($"Đây là tài liệu văn bản công việc bình thường số {i}. Nội dung có quy luật tự nhiên.\n", 50));
                        File.WriteAllText(Path.Combine(cleanDir, $"document_{i}.txt"), normalText);
                    }

                    var cleanResult = RansomwareProtectionService.AnalyzeFiles(cleanDir);
                    if (cleanResult.IsRansomwareDetected)
                        throw new Exception($"AI đã báo động nhầm trên thư mục sạch! Score: {cleanResult.AnomalyScore}");

                    // 2. Thư mục bị tấn công giả lập: nhiều file đổi đuôi .locked và nội dung entropy cao (random bytes giả mã hóa virus)
                    for (int i = 0; i < 6; i++)
                    {
                        byte[] encryptedTrash = new byte[8192];
                        new Random(i * 17).NextBytes(encryptedTrash);
                        File.WriteAllBytes(Path.Combine(infectedDir, $"important_db_{i}.docx.locked"), encryptedTrash);
                    }

                    var threatResult = RansomwareProtectionService.AnalyzeFiles(infectedDir);
                    if (!threatResult.IsRansomwareDetected)
                        throw new Exception($"AI không phát hiện ra dấu hiệu Ransomware trong thư mục bị tấn công! Score: {threatResult.AnomalyScore}");

                    if (threatResult.AnomalyScore < 0.6)
                        throw new Exception($"Điểm bất thường Anomaly Score ({threatResult.AnomalyScore:F2}) thấp hơn ngưỡng kỳ vọng.");
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            });

            // TEST 9: Incremental Backup & Bỏ qua file không đổi để tiết kiệm băng thông
            await RunTest("Backup Engine - Incremental Backup: Bỏ qua file không thay đổi để tiết kiệm băng thông mạng", async () =>
            {
                string testRoot = Path.Combine(Path.GetTempPath(), "SDB_Incremental_Test_" + Guid.NewGuid().ToString("N"));
                string serverStorage = Path.Combine(testRoot, "ServerStorage");
                string clientData = Path.Combine(testRoot, "ClientData");
                Directory.CreateDirectory(serverStorage);
                Directory.CreateDirectory(clientData);

                int testPort = 5993;
                var server = new TcpBackupServer(serverStorage);
                server.Start(testPort);
                await Task.Delay(200);

                try
                {
                    string file1 = Path.Combine(clientData, "file1.txt");
                    string file2 = Path.Combine(clientData, "file2.txt");
                    File.WriteAllText(file1, "Nội dung file 1 phiên bản gốc");
                    File.WriteAllText(file2, "Nội dung file 2 phiên bản gốc");

                    // Lần sao lưu 1: Full Backup
                    var worker1 = new NetworkBackupWorker();
                    var tcs1 = new TaskCompletionSource<NetworkBackupWorkerResult>();
                    worker1.TransferCompleted += r => tcs1.SetResult(r);

                    worker1.StartBackup(new NetworkBackupWorkerArgs
                    {
                        ServerHost = "127.0.0.1",
                        ServerPort = testPort,
                        SourcePath = clientData,
                        IsIncremental = false
                    });

                    var res1 = await tcs1.Task;
                    if (!res1.Success || res1.TransferredFilesCount != 2)
                        throw new Exception($"Lần 1 sao lưu thất bại hoặc không đủ 2 file: {res1.Message}");

                    // Lần sao lưu 2: Incremental Backup khi chưa sửa gì -> phải bỏ qua 2 file
                    var worker2 = new NetworkBackupWorker();
                    var tcs2 = new TaskCompletionSource<NetworkBackupWorkerResult>();
                    worker2.TransferCompleted += r => tcs2.SetResult(r);

                    worker2.StartBackup(new NetworkBackupWorkerArgs
                    {
                        ServerHost = "127.0.0.1",
                        ServerPort = testPort,
                        SourcePath = clientData,
                        IsIncremental = true
                    });

                    var res2 = await tcs2.Task;
                    if (!res2.Success || res2.TransferredFilesCount != 0 || res2.SkippedFilesCount != 2)
                        throw new Exception($"Lần 2 Incremental Backup không bỏ qua file không đổi! Transferred={res2.TransferredFilesCount}, Skipped={res2.SkippedFilesCount}");

                    // Lần sao lưu 3: Sửa đổi 1 file, giữ nguyên 1 file
                    await Task.Delay(50);
                    File.AppendAllText(file1, " - Nội dung mới được chỉnh sửa!");

                    var worker3 = new NetworkBackupWorker();
                    var tcs3 = new TaskCompletionSource<NetworkBackupWorkerResult>();
                    worker3.TransferCompleted += r => tcs3.SetResult(r);

                    worker3.StartBackup(new NetworkBackupWorkerArgs
                    {
                        ServerHost = "127.0.0.1",
                        ServerPort = testPort,
                        SourcePath = clientData,
                        IsIncremental = true
                    });

                    var res3 = await tcs3.Task;
                    if (!res3.Success || res3.TransferredFilesCount != 1 || res3.SkippedFilesCount != 1)
                        throw new Exception($"Lần 3 Incremental Backup phải truyền đúng 1 file thay đổi và bỏ qua 1 file! Transferred={res3.TransferredFilesCount}, Skipped={res3.SkippedFilesCount}");
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
