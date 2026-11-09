using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BackupSystem.Shared.Network
{
    /// <summary>
    /// Đại diện cho một gói tin TCP Packet Protocol hoàn chỉnh gồm Header (16 bytes) và Payload.
    /// Giải quyết triệt để vấn đề phân mảnh (Fragmentation) và dính gói (Coalescing) trong luồng TCP Stream.
    /// </summary>
    public sealed class NetworkPacket
    {
        public uint Magic { get; set; } = NetworkProtocolConstants.MagicNumber;
        public byte Version { get; set; } = NetworkProtocolConstants.ProtocolVersion;
        public PacketType Type { get; set; }
        public ushort Flags { get; set; }
        public int PayloadLength => Payload?.Length ?? 0;
        public uint Checksum { get; set; }
        public byte[] Payload { get; set; }

        public NetworkPacket()
        {
            Payload = Array.Empty<byte>();
        }

        public NetworkPacket(PacketType type, byte[]? payload = null, ushort flags = 0)
        {
            Magic = NetworkProtocolConstants.MagicNumber;
            Version = NetworkProtocolConstants.ProtocolVersion;
            Type = type;
            Flags = flags;
            Payload = payload ?? Array.Empty<byte>();
            Checksum = Crc32.Compute(Payload);
        }

        #region Factory Methods

        public static NetworkPacket Create(PacketType type, byte[]? payload = null, ushort flags = 0)
        {
            return new NetworkPacket(type, payload, flags);
        }

        public static NetworkPacket CreateError(string message)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message ?? "Unknown error");
            return new NetworkPacket(PacketType.ErrorMessage, bytes);
        }

        public static NetworkPacket CreateHeartbeatPing()
        {
            return new NetworkPacket(PacketType.HeartbeatPing, Array.Empty<byte>());
        }

        public static NetworkPacket CreateHeartbeatPong()
        {
            return new NetworkPacket(PacketType.HeartbeatPong, Array.Empty<byte>());
        }

        #endregion

        #region Serialization & Writing

        /// <summary>
        /// Đóng gói Header và Payload thành mảng byte để gửi.
        /// </summary>
        public byte[] ToBytes()
        {
            byte[] buffer = new byte[NetworkProtocolConstants.HeaderSize + PayloadLength];
            WriteHeaderToBuffer(buffer, 0);

            if (PayloadLength > 0 && Payload != null)
            {
                Buffer.BlockCopy(Payload, 0, buffer, NetworkProtocolConstants.HeaderSize, PayloadLength);
            }

            return buffer;
        }

        /// <summary>
        /// Ghi trực tiếp gói tin vào luồng mạng NetworkStream một cách đồng bộ.
        /// </summary>
        public void WriteToStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            byte[] packetBytes = ToBytes();
            stream.Write(packetBytes, 0, packetBytes.Length);
            stream.Flush();
        }

        /// <summary>
        /// Ghi trực tiếp gói tin vào luồng mạng NetworkStream một cách bất đồng bộ.
        /// </summary>
        public async Task WriteToStreamAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            byte[] packetBytes = ToBytes();
            await stream.WriteAsync(packetBytes, 0, packetBytes.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private void WriteHeaderToBuffer(byte[] buffer, int offset)
        {
            // 4 bytes Magic
            buffer[offset + 0] = (byte)(Magic & 0xFF);
            buffer[offset + 1] = (byte)((Magic >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((Magic >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((Magic >> 24) & 0xFF);

            // 1 byte Version
            buffer[offset + 4] = Version;

            // 1 byte PacketType
            buffer[offset + 5] = (byte)Type;

            // 2 bytes Flags
            buffer[offset + 6] = (byte)(Flags & 0xFF);
            buffer[offset + 7] = (byte)((Flags >> 8) & 0xFF);

            // 4 bytes PayloadLength (Little Endian)
            int len = PayloadLength;
            buffer[offset + 8] = (byte)(len & 0xFF);
            buffer[offset + 9] = (byte)((len >> 8) & 0xFF);
            buffer[offset + 10] = (byte)((len >> 16) & 0xFF);
            buffer[offset + 11] = (byte)((len >> 24) & 0xFF);

            // 4 bytes Checksum (CRC-32)
            buffer[offset + 12] = (byte)(Checksum & 0xFF);
            buffer[offset + 13] = (byte)((Checksum >> 8) & 0xFF);
            buffer[offset + 14] = (byte)((Checksum >> 16) & 0xFF);
            buffer[offset + 15] = (byte)((Checksum >> 24) & 0xFF);
        }

        #endregion

        #region Deserialization & Reading

        /// <summary>
        /// Đọc một gói tin hoàn chỉnh từ Stream một cách bất đồng bộ.
        /// Trả về null nếu luồng bị ngắt kết nối (End of Stream).
        /// </summary>
        public static async Task<NetworkPacket?> ReadFromStreamAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            byte[] headerBuffer = new byte[NetworkProtocolConstants.HeaderSize];
            bool headerRead = await ReadExactBytesAsync(stream, headerBuffer, 0, headerBuffer.Length, cancellationToken).ConfigureAwait(false);
            if (!headerRead)
                return null; // Connection closed gracefully

            var packet = ParseHeader(headerBuffer);

            if (packet.PayloadLength > 0)
            {
                if (packet.PayloadLength > NetworkProtocolConstants.MaxPayloadSize)
                    throw new InvalidDataException($"Kích thước Payload ({packet.PayloadLength} bytes) vượt quá giới hạn an toàn {NetworkProtocolConstants.MaxPayloadSize} bytes.");

                byte[] payloadBuffer = new byte[packet.PayloadLength];
                bool payloadRead = await ReadExactBytesAsync(stream, payloadBuffer, 0, payloadBuffer.Length, cancellationToken).ConfigureAwait(false);
                if (!payloadRead)
                    throw new EndOfStreamException("Mất kết nối trước khi nhận đủ dữ liệu Payload.");

                // Kiểm tra tính toàn vẹn CRC-32 của Payload
                uint calculatedCrc = Crc32.Compute(payloadBuffer);
                if (calculatedCrc != packet.Checksum)
                {
                    throw new InvalidDataException($"Lỗi toàn vẹn dữ liệu: Checksum CRC-32 không khớp (Nhận: 0x{packet.Checksum:X8}, Tính toán: 0x{calculatedCrc:X8}).");
                }

                packet.Payload = payloadBuffer;
            }
            else
            {
                packet.Payload = Array.Empty<byte>();
            }

            return packet;
        }

        /// <summary>
        /// Đọc một gói tin hoàn chỉnh từ Stream một cách đồng bộ.
        /// </summary>
        public static NetworkPacket? ReadFromStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            byte[] headerBuffer = new byte[NetworkProtocolConstants.HeaderSize];
            bool headerRead = ReadExactBytes(stream, headerBuffer, 0, headerBuffer.Length);
            if (!headerRead)
                return null;

            var packet = ParseHeader(headerBuffer);

            if (packet.PayloadLength > 0)
            {
                if (packet.PayloadLength > NetworkProtocolConstants.MaxPayloadSize)
                    throw new InvalidDataException($"Kích thước Payload ({packet.PayloadLength} bytes) vượt quá giới hạn an toàn {NetworkProtocolConstants.MaxPayloadSize} bytes.");

                byte[] payloadBuffer = new byte[packet.PayloadLength];
                bool payloadRead = ReadExactBytes(stream, payloadBuffer, 0, payloadBuffer.Length);
                if (!payloadRead)
                    throw new EndOfStreamException("Mất kết nối trước khi nhận đủ dữ liệu Payload.");

                uint calculatedCrc = Crc32.Compute(payloadBuffer);
                if (calculatedCrc != packet.Checksum)
                {
                    throw new InvalidDataException($"Lỗi toàn vẹn dữ liệu: Checksum CRC-32 không khớp (Nhận: 0x{packet.Checksum:X8}, Tính toán: 0x{calculatedCrc:X8}).");
                }

                packet.Payload = payloadBuffer;
            }
            else
            {
                packet.Payload = Array.Empty<byte>();
            }

            return packet;
        }

        private static NetworkPacket ParseHeader(byte[] buffer)
        {
            uint magic = (uint)(buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24));
            if (magic != NetworkProtocolConstants.MagicNumber)
            {
                throw new InvalidDataException($"Gói tin không hợp lệ: Sai Magic Number (Nhận: 0x{magic:X8}, Kỳ vọng: 0x{NetworkProtocolConstants.MagicNumber:X8}).");
            }

            byte version = buffer[4];
            PacketType type = (PacketType)buffer[5];
            ushort flags = (ushort)(buffer[6] | (buffer[7] << 8));
            int payloadLength = buffer[8] | (buffer[9] << 8) | (buffer[10] << 16) | (buffer[11] << 24);
            uint checksum = (uint)(buffer[12] | (buffer[13] << 8) | (buffer[14] << 16) | (buffer[15] << 24));

            if (payloadLength < 0)
                throw new InvalidDataException($"Kích thước Payload âm ({payloadLength}) không hợp lệ.");

            return new NetworkPacket
            {
                Magic = magic,
                Version = version,
                Type = type,
                Flags = flags,
                Checksum = checksum,
                Payload = new byte[payloadLength] // will be filled by caller
            };
        }

        /// <summary>
        /// Hàm đọc chính xác số lượng byte yêu cầu qua TCP Socket (giải quyết triệt để TCP Fragmentation).
        /// </summary>
        public static async Task<bool> ReadExactBytesAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                int bytesRead = await stream.ReadAsync(buffer, offset + totalBytesRead, count - totalBytesRead, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    if (totalBytesRead == 0)
                        return false; // Đóng kết nối bình thường ở đầu gói tin
                    throw new EndOfStreamException($"Ngắt kết nối đột ngột giữa gói tin: Đã đọc {totalBytesRead}/{count} bytes.");
                }
                totalBytesRead += bytesRead;
            }
            return true;
        }

        /// <summary>
        /// Hàm đọc chính xác số lượng byte yêu cầu (đồng bộ).
        /// </summary>
        public static bool ReadExactBytes(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                int bytesRead = stream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
                if (bytesRead == 0)
                {
                    if (totalBytesRead == 0)
                        return false;
                    throw new EndOfStreamException($"Ngắt kết nối đột ngột giữa gói tin: Đã đọc {totalBytesRead}/{count} bytes.");
                }
                totalBytesRead += bytesRead;
            }
            return true;
        }

        #endregion
    }
}

