using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace BackupSystem.Server.DataAccess
{
    public static class DbConnectionFactory
    {
        public static readonly string ServerDataRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerDataRoot");
        public static readonly string DbDirectory = Path.Combine(ServerDataRoot, "Database");
        public static readonly string DbFilePath = Path.Combine(DbDirectory, "BackupServer.sqlite");
        private static readonly string ConnectionString = $"Data Source={DbFilePath}";

        public static SqliteConnection GetConnection()
        {
            if (!Directory.Exists(DbDirectory))
            {
                Directory.CreateDirectory(DbDirectory);
            }

            var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA foreign_keys = ON;";
            pragmaCmd.ExecuteNonQuery();

            return connection;
        }

        public static void InitializeDatabase()
        {
            using var conn = GetConnection();
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

            SeedDefaultUserAndConfig(conn);
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

        public static string HashPassword(string plainPassword)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainPassword));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
