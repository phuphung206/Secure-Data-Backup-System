using System;
using System.Collections.Concurrent;
using System.IO;
using BackupSystem.Shared.Chunking;
using BackupSystem.Shared.Models;

namespace BackupSystem.Server.StorageManager
{
    /// <summary>
    /// Quản lý việc lưu trữ, ráp file từ các chunk và tổ chức kho dữ liệu trên Server (Server Storage Manager).
    /// </summary>
    public sealed class ServerStorageManager : IDisposable
    {
        private readonly string _storageRoot;
        private readonly string _assemblingRoot;
        private readonly string _backupStoreRoot;
        private readonly ConcurrentDictionary<string, FileAssembler> _activeAssemblers;
        private readonly ConcurrentDictionary<string, BackupInitPayload> _activeTransfers;
        private bool _isDisposed;

        public string StorageRoot => _storageRoot;
        public string BackupStoreRoot => _backupStoreRoot;

        public ServerStorageManager(string? storageRoot = null)
        {
            _storageRoot = !string.IsNullOrWhiteSpace(storageRoot)
                ? Path.GetFullPath(storageRoot)
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerStorage");

            _assemblingRoot = Path.Combine(_storageRoot, ".assembling");
            _backupStoreRoot = Path.Combine(_storageRoot, "BackupStore");

            Directory.CreateDirectory(_storageRoot);
            Directory.CreateDirectory(_assemblingRoot);
            Directory.CreateDirectory(_backupStoreRoot);

            _activeAssemblers = new ConcurrentDictionary<string, FileAssembler>(StringComparer.OrdinalIgnoreCase);
            _activeTransfers = new ConcurrentDictionary<string, BackupInitPayload>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Khởi tạo phiên nhận file mới hoặc tiếp tục phiên cũ (Resume).
        /// </summary>
        public BackupInitResponsePayload InitTransfer(BackupInitPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));

            string relativePath = payload.RelativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            string backupFolder = !string.IsNullOrWhiteSpace(payload.BackupId)
                ? payload.BackupId
                : "default";

            string finalDestination = Path.Combine(_backupStoreRoot, backupFolder, relativePath);

            // Tạo FileAssembler quản lý việc nhận và ghép chunk
            var assembler = new FileAssembler(
                payload.TransferId,
                finalDestination,
                payload.TotalBytes,
                payload.TotalChunks,
                payload.ExpectedSha256);

            _activeAssemblers[payload.TransferId] = assembler;
            _activeTransfers[payload.TransferId] = payload;

            return new BackupInitResponsePayload
            {
                TransferId = payload.TransferId,
                IsAccepted = true,
                Message = "Server sẵn sàng nhận dữ liệu các chunk.",
                ResumeFromChunkIndex = 0,
                BytesAlreadyReceived = 0
            };
        }

