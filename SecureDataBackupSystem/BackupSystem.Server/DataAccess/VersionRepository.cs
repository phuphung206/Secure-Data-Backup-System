using System;
using System.Collections.Generic;
using BackupSystem.Shared.Models;
using Microsoft.Data.Sqlite;

namespace BackupSystem.Server.DataAccess
{
    public class VersionRepository
    {
        // 1. Tạo Version mới (Tự động tìm ParentVersionId gần nhất nếu là INCREMENTAL)
        public int CreateBackupVersion(int userId, string backupType, string rootClientPath, string triggerType = "MANUAL", int? configId = null)
        {
            int? parentVersionId = null;
            if (backupType.Equals("INCREMENTAL", StringComparison.OrdinalIgnoreCase))
            {
                parentVersionId = GetLatestCompletedVersionId(userId, rootClientPath);
            }

            using var conn = DbConnectionFactory.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO BackupVersions (UserId, ConfigId, ParentVersionId, BackupType, TriggerType, RootClientPath, Status)
                VALUES ($userId, $cfgId, $parentId, $type, $trigger, $rootPath, 'IN_PROGRESS');
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$cfgId", (object?)configId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$parentId", (object?)parentVersionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$type", backupType.ToUpperInvariant());
            cmd.Parameters.AddWithValue("$trigger", triggerType.ToUpperInvariant());
            cmd.Parameters.AddWithValue("$rootPath", rootClientPath);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Lấy VersionId của bản Backup thành công gần nhất để làm cha (ParentVersionId)
        public int? GetLatestCompletedVersionId(int userId, string rootClientPath)
        {
            using var conn = DbConnectionFactory.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT VersionId FROM BackupVersions
                WHERE UserId = $userId AND RootClientPath = $rootPath AND Status = 'COMPLETED'
                ORDER BY VersionId DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$rootPath", rootClientPath);

            object? result = cmd.ExecuteScalar();
            return (result != null && result != DBNull.Value) ? Convert.ToInt32(result) : null;
        }

        // 2. Lưu 1 bản ghi FileMetadata
        public int AddFileMetadata(FileMetadata file)
        {
            using var conn = DbConnectionFactory.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO BackedUpFiles 
                (VersionId, RelativePath, FileExtension, Sha256Hash, OriginalSizeBytes, EncryptedSizeBytes, AesIv, TotalChunks, ChangeType, LastModifiedTime, ServerPhysicalPath)
                VALUES ($verId, $relPath, $ext, $hash, $origSize, $encSize, $iv, $chunks, $changeType, $lastMod, $physPath);
                SELECT last_insert_rowid();";

            BindFileParameters(cmd, file);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // 3. Lưu hàng loạt FileMetadata bằng Transaction
        public void SaveFileMetadataBatch(IEnumerable<FileMetadata> files)
        {
            using var conn = DbConnectionFactory.GetConnection();
            using var transaction = conn.BeginTransaction();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO BackedUpFiles 
                    (VersionId, RelativePath, FileExtension, Sha256Hash, OriginalSizeBytes, EncryptedSizeBytes, AesIv, TotalChunks, ChangeType, LastModifiedTime, ServerPhysicalPath)
                    VALUES ($verId, $relPath, $ext, $hash, $origSize, $encSize, $iv, $chunks, $changeType, $lastMod, $physPath);";

                foreach (var file in files)
                {
                    cmd.Parameters.Clear();
                    BindFileParameters(cmd, file);
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // 4. Kế thừa đường dẫn vật lý của các file KHÔNG ĐỔI từ ParentVersion sang Version mới
        public int CarryOverUnchangedFiles(int previousVersionId, int newVersionId, HashSet<string> modifiedOrDeletedRelativePaths)
        {
            var previousFiles = GetFilesByVersion(previousVersionId);
            var unchangedFiles = new List<FileMetadata>();

            foreach (var oldFile in previousFiles)
            {
                if (!modifiedOrDeletedRelativePaths.Contains(oldFile.RelativePath) && oldFile.ChangeType != "DELETED")
                {
                    oldFile.VersionId = newVersionId;
                    oldFile.ChangeType = "UNCHANGED";
                    unchangedFiles.Add(oldFile);
                }
            }

            if (unchangedFiles.Count > 0)
            {
                SaveFileMetadataBatch(unchangedFiles);
            }
            return unchangedFiles.Count;
        }

        // 5. Hoàn tất phiên bản Backup
        public void CompleteBackupVersion(int versionId, string finalStatus = "COMPLETED")
        {
            using var conn = DbConnectionFactory.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE BackupVersions
                SET Status = $status,
                    TotalFiles = (SELECT COUNT(1) FROM BackedUpFiles WHERE VersionId = $verId AND ChangeType != 'DELETED'),
                    TotalSizeBytes = IFNULL((SELECT SUM(EncryptedSizeBytes) FROM BackedUpFiles WHERE VersionId = $verId AND ChangeType != 'DELETED'), 0)
                WHERE VersionId = $verId;";

            cmd.Parameters.AddWithValue("$status", finalStatus);
            cmd.Parameters.AddWithValue("$verId", versionId);
            cmd.ExecuteNonQuery();
        }

        // 6. Lấy danh sách lịch sử các phiên bản của User
        public List<BackupVersion> GetVersionsByUser(int userId)
        {
            var list = new List<BackupVersion>();
            using var conn = DbConnectionFactory.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT VersionId, UserId, ConfigId, ParentVersionId, BackupType, TriggerType, 
                       RootClientPath, TotalFiles, TotalSizeBytes, Status, CreatedAt
                FROM BackupVersions
                WHERE UserId = $userId
                ORDER BY VersionId DESC;";
            cmd.Parameters.AddWithValue("$userId", userId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new BackupVersion
                {
                    VersionId = reader.GetInt32(0),
                    UserId = reader.GetInt32(1),
                    ConfigId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    ParentVersionId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    BackupType = reader.GetString(4),
                    TriggerType = reader.GetString(5),
                    RootClientPath = reader.GetString(6),
                    TotalFiles = reader.GetInt32(7),
                    TotalSizeBytes = reader.GetInt64(8),
                    Status = reader.GetString(9),
                    CreatedAt = DateTime.Parse(reader.GetString(10))
                });
            }
            return list;
        }

        // 7. Lấy toàn bộ danh sách file thuộc 1 Version (hoặc lấy 1 file lẻ để Restore)
        public List<FileMetadata> GetFilesByVersion(int versionId, string? singleRelativePath = null)
        {
            var files = new List<FileMetadata>();
            using var conn = DbConnectionFactory.GetConnection();
            using var cmd = conn.CreateCommand();

            string sql = @"
                SELECT FileId, VersionId, RelativePath, FileExtension, Sha256Hash, OriginalSizeBytes, 
                       EncryptedSizeBytes, AesIv, TotalChunks, ChangeType, LastModifiedTime, ServerPhysicalPath
                FROM BackedUpFiles
                WHERE VersionId = $verId AND ChangeType != 'DELETED'";

            if (!string.IsNullOrEmpty(singleRelativePath))
            {
                sql += " AND RelativePath = $relPath;";
                cmd.Parameters.AddWithValue("$relPath", singleRelativePath);
            }

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$verId", versionId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                files.Add(new FileMetadata
                {
                    FileId = reader.GetInt32(0),
                    VersionId = reader.GetInt32(1),
                    RelativePath = reader.GetString(2),
                    FileExtension = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Sha256Hash = reader.GetString(4),
                    OriginalSizeBytes = reader.GetInt64(5),
                    EncryptedSizeBytes = reader.GetInt64(6),
                    AesIv = reader.GetString(7),
                    TotalChunks = reader.GetInt32(8),
                    ChangeType = reader.GetString(9),
                    LastModifiedTime = DateTime.Parse(reader.GetString(10)),
                    ServerPhysicalPath = reader.GetString(11)
                });
            }
            return files;
        }

        private static void BindFileParameters(SqliteCommand cmd, FileMetadata file)
        {
            cmd.Parameters.AddWithValue("$verId", file.VersionId);
            cmd.Parameters.AddWithValue("$relPath", file.RelativePath);
            cmd.Parameters.AddWithValue("$ext", file.FileExtension ?? string.Empty);
            cmd.Parameters.AddWithValue("$hash", file.Sha256Hash);
            cmd.Parameters.AddWithValue("$origSize", file.OriginalSizeBytes);
            cmd.Parameters.AddWithValue("$encSize", file.EncryptedSizeBytes);
            cmd.Parameters.AddWithValue("$iv", file.AesIv);
            cmd.Parameters.AddWithValue("$chunks", file.TotalChunks);
            cmd.Parameters.AddWithValue("$changeType", file.ChangeType);
            cmd.Parameters.AddWithValue("$lastMod", file.LastModifiedTime.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$physPath", file.ServerPhysicalPath);
        }
    }
}
