using System;
using System.Collections.Generic;
using System.IO;
using BackupSystem.Shared.Models;

namespace BackupSystem.Server.DataAccess
{
    /// <summary>
    /// Lớp điều phối Metadata trên Server: người dùng, phiên bản backup và ánh xạ đường dẫn vật lý.
    /// Được ClientSessionHandler gọi ở từng bước của giao thức (Auth → SessionStart → upload file → SessionEnd → Restore).
    /// </summary>
    public sealed class BackupMetadataService
    {
        private readonly VersionRepository _versions;
        private readonly UserRepository _users;
        private readonly AuditLogRepository _audit;

        public string DbFilePath { get; }

        public BackupMetadataService(string dbFilePath)
        {
            if (string.IsNullOrWhiteSpace(dbFilePath))
                throw new ArgumentException("Đường dẫn CSDL không hợp lệ.", nameof(dbFilePath));

            DbFilePath = Path.GetFullPath(dbFilePath);
            _versions = new VersionRepository(DbFilePath);
            _users = new UserRepository(DbFilePath);
            _audit = new AuditLogRepository(DbFilePath);
        }

        public UserRepository Users => _users;
        public VersionRepository Versions => _versions;

        public void Initialize()
        {
            DbConnectionFactory.InitializeDatabase(DbFilePath);
        }

        public ServerUser? Authenticate(string username, string password, string? machineName)
        {
            return _users.Authenticate(username, password, machineName);
        }

        /// <summary>
        /// Mở phiên bản mới. Nếu Client yêu cầu INCREMENTAL mà chưa có phiên bản COMPLETED nào của thư mục này
        /// thì phiên bản được ghi nhận là FULL (không có gì để kế thừa).
        /// </summary>
        public BackupSessionStartResponsePayload StartSession(int userId, BackupSessionStartPayload request)
        {
            if (!SafePathHelper.IsValidBackupId(request.BackupId))
                return new BackupSessionStartResponsePayload { IsAccepted = false, Message = "BackupId không hợp lệ." };
            if (string.IsNullOrWhiteSpace(request.RootClientPath))
                return new BackupSessionStartResponsePayload { IsAccepted = false, Message = "Thiếu thư mục gốc của Client." };

            string requestedType = string.Equals(request.BackupType, "INCREMENTAL", StringComparison.OrdinalIgnoreCase)
                ? "INCREMENTAL"
                : "FULL";
            string trigger = NormalizeTrigger(request.TriggerType);

            int? parentId = _versions.GetLatestCompletedVersionId(userId, request.RootClientPath);
            string effectiveType = parentId.HasValue ? requestedType : "FULL";

            int versionId = _versions.CreateBackupVersion(
                userId,
                effectiveType,
                request.RootClientPath,
                trigger,
                null,
                request.BackupId,
                request.ClientMachineName);

            var response = new BackupSessionStartResponsePayload
            {
                IsAccepted = true,
                VersionId = versionId,
                ParentVersionId = parentId,
                BackupType = effectiveType,
                Message = parentId.HasValue
                    ? $"Đã mở phiên bản #{versionId} ({effectiveType}), kế thừa từ phiên bản #{parentId}."
                    : $"Đã mở phiên bản #{versionId} (FULL - lần sao lưu đầu tiên của thư mục này)."
            };

            if (parentId.HasValue)
            {
                foreach (var file in _versions.GetFilesByVersion(parentId.Value))
                    response.ParentFiles.Add(ToEntry(file));
            }

            return response;
        }

