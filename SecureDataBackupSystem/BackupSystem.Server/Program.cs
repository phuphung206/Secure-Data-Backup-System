using System;
using System.Collections.Generic;
using System.Text;
using BackupSystem.Server.DataAccess;
using BackupSystem.Server.NetworkServer;
using BackupSystem.Shared.Models;
using BackupSystem.Shared.Network;

namespace BackupSystem.Server
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("==================================================================");
            Console.WriteLine("   KIỂM THỬ TÍCH HỢP LƯU TRỮ VẬT LÝ & TRUY VẤN SERVER (WEEK 3)   ");
            Console.WriteLine("==================================================================\n");

            // 1. Khởi tạo CSDL 6 bảng
            DbConnectionFactory.InitializeDatabase();
            var coordinator = new ServerStorageCoordinator();
            var userRepo = new UserRepository();

            var admin = userRepo.Authenticate("admin", "123456")!;
            string sessionId = Guid.NewGuid().ToString("N")[..8];

            // 2. Bắt đầu phiên FULL BACKUP mới
            var startRes = coordinator.StartBackupSession(new StartBackupRequestDto
            {
                UserId = admin.UserId,
                Username = admin.Username,
                BackupType = "FULL",
                TriggerType = "MANUAL",
                RootClientPath = @"D:\ProjectSource",
                EstimatedTotalBytes = 1024 * 10
            });

            Console.WriteLine($"[1] Mở phiên Backup mới: VersionId = {startRes.VersionId} ({startRes.Message})");

            // 3. Giả lập Vinh gửi 2 mảnh Chunks của file "Report_CNPM.docx" lên Server
            byte[] chunk1 = Encoding.UTF8.GetBytes("ENCRYPTED_AES256_HEADER_BLOCK_PART_1___");
            byte[] chunk2 = Encoding.UTF8.GetBytes("ENCRYPTED_AES256_PAYLOAD_BLOCK_PART_2_END");

            coordinator.ProcessIncomingFileChunk(new FileChunkDto
            {
                SessionId = sessionId,
                VersionId = startRes.VersionId,
                RelativePath = @"\Docs\Report_CNPM.docx",
                ChunkIndex = 0,
                TotalChunks = 2,
                Data = chunk1
            });

            coordinator.ProcessIncomingFileChunk(new FileChunkDto
            {
                SessionId = sessionId,
                VersionId = startRes.VersionId,
                RelativePath = @"\Docs\Report_CNPM.docx",
                ChunkIndex = 1,
                TotalChunks = 2,
                Data = chunk2
            });

            // 4. Chốt file từ TempChunks sang kho chính thức Vault (.enc) và lưu SQLite
            var savedFile = coordinator.FinalizeSingleFileTransfer(
                sessionId,
                admin.UserId,
                admin.Username,
                "FULL",
                new FileMetadata
                {
                    VersionId = startRes.VersionId,
                    RelativePath = @"\Docs\Report_CNPM.docx",
                    FileExtension = ".docx",
                    Sha256Hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                    OriginalSizeBytes = 1024,
                    AesIv = "MTIzNDU2Nzg5MDEyMzQ1Ng==",
                    TotalChunks = 2,
                    ChangeType = "ADDED",
                    LastModifiedTime = DateTime.Now
                });

            coordinator.CompleteBackupSession(sessionId, startRes.VersionId, "FULL", null, new HashSet<string>());
            Console.WriteLine($"[2] Đã ghép 2 Chunks và lưu vào Vault tại:\n    -> {savedFile.ServerPhysicalPath}");

            // 5. Truy vấn danh sách bản sao lưu trả về dạng JSON cho UI của Hảo
            string versionListJson = coordinator.HandleGetVersionListJson(admin.UserId);
            Console.WriteLine($"\n[3] Kết quả truy vấn JSON danh sách phiên bản (Gửi về Client UI):\n    {versionListJson}");

            // 6. Kiểm thử luồng Phục hồi dữ liệu (Restore Streaming từ ổ cứng Server)
            Console.WriteLine("\n[4] Kiểm thử đọc file từ kho Vault để gửi Chunks khôi phục (Restore):");
            coordinator.ExecuteRestoreStream(new RestoreRequestDto
            {
                UserId = admin.UserId,
                VersionId = startRes.VersionId,
                RestoreScope = "SINGLE_FILE",
                TargetRelativePath = @"\Docs\Report_CNPM.docx"
            }, (meta, buffer, bytesRead, idx, total) =>
            {
                string restoredContent = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.WriteLine($"    -> Đang gửi Restore Chunk [{idx + 1}/{total}] của file '{meta.RelativePath}' ({bytesRead} bytes): {restoredContent}");
            });

            Console.WriteLine("\n[+] Hoàn tất kiểm thử tích hợp Tuần 3! Nhấn phím bất kỳ để thoát...");
            Console.ReadKey();
        }
    }
}
