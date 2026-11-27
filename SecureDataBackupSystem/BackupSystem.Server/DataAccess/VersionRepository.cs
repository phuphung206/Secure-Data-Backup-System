using System;
using System.Collections.Generic;
using System.Globalization;
using BackupSystem.Shared.Models;
using Microsoft.Data.Sqlite;

namespace BackupSystem.Server.DataAccess
{
    /// <summary>
    /// Truy cập bảng BackupVersions / BackedUpFiles: quản lý lịch sử phiên bản (Version Control).
    ///
    /// Mỗi phiên bản là một SNAPSHOT đầy đủ ở mức metadata: file được gửi trong lần backup này có ChangeType
    /// ADDED/MODIFIED và trỏ tới file vật lý mới; file không đổi được "kế thừa" (UNCHANGED) từ phiên bản trước
    /// và vẫn trỏ tới file vật lý cũ. Nhờ vậy khôi phục về một thời điểm chỉ cần đọc danh sách file của đúng
    /// phiên bản đó, trong khi dữ liệu vật lý không bị nhân bản.
    /// </summary>
    public class VersionRepository
    {
        private const string DateFormat = "yyyy-MM-dd HH:mm:ss.fff";
        private readonly string? _dbFilePath;

        /// <param name="dbFilePath">Đường dẫn file SQLite (null = CSDL mặc định).</param>
        public VersionRepository(string? dbFilePath = null)
        {
            _dbFilePath = dbFilePath;
        }

        private SqliteConnection Open() => DbConnectionFactory.GetConnection(_dbFilePath);

        // 1. Tạo Version mới (tự tìm ParentVersionId là bản COMPLETED gần nhất của cùng thư mục gốc)
        public int CreateBackupVersion(
            int userId,
            string backupType,
            string rootClientPath,
            string triggerType = "MANUAL",
            int? configId = null,
            string clientBackupId = "",
            string clientMachineName = "")
        {
            int? parentVersionId = GetLatestCompletedVersionId(userId, rootClientPath);

            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO BackupVersions (UserId, ConfigId, ParentVersionId, BackupType, TriggerType, RootClientPath, Status,
                                            ClientBackupId, ClientMachineName, CreatedAt)
                VALUES ($userId, $cfgId, $parentId, $type, $trigger, $rootPath, 'IN_PROGRESS', $backupId, $machine, $createdAt);
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$cfgId", (object?)configId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$parentId", (object?)parentVersionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$type", backupType.ToUpperInvariant());
            cmd.Parameters.AddWithValue("$trigger", triggerType.ToUpperInvariant());
            cmd.Parameters.AddWithValue("$rootPath", rootClientPath);
            cmd.Parameters.AddWithValue("$backupId", clientBackupId ?? string.Empty);
            cmd.Parameters.AddWithValue("$machine", clientMachineName ?? string.Empty);
            cmd.Parameters.AddWithValue("$createdAt", DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture));

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Lấy VersionId của bản Backup thành công gần nhất để làm cha (ParentVersionId)
        public int? GetLatestCompletedVersionId(int userId, string rootClientPath)
        {
            using var conn = Open();
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

        public BackupVersion? GetVersion(int versionId)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = VersionSelectSql + " WHERE VersionId = $verId;";
            cmd.Parameters.AddWithValue("$verId", versionId);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadVersion(reader) : null;
        }

        // 2. Lưu 1 bản ghi FileMetadata
        public int AddFileMetadata(FileMetadata file)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = FileInsertSql + " SELECT last_insert_rowid();";

