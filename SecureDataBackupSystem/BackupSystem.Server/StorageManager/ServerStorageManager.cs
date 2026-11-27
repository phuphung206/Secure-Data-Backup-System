using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
                : BackupPaths.GetBackupRoot();

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

            string backupFolder = string.IsNullOrWhiteSpace(payload.BackupId) ? "default" : payload.BackupId;
            string finalDestination;
            try
            {
                // Chống Path Traversal: BackupId và RelativePath đến từ mạng nên phải được kiểm tra
                // trước khi ghép vào đường dẫn vật lý của kho lưu trữ.
                if (!SafePathHelper.IsValidBackupId(backupFolder))
                    throw new InvalidDataException($"BackupId '{payload.BackupId}' không hợp lệ.");
                if (string.IsNullOrWhiteSpace(payload.TransferId) || !SafePathHelper.IsValidBackupId(payload.TransferId))
                    throw new InvalidDataException("TransferId không hợp lệ.");
                if (payload.TotalBytes < 0 || payload.TotalChunks < 0)
                    throw new InvalidDataException("Kích thước file hoặc số chunk không hợp lệ.");

                finalDestination = SafePathHelper.CombineUnderRoot(_backupStoreRoot, backupFolder, payload.RelativePath);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException || ex is PathTooLongException || ex is NotSupportedException)
            {
                return new BackupInitResponsePayload
                {
                    TransferId = payload.TransferId,
                    IsAccepted = false,
                    Message = "Từ chối phiên truyền: " + ex.Message
                };
            }

            // Tạo FileAssembler quản lý việc nhận và ghép chunk
            var assembler = new FileAssembler(
                payload.TransferId,
                finalDestination,
                payload.TotalBytes,
                payload.TotalChunks,
                payload.ExpectedSha256);

            if (_activeAssemblers.TryRemove(payload.TransferId, out var previous))
            {
                previous.Abort();
                previous.Dispose();
            }

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
        /// Chuẩn bị thông tin file cho luồng Restore file về phía Client (theo BackupId + đường dẫn tương đối).
        /// </summary>
        public RestoreInitResponsePayload GetFileForRestore(string backupId, string relativePath, int chunkSize)
        {
            string? targetPath = GetPhysicalFilePath(backupId, relativePath);
            if (targetPath == null)
            {
                return new RestoreInitResponsePayload
                {
                    FileExists = false,
                    RelativePath = relativePath,
                    Message = $"Không tìm thấy file trên server: {relativePath}"
                };
            }

            return GetFileForRestoreByPhysicalPath(targetPath, relativePath, chunkSize);
        }

        /// <summary>
        /// Chuẩn bị Restore từ đường dẫn vật lý đã tra cứu trong CSDL. Đường dẫn vẫn phải nằm trong kho BackupStore.
        /// </summary>
        public RestoreInitResponsePayload GetFileForRestoreByPhysicalPath(string physicalPath, string relativePath, int chunkSize)
        {
            if (string.IsNullOrWhiteSpace(physicalPath) ||
                !SafePathHelper.IsUnderRoot(_backupStoreRoot, physicalPath) ||
                !File.Exists(physicalPath))
            {
                return new RestoreInitResponsePayload
                {
                    FileExists = false,
                    RelativePath = relativePath,
                    Message = $"Không tìm thấy file trên server: {relativePath}"
                };
            }

            var info = new FileInfo(physicalPath);
            int totalChunks = FileChunker.CalculateTotalChunks(info.Length, chunkSize);
            string sha256 = FileChunker.ComputeFileSha256(physicalPath);

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
        /// Trả về null nếu BackupId/RelativePath không hợp lệ (ví dụ chứa "..") hoặc file không tồn tại.
        /// </summary>
        public string? GetPhysicalFilePath(string backupId, string relativePath)
        {
            try
            {
                if (!SafePathHelper.IsValidBackupId(backupId))
                    return null;

                string targetPath = SafePathHelper.CombineUnderRoot(_backupStoreRoot, backupId, relativePath);
                return File.Exists(targetPath) ? targetPath : null;
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException || ex is PathTooLongException || ex is NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>
        /// Liệt kê toàn bộ các file đã sao lưu trong kho BackupStore trên Server.
        /// </summary>
        public List<RemoteBackupFileEntry> ListBackupFiles()
        {
            var result = new List<RemoteBackupFileEntry>();
            if (!Directory.Exists(_backupStoreRoot))
                return result;

            string storePrefix = Path.GetFullPath(_backupStoreRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            foreach (string file in Directory.GetFiles(_backupStoreRoot, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase))
                    continue;

                string fullFile = Path.GetFullPath(file);
                if (!fullFile.StartsWith(storePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string relInStore = fullFile.Substring(storePrefix.Length);
                int sepIndex = relInStore.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                if (sepIndex <= 0 || sepIndex >= relInStore.Length - 1)
                    continue;

                string backupId = relInStore.Substring(0, sepIndex);
                string relativePath = relInStore.Substring(sepIndex + 1).Replace('\\', '/');
                var info = new FileInfo(fullFile);

                result.Add(new RemoteBackupFileEntry
                {
                    BackupId = backupId,
                    RelativePath = relativePath,
                    FileSize = info.Length,
                    LastModifiedUtc = info.LastWriteTimeUtc
                });
            }

            result.Sort((a, b) => b.LastModifiedUtc.CompareTo(a.LastModifiedUtc));
            return result;
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

