using System;

namespace BackupSystem.Shared.Models
{
    /// <summary>
    /// Gói tin bắt tay (Handshake) thiết lập phiên làm việc ban đầu giữa Client và Server.
    /// </summary>
    public sealed class HandshakeRequestPayload
    {
        public byte ProtocolVersion { get; set; } = Network.NetworkProtocolConstants.ProtocolVersion;
        public string ClientId { get; set; } = Environment.MachineName;
        public string ClientAppVersion { get; set; } = "1.0.0";
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Server phản hồi Handshake xác nhận phiên kết nối.
    /// </summary>
    public sealed class HandshakeResponsePayload
    {
        public bool IsCompatible { get; set; }
        public byte ServerProtocolVersion { get; set; } = Network.NetworkProtocolConstants.ProtocolVersion;
        public string ServerName { get; set; } = "SecureBackupServer";
        public string Message { get; set; } = string.Empty;
        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    }
}