            BindFileParameters(cmd, file);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // 3. Lưu hàng loạt FileMetadata bằng Transaction
        public void SaveFileMetadataBatch(IEnumerable<FileMetadata> files)
        {
            using var conn = Open();
            using var transaction = conn.BeginTransaction();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = FileInsertSql;

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

        /// <summary>
        /// Xóa bản ghi cũ của cùng một file trong phiên bản (khi Client gửi lại file) để mỗi đường dẫn chỉ có một dòng.
        /// </summary>
        public void RemoveFileFromVersion(int versionId, string relativePath)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM BackedUpFiles WHERE VersionId = $verId AND RelativePath = $relPath;";
            cmd.Parameters.AddWithValue("$verId", versionId);
            cmd.Parameters.AddWithValue("$relPath", relativePath);
            cmd.ExecuteNonQuery();
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

        /// <summary>
        /// Dựng snapshot cho phiên bản mới: kế thừa từ phiên bản cha những file vẫn còn ở Client
        /// (có trong <paramref name="currentClientPaths"/>) nhưng không được gửi lại trong phiên này.
        /// File đã bị xóa ở Client sẽ không được kế thừa → không xuất hiện trong snapshot mới.
        /// </summary>
        public int CarryOverFromParent(int parentVersionId, int newVersionId, ICollection<string> currentClientPaths)
        {
            var uploaded = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in GetFilesByVersion(newVersionId))
                uploaded.Add(file.RelativePath);

            var current = new HashSet<string>(currentClientPaths, StringComparer.Ordinal);
            var carried = new List<FileMetadata>();

            foreach (var oldFile in GetFilesByVersion(parentVersionId))
            {
                if (uploaded.Contains(oldFile.RelativePath) || !current.Contains(oldFile.RelativePath))
                    continue;

                oldFile.VersionId = newVersionId;
                oldFile.ChangeType = "UNCHANGED";
                carried.Add(oldFile);
            }

            if (carried.Count > 0)
                SaveFileMetadataBatch(carried);

            return carried.Count;
        }

        // 5. Hoàn tất phiên bản Backup
        public void CompleteBackupVersion(int versionId, string finalStatus = "COMPLETED")
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE BackupVersions
                SET Status = $status,
                    TotalFiles = (SELECT COUNT(1) FROM BackedUpFiles WHERE VersionId = $verId AND ChangeType != 'DELETED'),
                    TotalSizeBytes = IFNULL((SELECT SUM(EncryptedSizeBytes) FROM BackedUpFiles WHERE VersionId = $verId AND ChangeType != 'DELETED'), 0),
                    OriginalSizeBytes = IFNULL((SELECT SUM(OriginalSizeBytes) FROM BackedUpFiles WHERE VersionId = $verId AND ChangeType != 'DELETED'), 0),
                    CompletedAt = $completedAt
                WHERE VersionId = $verId;";

            cmd.Parameters.AddWithValue("$status", finalStatus);
            cmd.Parameters.AddWithValue("$verId", versionId);
            cmd.Parameters.AddWithValue("$completedAt", DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }

        // 6. Lấy danh sách lịch sử các phiên bản của User (mới nhất trước)
        public List<BackupVersion> GetVersionsByUser(int userId, string? rootClientPath = null, bool includeFailed = false)
        {
            var list = new List<BackupVersion>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();

            string sql = VersionSelectSql + " WHERE UserId = $userId";
            if (!string.IsNullOrWhiteSpace(rootClientPath))
            {
                sql += " AND RootClientPath = $rootPath";
                cmd.Parameters.AddWithValue("$rootPath", rootClientPath);
            }
            if (!includeFailed)
            {
                sql += " AND Status = 'COMPLETED'";
            }
            sql += " ORDER BY VersionId DESC;";

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$userId", userId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadVersion(reader));
            }
            return list;
        }