        /// <summary>
        /// Nhận và ghi một chunk vào file tạm đang lắp ráp.
        /// </summary>
        public FileChunkAckPayload WriteChunk(FileChunk chunk)
        {
            if (chunk == null) throw new ArgumentNullException(nameof(chunk));

            if (!_activeAssemblers.TryGetValue(chunk.TransferId, out var assembler))
            {
                return new FileChunkAckPayload
                {
                    TransferId = chunk.TransferId,
                    ChunkIndex = chunk.ChunkIndex,
                    IsSuccess = false,
                    Message = $"Không tìm thấy phiên truyền file có TransferId '{chunk.TransferId}'."
                };
            }

            try
            {
                assembler.WriteChunk(chunk);

                return new FileChunkAckPayload
                {
                    TransferId = chunk.TransferId,
                    ChunkIndex = chunk.ChunkIndex,
                    IsSuccess = true,
                    Message = "Chunk đã được ghi thành công."
                };
            }
            catch (Exception ex)
            {
                return new FileChunkAckPayload
                {
                    TransferId = chunk.TransferId,
                    ChunkIndex = chunk.ChunkIndex,
                    IsSuccess = false,
                    Message = $"Lỗi khi ghi chunk {chunk.ChunkIndex}: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Hoàn tất truyền file: xác thực tính toàn vẹn SHA-256 của file vừa ráp và lưu chính thức.
        /// </summary>
        public BackupCompleteResponsePayload CompleteTransfer(BackupCompletePayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));

            if (!_activeAssemblers.TryRemove(payload.TransferId, out var assembler))
            {
                return new BackupCompleteResponsePayload
                {
                    TransferId = payload.TransferId,
                    IsSuccess = false,
                    Message = $"Không tìm thấy phiên truyền file có TransferId '{payload.TransferId}'."
                };
            }

            _activeTransfers.TryRemove(payload.TransferId, out _);

            try
            {
                string actualSha256 = assembler.FinalizeAndVerify(payload.ExpectedFullSha256);
                long fileSize = new FileInfo(assembler.FinalDestinationPath).Length;

                return new BackupCompleteResponsePayload
                {
                    TransferId = payload.TransferId,
                    IsSuccess = true,
                    Message = "File đã được lắp ráp và xác thực toàn vẹn SHA-256 thành công!",
                    ServerSha256 = actualSha256,
                    StoredFilePath = assembler.FinalDestinationPath,
                    FinalSizeBytes = fileSize
                };
            }
            catch (Exception ex)
            {
                assembler.Abort();
                return new BackupCompleteResponsePayload
                {
                    TransferId = payload.TransferId,
                    IsSuccess = false,
                    Message = $"Lỗi kiểm tra toàn vẹn file: {ex.Message}"
                };
            }
            finally
            {
                assembler.Dispose();
            }
        }

        /// <summary>
        /// Chuẩn bị thông tin file cho luồng Restore file về phía Client.
        /// </summary>
        public RestoreInitResponsePayload GetFileForRestore(string backupId, string relativePath, int chunkSize)
        {
            string cleanRelative = relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            string targetPath = Path.Combine(_backupStoreRoot, backupId, cleanRelative);

            if (!File.Exists(targetPath))
            {
                return new RestoreInitResponsePayload
                {
                    FileExists = false,
                    RelativePath = relativePath,
                    Message = $"Không tìm thấy file trên server: {cleanRelative}"
                };
            }

            var info = new FileInfo(targetPath);
            int totalChunks = FileChunker.CalculateTotalChunks(info.Length, chunkSize);
            string sha256 = FileChunker.ComputeFileSha256(targetPath);

            return new RestoreInitResponsePayload
            {
                TransferId = Guid.NewGuid().ToString("N"),
                FileExists = true,
                RelativePath = relativePath,
                TotalBytes = info.Length,
                ChunkSize = chunkSize,
                TotalChunks = totalChunks,
                FileSha256 = sha256,
                Message = "File đã sẵn sàng để truyền về Client."
            };
        }

        /// <summary>
        /// Lấy đường dẫn file vật lý trên Server cho mục đích Restore.
        /// </summary>
        public string? GetPhysicalFilePath(string backupId, string relativePath)
        {
            string cleanRelative = relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            string targetPath = Path.Combine(_backupStoreRoot, backupId, cleanRelative);
            return File.Exists(targetPath) ? targetPath : null;
        }

        /// <summary>
        /// Hủy bỏ phiên truyền file dở dang.
        /// </summary>
        public void AbortTransfer(string transferId)
        {
            if (_activeAssemblers.TryRemove(transferId, out var assembler))
            {
                assembler.Abort();
                assembler.Dispose();
            }
            _activeTransfers.TryRemove(transferId, out _);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            foreach (var kvp in _activeAssemblers)
            {
                kvp.Value.Abort();
                kvp.Value.Dispose();
            }
            _activeAssemblers.Clear();
            _activeTransfers.Clear();
        }
    }
}

