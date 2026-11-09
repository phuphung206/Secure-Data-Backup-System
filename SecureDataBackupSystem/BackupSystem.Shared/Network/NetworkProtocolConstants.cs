using System;

namespace BackupSystem.Shared.Network
{
    /// <summary>
    /// Các hằng số quy định giao thức mạng TCP Packet Protocol.
    /// </summary>
    public static class NetworkProtocolConstants
    {
        /// <summary>
        /// 4 bytes nhận diện giao thức Secure Data Backup System ("SDBS" = 0x53, 0x44, 0x42, 0x53).
        /// Dùng để định vị đầu gói tin và loại bỏ các gói tin rác / tấn công.
        /// </summary>
        public static readonly byte[] MagicBytes = new byte[] { 0x53, 0x44, 0x42, 0x53 };
        public const uint MagicNumber = 0x53444253; // "SDBS" in ASCII

        /// <summary>
        /// Phiên bản giao thức hiện tại.
        /// </summary>
        public const byte ProtocolVersion = 0x01;

        /// <summary>
        /// Kích thước Header cố định (16 bytes):
        /// - 4 bytes: Magic ("SDBS")
        /// - 1 byte : Version (0x01)
        /// - 1 byte : PacketType
        /// - 2 bytes: Flags (ushort: IsCompressed, IsEncrypted, HasChecksum)
        /// - 4 bytes: PayloadLength (int32)
        /// - 4 bytes: Checksum (CRC-32 của Payload)
        /// </summary>
        public const int HeaderSize = 16;

        /// <summary>
        /// Kích thước chunk mặc định cho cơ chế Chunking: 64 KB (65,536 bytes).
        /// Cân bằng tối ưu giữa thông lượng mạng TCP và dung lượng RAM.
        /// </summary>
        public const int DefaultChunkSize = 64 * 1024; // 64 KB

        /// <summary>
        /// Kích thước chunk tối đa cho phép: 1 MB.
        /// </summary>
        public const int MaxChunkSize = 1024 * 1024; // 1 MB

        /// <summary>
        /// Kích thước chunk tối thiểu: 4 KB.
        /// </summary>
        public const int MinChunkSize = 4 * 1024; // 4 KB

        /// <summary>
        /// Kích thước Payload tối đa cho một gói tin: 16 MB (phòng chống Memory Exhaustion Attack).
        /// </summary>
        public const int MaxPayloadSize = 16 * 1024 * 1024; // 16 MB

        /// <summary>
        /// Cổng TCP mặc định cho Server.
        /// </summary>
        public const int DefaultPort = 5050;

        /// <summary>
        /// Kích thước buffer I/O mạng mặc định: 8 KB.
        /// </summary>
        public const int NetworkBufferSize = 8 * 1024;

        /// <summary>
        /// Thời gian chờ kết nối và I/O (Timeout): 30 giây.
        /// </summary>
        public const int DefaultTimeoutMs = 30000;
    }
}

