PRAGMA foreign_keys = ON;

-- 1. Bảng Người dùng / Máy trạm
CREATE TABLE IF NOT EXISTS Users (
    UserId INTEGER PRIMARY KEY AUTOINCREMENT,
    Username TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    MachineName TEXT,
    QuotaBytes INTEGER DEFAULT 10737418240, -- 10 GB
    UsedBytes INTEGER DEFAULT 0,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 2. Bảng Cấu hình & Lịch trình Sao lưu (Scheduler & Watcher)
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

-- 3. Bảng Lịch sử Phiên bản Sao lưu (Version Control)
CREATE TABLE IF NOT EXISTS BackupVersions (
    VersionId INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    ConfigId INTEGER,
    ParentVersionId INTEGER, -- Trỏ về Version trước nếu là INCREMENTAL
    BackupType TEXT NOT NULL CHECK(BackupType IN ('FULL', 'INCREMENTAL')),
    TriggerType TEXT DEFAULT 'MANUAL', -- MANUAL | SCHEDULER | WATCHER
    RootClientPath TEXT NOT NULL,
    TotalFiles INTEGER DEFAULT 0,
    TotalSizeBytes INTEGER DEFAULT 0,
    Status TEXT NOT NULL DEFAULT 'IN_PROGRESS', -- IN_PROGRESS | COMPLETED | BLOCKED_BY_AI | FAILED
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE,
    FOREIGN KEY (ConfigId) REFERENCES BackupConfigs(ConfigId) ON DELETE SET NULL,
    FOREIGN KEY (ParentVersionId) REFERENCES BackupVersions(VersionId) ON DELETE SET NULL
);

-- 4. Bảng Metadata từng File & Ánh xạ lưu trữ vật lý
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
    ChangeType TEXT NOT NULL DEFAULT 'MODIFIED', -- ADDED | MODIFIED | UNCHANGED | DELETED
    LastModifiedTime DATETIME NOT NULL,
    ServerPhysicalPath TEXT NOT NULL,
    FOREIGN KEY (VersionId) REFERENCES BackupVersions(VersionId) ON DELETE CASCADE
);

-- 5. Bảng Nhật ký Phục hồi dữ liệu (Restore History)
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

-- 6. Bảng Nhật ký Cảnh báo Bảo mật & AI Ransomware
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

-- Indexes tối ưu hóa truy vấn
CREATE INDEX IF NOT EXISTS idx_versions_user ON BackupVersions(UserId, CreatedAt DESC);
CREATE INDEX IF NOT EXISTS idx_files_version ON BackedUpFiles(VersionId);
CREATE INDEX IF NOT EXISTS idx_files_hash ON BackedUpFiles(Sha256Hash);
