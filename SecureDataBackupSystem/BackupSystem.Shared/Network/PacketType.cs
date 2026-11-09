using System;

namespace BackupSystem.Shared.Network
{
    /// <summary>
    /// Định nghĩa các loại gói tin trong giao thức TCP Packet Protocol của hệ thống Secure Data Backup.
    /// </summary>
    public enum PacketType : byte
    {
        // 0x01 - 0x0F: Quản lý kết nối & Xác thực (Handshake & Auth)
        HandshakeRequest = 0x01,
        HandshakeResponse = 0x02,
        AuthRequest = 0x03,
        AuthResponse = 0x04,
        HeartbeatPing = 0x08,
        HeartbeatPong = 0x09,

        // 0x10 - 0x2F: Quá trình Sao lưu (Backup Flow)
        BackupInitRequest = 0x10,       // Khởi tạo phiên truyền file backup (Metadata, Hash, Total Chunks)
        BackupInitResponse = 0x11,      // Server phản hồi chấp nhận hoặc thông báo resume chunk
        FileChunkData = 0x12,           // Dữ liệu từng chunk của file
        FileChunkAck = 0x13,            // Server xác nhận đã nhận chunk an toàn
        BackupCompleteRequest = 0x14,   // Client thông báo đã gửi hết chunks, yêu cầu ráp & kiểm tra SHA-256
        BackupCompleteResponse = 0x15,  // Server trả kết quả kiểm tra toàn vẹn và lưu trữ

        // 0x30 - 0x4F: Quá trình Phục hồi (Restore Flow)
        RestoreInitRequest = 0x30,      // Client yêu cầu restore file theo BackupId/RelativePath
        RestoreInitResponse = 0x31,     // Server trả thông tin file (TotalBytes, TotalChunks, Sha256)
        RestoreChunkRequest = 0x32,     // Client yêu cầu chunk cụ thể
        RestoreChunkData = 0x33,        // Server gửi chunk data cho Client
        RestoreCompleteAck = 0x34,      // Client xác nhận đã nhận đủ và kiểm tra SHA-256 khớp

        // 0x50 - 0x5F: Đồng bộ Danh mục & Manifest (Manifest Sync)
        ManifestUploadRequest = 0x50,
        ManifestUploadResponse = 0x51,
        ManifestListRequest = 0x52,
        ManifestListResponse = 0x53,

        // 0xF0 - 0xFF: Thông báo trạng thái & Lỗi hệ thống
        WarningNotification = 0xF0,
        ErrorMessage = 0xFE,
        Disconnect = 0xFF
    }
}

