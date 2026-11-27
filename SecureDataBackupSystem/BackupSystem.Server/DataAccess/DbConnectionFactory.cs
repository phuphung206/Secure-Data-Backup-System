using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace BackupSystem.Server.DataAccess
{
    public static class DbConnectionFactory
    {
        public static readonly string ServerDataRoot = BackupSystem.Shared.Models.BackupPaths.GetBackupRoot();
        public static readonly string DbDirectory = Path.Combine(ServerDataRoot, "Database");
        public static readonly string DbFilePath = Path.Combine(DbDirectory, "BackupServer.sqlite");

        /// <summary>
        /// Đường dẫn CSDL nằm cùng kho lưu trữ của Server: &lt;StorageRoot&gt;/Database/BackupServer.sqlite.
        /// </summary>
        public static string GetDbFilePathForStorage(string storageRoot)
        {
            return Path.Combine(Path.GetFullPath(storageRoot), "Database", "BackupServer.sqlite");
        }

        public static SqliteConnection GetConnection(string? dbFilePath = null)
        {
            string path = string.IsNullOrWhiteSpace(dbFilePath) ? DbFilePath : Path.GetFullPath(dbFilePath);
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();

            using var pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            pragmaCmd.ExecuteNonQuery();

            return connection;
        }

        public static void InitializeDatabase(string? dbFilePath = null)
        {
            using var conn = GetConnection(dbFilePath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Users (
                    UserId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL UNIQUE,
                    PasswordHash TEXT NOT NULL,
                    MachineName TEXT,
                    QuotaBytes INTEGER DEFAULT 10737418240,
                    UsedBytes INTEGER DEFAULT 0,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS BackupConfigs (
                    ConfigId INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    SourceFolderPath TEXT NOT NULL,
                    AllowedExtensions TEXT DEFAULT '*.*',
                    ExcludedPatterns TEXT DEFAULT '.tmp;.log;bin;obj',
                    ScheduleCron TEXT DEFAULT 'DAILY_00:00',
                    EnableRealtimeWatch INTEGER DEFAULT 1,
                    IsActive INTEGER DEFAULT 1,
                    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS BackupVersions (
                    VersionId INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    ConfigId INTEGER,
                    ParentVersionId INTEGER,
                    BackupType TEXT NOT NULL CHECK(BackupType IN ('FULL', 'INCREMENTAL')),
                    TriggerType TEXT DEFAULT 'MANUAL',
                    RootClientPath TEXT NOT NULL,
                    TotalFiles INTEGER DEFAULT 0,
                    TotalSizeBytes INTEGER DEFAULT 0,
                    Status TEXT NOT NULL DEFAULT 'IN_PROGRESS',
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE,
                    FOREIGN KEY (ConfigId) REFERENCES BackupConfigs(ConfigId) ON DELETE SET NULL,
                    FOREIGN KEY (ParentVersionId) REFERENCES BackupVersions(VersionId) ON DELETE SET NULL
                );

                CREATE TABLE IF NOT EXISTS BackedUpFiles (
                    FileId INTEGER PRIMARY KEY AUTOINCREMENT,
                    VersionId INTEGER NOT NULL,
                    RelativePath TEXT NOT NULL,
                    FileExtension TEXT,
                    Sha256Hash TEXT NOT NULL,
                    OriginalSizeBytes INTEGER NOT NULL,
                    EncryptedSizeBytes INTEGER NOT NULL,
                    AesIv TEXT NOT NULL,
                    TotalChunks INTEGER DEFAULT 1,
                    ChangeType TEXT NOT NULL DEFAULT 'MODIFIED',
                    LastModifiedTime DATETIME NOT NULL,
                    ServerPhysicalPath TEXT NOT NULL,
                    FOREIGN KEY (VersionId) REFERENCES BackupVersions(VersionId) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS RestoreLogs (
                    RestoreId INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    VersionId INTEGER NOT NULL,
                    RestoreScope TEXT NOT NULL CHECK(RestoreScope IN ('FULL_FOLDER', 'SINGLE_FILE')),
                    TargetFilePath TEXT NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'COMPLETED',
                    RestoredAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE,
                    FOREIGN KEY (VersionId) REFERENCES BackupVersions(VersionId) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS SecurityLogs (
                    LogId INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    EventType TEXT NOT NULL,
                    AnomalyScore REAL DEFAULT 0.0,
                    AffectedFilesCount INTEGER DEFAULT 0,
                    Description TEXT,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_versions_user ON BackupVersions(UserId, CreatedAt DESC);
                CREATE INDEX IF NOT EXISTS idx_files_version ON BackedUpFiles(VersionId);
                CREATE INDEX IF NOT EXISTS idx_files_hash ON BackedUpFiles(Sha256Hash);
            ";
            cmd.ExecuteNonQuery();

            MigrateSchema(conn);
            SeedDefaultUserAndConfig(conn);
        }

        /// <summary>
        /// Bổ sung các cột mới cho CSDL đã tạo từ phiên bản cũ (CREATE TABLE IF NOT EXISTS không tự thêm cột).
        /// </summary>
        private static void MigrateSchema(SqliteConnection conn)
        {
            AddColumnIfMissing(conn, "BackupVersions", "ClientBackupId", "TEXT");
            AddColumnIfMissing(conn, "BackupVersions", "ClientMachineName", "TEXT");
            AddColumnIfMissing(conn, "BackupVersions", "OriginalSizeBytes", "INTEGER DEFAULT 0");
            AddColumnIfMissing(conn, "BackupVersions", "CompletedAt", "DATETIME");
            AddColumnIfMissing(conn, "BackedUpFiles", "IsEncrypted", "INTEGER DEFAULT 0");
            AddColumnIfMissing(conn, "BackedUpFiles", "IsCompressed", "INTEGER DEFAULT 0");
            AddColumnIfMissing(conn, "BackedUpFiles", "SourceVersionId", "INTEGER");

            using var idxCmd = conn.CreateCommand();
            idxCmd.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_versions_root ON BackupVersions(UserId, RootClientPath, Status, VersionId DESC);
                CREATE INDEX IF NOT EXISTS idx_files_version_path ON BackedUpFiles(VersionId, RelativePath);";
            idxCmd.ExecuteNonQuery();
        }

        private static void AddColumnIfMissing(SqliteConnection conn, string table, string column, string definition)
        {
            using (var check = conn.CreateCommand())
            {
                check.CommandText = $"PRAGMA table_info({table});";
                using var reader = check.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }

            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
            alter.ExecuteNonQuery();
        }

        private static void SeedDefaultUserAndConfig(SqliteConnection conn)
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(1) FROM Users WHERE Username = 'admin';";
            long count = Convert.ToInt64(checkCmd.ExecuteScalar());

            if (count == 0)
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO Users (Username, PasswordHash, MachineName, QuotaBytes)
                    VALUES ('admin', $passHash, 'DEFAULT-CLIENT-PC', 10737418240);
                    
                    INSERT INTO BackupConfigs (UserId, SourceFolderPath, AllowedExtensions, ScheduleCron, EnableRealtimeWatch)
                    VALUES (last_insert_rowid(), 'D:\ImportantData', '.docx;.xlsx;.pdf;.cs', 'DAILY_23:00', 1);";
                insertCmd.Parameters.AddWithValue("$passHash", HashPassword("123456"));
                insertCmd.ExecuteNonQuery();
            }
        }

        private const int PasswordHashIterations = 100000;

        /// <summary>
        /// Băm mật khẩu bằng PBKDF2-SHA256 có salt ngẫu nhiên: "pbkdf2$&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;".
        /// </summary>
        public static string HashPassword(string plainPassword)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(plainPassword, salt, PasswordHashIterations, HashAlgorithmName.SHA256, 32);
            return $"pbkdf2${PasswordHashIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        /// <summary>
        /// Kiểm tra mật khẩu. Hỗ trợ cả định dạng cũ (SHA-256 hex không salt) để không khóa tài khoản đã tạo trước đây.
        /// </summary>
        public static bool VerifyPassword(string plainPassword, string storedHash)
        {
            if (string.IsNullOrEmpty(storedHash) || plainPassword == null)
                return false;

            if (storedHash.StartsWith("pbkdf2$", StringComparison.Ordinal))
            {
                string[] parts = storedHash.Split('$');
                if (parts.Length != 4 || !int.TryParse(parts[1], out int iterations) || iterations <= 0)
                    return false;

                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expected = Convert.FromBase64String(parts[3]);
                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(plainPassword, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }

            byte[] legacy = SHA256.HashData(Encoding.UTF8.GetBytes(plainPassword));
            byte[] stored;
            try
            {
                stored = Convert.FromHexString(storedHash);
            }
            catch (FormatException)
            {
                return false;
            }
            return CryptographicOperations.FixedTimeEquals(legacy, stored);
        }

        public static bool IsLegacyPasswordHash(string storedHash)
        {
            return !string.IsNullOrEmpty(storedHash) && !storedHash.StartsWith("pbkdf2$", StringComparison.Ordinal);
        }
    }
}