        /// <summary>
        /// Ghi metadata cho một file vừa được lưu thành công trong phiên bản đang mở.
        /// </summary>
        public bool RecordUploadedFile(int userId, BackupInitPayload init, string storedPhysicalPath, long storedSizeBytes, out string error)
        {
            error = string.Empty;
            var version = _versions.GetVersion(init.VersionId);
            if (version == null || version.UserId != userId)
            {
                error = $"Phiên bản #{init.VersionId} không tồn tại hoặc không thuộc tài khoản hiện tại.";
                return false;
            }
            if (!string.Equals(version.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Phiên bản #{init.VersionId} đã đóng ({version.Status}).";
                return false;
            }
            if (!string.Equals(version.ClientBackupId, init.BackupId, StringComparison.Ordinal))
            {
                error = "BackupId của file không khớp với phiên bản đang mở.";
                return false;
            }

            string originalPath;
            try
            {
                originalPath = SafePathHelper.NormalizeRelativePath(
                    string.IsNullOrWhiteSpace(init.OriginalRelativePath) ? init.RelativePath : init.OriginalRelativePath);
            }
            catch (InvalidDataException ex)
            {
                error = ex.Message;
                return false;
            }

            _versions.RemoveFileFromVersion(version.VersionId, originalPath);
            _versions.AddFileMetadata(new FileMetadata
            {
                VersionId = version.VersionId,
                SourceVersionId = version.VersionId,
                RelativePath = originalPath,
                FileExtension = Path.GetExtension(originalPath),
                Sha256Hash = string.IsNullOrWhiteSpace(init.OriginalSha256) ? init.ExpectedSha256 : init.OriginalSha256,
                OriginalSizeBytes = init.OriginalSizeBytes > 0 ? init.OriginalSizeBytes : init.TotalBytes,
                EncryptedSizeBytes = storedSizeBytes,
                AesIv = init.IsEncrypted ? "AES-256-CBC+HMAC-SHA256 (PBKDF2, IV trong header file)" : string.Empty,
                TotalChunks = init.TotalChunks,
                ChangeType = NormalizeChangeType(init.ChangeType),
                LastModifiedTime = init.OriginalLastWriteTimeUtc == default ? DateTime.UtcNow : init.OriginalLastWriteTimeUtc,
                ServerPhysicalPath = storedPhysicalPath,
                IsEncrypted = init.IsEncrypted,
                IsCompressed = init.IsCompressed
            });
            return true;
        }

        /// <summary>
        /// Đóng phiên bản: kế thừa file không đổi từ phiên bản cha rồi chốt trạng thái COMPLETED/FAILED.
        /// </summary>
        public BackupSessionEndResponsePayload EndSession(int userId, BackupSessionEndPayload request)
        {
            var version = _versions.GetVersion(request.VersionId);
            if (version == null || version.UserId != userId)
                return new BackupSessionEndResponsePayload { IsSuccess = false, Message = "Phiên bản không tồn tại hoặc không thuộc tài khoản hiện tại." };
            if (!string.Equals(version.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
                return new BackupSessionEndResponsePayload { IsSuccess = false, VersionId = version.VersionId, Status = version.Status, Message = $"Phiên bản đã đóng ({version.Status})." };

            if (!request.IsSuccess)
            {
                _versions.MarkVersionFailed(version.VersionId);
                return new BackupSessionEndResponsePayload
                {
                    IsSuccess = true,
                    VersionId = version.VersionId,
                    Status = "FAILED",
                    Message = $"Phiên bản #{version.VersionId} bị hủy: {request.Message}"
                };
            }

            var currentPaths = new List<string>();
            foreach (string path in request.CurrentRelativePaths ?? new List<string>())
            {
                try
                {
                    currentPaths.Add(SafePathHelper.NormalizeRelativePath(path));
                }
                catch (InvalidDataException)
                {
                    // Bỏ qua đường dẫn không hợp lệ
                }
            }

            int uploaded = _versions.GetFilesByVersion(version.VersionId).Count;
            int carried = 0;
            if (version.ParentVersionId.HasValue)
            {
                carried = _versions.CarryOverFromParent(version.ParentVersionId.Value, version.VersionId, currentPaths);
            }

            _versions.CompleteBackupVersion(version.VersionId, "COMPLETED");
            _users.RecalculateUsedBytes(userId);

            return new BackupSessionEndResponsePayload
            {
                IsSuccess = true,
                VersionId = version.VersionId,
                Status = "COMPLETED",
                UploadedFiles = uploaded,
                CarriedOverFiles = carried,
                TotalFiles = uploaded + carried,
                Message = $"Phiên bản #{version.VersionId} hoàn tất: {uploaded} file mới/thay đổi, {carried} file kế thừa."
            };
        }

        public HashSet<string> GetOwnedBackupIds(int userId)
        {
            return _versions.GetClientBackupIds(userId);
        }

        public void FailVersion(int versionId)
        {
            _versions.MarkVersionFailed(versionId);
        }

        public List<BackupVersion> ListVersions(int userId, string? rootClientPath, bool includeFailed)
        {
            return _versions.GetVersionsByUser(userId, rootClientPath, includeFailed);
        }

        public VersionFilesResponsePayload GetVersionFiles(int userId, int versionId)
        {
            var version = _versions.GetVersion(versionId);
            if (version == null || version.UserId != userId)
                return new VersionFilesResponsePayload { IsSuccess = false, VersionId = versionId, Message = "Phiên bản không tồn tại hoặc không thuộc tài khoản hiện tại." };

            var response = new VersionFilesResponsePayload { IsSuccess = true, VersionId = versionId };
            foreach (var file in _versions.GetFilesByVersion(versionId))
                response.Files.Add(ToEntry(file));
            response.Message = $"Phiên bản #{versionId} có {response.Files.Count} file.";
            return response;
        }

        /// <summary>
        /// Tìm file vật lý để khôi phục theo (phiên bản, đường dẫn gốc). Trả về null nếu không có quyền hoặc không tồn tại.
        /// </summary>
        public FileMetadata? ResolveFileForRestore(int userId, int versionId, string relativePath)
        {
            var version = _versions.GetVersion(versionId);
            if (version == null || version.UserId != userId)
                return null;

            string normalized;
            try
            {
                normalized = SafePathHelper.NormalizeRelativePath(relativePath);
            }
            catch (InvalidDataException)
            {
                return null;
            }

            var files = _versions.GetFilesByVersion(versionId, normalized);
            return files.Count > 0 ? files[0] : null;
        }

        public void LogRestore(int userId, int versionId, string scope, string target, bool success)
        {
            try
            {
                _audit.LogRestoreOperation(userId, versionId, scope, target, success ? "COMPLETED" : "FAILED");
            }
            catch
            {
                // Ghi nhật ký không được làm hỏng luồng khôi phục
            }
        }

        private static VersionFileEntry ToEntry(FileMetadata file)
        {
            return new VersionFileEntry
            {
                RelativePath = file.RelativePath,
                Sha256 = file.Sha256Hash,
                OriginalSizeBytes = file.OriginalSizeBytes,
                StoredSizeBytes = file.EncryptedSizeBytes,
                ChangeType = file.ChangeType,
                IsEncrypted = file.IsEncrypted,
                IsCompressed = file.IsCompressed,
                LastModifiedTime = file.LastModifiedTime,
                SourceVersionId = file.SourceVersionId
            };
        }

        private static string NormalizeTrigger(string? trigger)
        {
            string value = (trigger ?? "MANUAL").Trim().ToUpperInvariant();
            return value == "SCHEDULER" || value == "WATCHER" ? value : "MANUAL";
        }

        private static string NormalizeChangeType(string? changeType)
        {
            string value = (changeType ?? "MODIFIED").Trim().ToUpperInvariant();
            return value == "ADDED" ? "ADDED" : "MODIFIED";
        }
    }
}
