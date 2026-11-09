erDiagram
    USERS ||--o{ BACKUP_VERSIONS : "thực hiện (1-N)"
    USERS ||--o{ SECURITY_LOGS : "ghi nhận (1-N)"
    BACKUP_VERSIONS ||--o{ BACKED_UP_FILES : "bao gồm (1-N)"

    USERS {
        int UserId PK "Khóa chính (Auto Increment)"
        varchar Username UK "Tên đăng nhập duy nhất"
        varchar PasswordHash "Mật khẩu đã băm (SHA-256/BCrypt)"
        varchar MachineName "Tên máy tính Client"
        bigint QuotaBytes "Giới hạn dung lượng lưu trữ"
        bigint UsedBytes "Dung lượng đã sử dụng"
        datetime CreatedAt "Ngày tạo tài khoản"
    }

    BACKUP_VERSIONS {
        int VersionId PK "Khóa chính (Auto Increment)"
        int UserId FK "Tham chiếu USERS(UserId)"
        varchar BackupType "FULL hoặc INCREMENTAL"
        varchar RootClientPath "Thư mục gốc trên máy Client"
        int TotalFiles "Tổng số file trong phiên bản"
        bigint TotalSizeBytes "Tổng dung lượng (Bytes)"
        varchar Status "IN_PROGRESS | COMPLETED | BLOCKED_BY_AI | FAILED"
        datetime CreatedAt "Thời điểm sao lưu"
    }

    BACKED_UP_FILES {
        int FileId PK "Khóa chính (Auto Increment)"
        int VersionId FK "Tham chiếu BACKUP_VERSIONS(VersionId)"
        varchar RelativePath "Đường dẫn tương đối của file"
        varchar FileExtension "Đuôi file (.docx, .pdf...)"
        char Sha256Hash "Mã băm SHA-256 của file gốc (64 ký tự)"
        bigint OriginalSizeBytes "Dung lượng trước khi nén/mã hóa"
        bigint EncryptedSizeBytes "Dung lượng lưu trên Server"
        varchar AesIv "Vector khởi tạo (IV) 16-byte dạng Base64 cho AES-256"
        varchar ChangeType "ADDED | MODIFIED | UNCHANGED | DELETED"
        datetime LastModifiedTime "Thời gian sửa đổi cuối ở Client"
        varchar ServerPhysicalPath "Đường dẫn file .enc trên ổ cứng Server"
    }

    SECURITY_LOGS {
        int LogId PK "Khóa chính (Auto Increment)"
        int UserId FK "Tham chiếu USERS(UserId)"
        varchar EventType "RANSOMWARE_DETECTED | AUTH_FAILED | CONNECTION_DROP"
        float AnomalyScore "Điểm bất thường từ Python AI (0.0 - 1.0)"
        text Description "Chi tiết (VD: Đổi đuôi 50 file trong 2 giây)"
        datetime CreatedAt "Thời gian phát hiện"
    }