        // 7. Lấy toàn bộ danh sách file thuộc 1 Version (hoặc lấy 1 file lẻ để Restore)
        public List<FileMetadata> GetFilesByVersion(int versionId, string? singleRelativePath = null)
        {
            var files = new List<FileMetadata>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();

            string sql = @"
                SELECT FileId, VersionId, RelativePath, FileExtension, Sha256Hash, OriginalSizeBytes,
                       EncryptedSizeBytes, AesIv, TotalChunks, ChangeType, LastModifiedTime, ServerPhysicalPath,
                       IFNULL(IsEncrypted, 0), IFNULL(IsCompressed, 0), IFNULL(SourceVersionId, VersionId)
                FROM BackedUpFiles
                WHERE VersionId = $verId AND ChangeType != 'DELETED'";

            if (!string.IsNullOrEmpty(singleRelativePath))
            {
                sql += " AND RelativePath = $relPath";
                cmd.Parameters.AddWithValue("$relPath", singleRelativePath);
            }

            cmd.CommandText = sql + " ORDER BY RelativePath;";
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
                    LastModifiedTime = ParseDate(reader.GetString(10)),
                    ServerPhysicalPath = reader.GetString(11),
                    IsEncrypted = reader.GetInt64(12) != 0,
                    IsCompressed = reader.GetInt64(13) != 0,
                    SourceVersionId = reader.GetInt32(14)
                });
            }
            return files;
        }

        /// <summary>
        /// Danh sách thư mục BackupStore (ClientBackupId) thuộc về người dùng - dùng để phân quyền khi liệt kê/khôi phục.
        /// </summary>
        public HashSet<string> GetClientBackupIds(int userId)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT ClientBackupId FROM BackupVersions WHERE UserId = $userId AND IFNULL(ClientBackupId, '') <> '';";
            cmd.Parameters.AddWithValue("$userId", userId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetString(0));
            return ids;
        }

        /// <summary>
        /// Đánh dấu FAILED các phiên bản còn IN_PROGRESS (ví dụ khi Client rớt mạng giữa chừng).
        /// </summary>
        public void MarkVersionFailed(int versionId)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE BackupVersions SET Status = 'FAILED', CompletedAt = $completedAt
                WHERE VersionId = $verId AND Status = 'IN_PROGRESS';";
            cmd.Parameters.AddWithValue("$verId", versionId);
            cmd.Parameters.AddWithValue("$completedAt", DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }

        private const string VersionSelectSql = @"
                SELECT VersionId, UserId, ConfigId, ParentVersionId, BackupType, TriggerType,
                       RootClientPath, TotalFiles, TotalSizeBytes, Status, CreatedAt,
                       IFNULL(ClientBackupId, ''), IFNULL(ClientMachineName, ''), IFNULL(OriginalSizeBytes, 0)
                FROM BackupVersions";

        private const string FileInsertSql = @"
                INSERT INTO BackedUpFiles
                (VersionId, RelativePath, FileExtension, Sha256Hash, OriginalSizeBytes, EncryptedSizeBytes, AesIv, TotalChunks,
                 ChangeType, LastModifiedTime, ServerPhysicalPath, IsEncrypted, IsCompressed, SourceVersionId)
                VALUES ($verId, $relPath, $ext, $hash, $origSize, $encSize, $iv, $chunks,
                        $changeType, $lastMod, $physPath, $isEnc, $isComp, $srcVer);";

        private static BackupVersion ReadVersion(SqliteDataReader reader)
        {
            return new BackupVersion
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
                CreatedAt = ParseDate(reader.GetString(10)),
                ClientBackupId = reader.GetString(11),
                ClientMachineName = reader.GetString(12),
                OriginalSizeBytes = reader.GetInt64(13)
            };
        }

        private static DateTime ParseDate(string value)
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
                return parsed;
            return DateTime.MinValue;
        }

        private static void BindFileParameters(SqliteCommand cmd, FileMetadata file)
        {
            cmd.Parameters.AddWithValue("$verId", file.VersionId);
            cmd.Parameters.AddWithValue("$relPath", file.RelativePath);
            cmd.Parameters.AddWithValue("$ext", file.FileExtension ?? string.Empty);
            cmd.Parameters.AddWithValue("$hash", file.Sha256Hash);
            cmd.Parameters.AddWithValue("$origSize", file.OriginalSizeBytes);
            cmd.Parameters.AddWithValue("$encSize", file.EncryptedSizeBytes);
            cmd.Parameters.AddWithValue("$iv", file.AesIv ?? string.Empty);
            cmd.Parameters.AddWithValue("$chunks", file.TotalChunks);
            cmd.Parameters.AddWithValue("$changeType", file.ChangeType);
            cmd.Parameters.AddWithValue("$lastMod", file.LastModifiedTime.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$physPath", file.ServerPhysicalPath);
            cmd.Parameters.AddWithValue("$isEnc", file.IsEncrypted ? 1 : 0);
            cmd.Parameters.AddWithValue("$isComp", file.IsCompressed ? 1 : 0);
            cmd.Parameters.AddWithValue("$srcVer", file.SourceVersionId > 0 ? file.SourceVersionId : file.VersionId);
        }
    }
}
