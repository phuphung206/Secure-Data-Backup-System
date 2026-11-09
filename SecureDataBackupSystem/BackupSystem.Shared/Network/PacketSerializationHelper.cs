using System;
using System.IO;
using System.Text;
using BackupSystem.Shared.Models;

#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif

namespace BackupSystem.Shared.Network
{
    /// <summary>
    /// Hỗ trợ tuần tự hóa (Serialization) và giải tuần tự hóa (Deserialization) payload của các gói tin.
    /// Tối ưu hóa: Dữ liệu điều khiển (Control) dùng JSON; Dữ liệu FileChunk dùng Binary Packing trực tiếp để đạt tốc độ truyền tải tối đa.
    /// Tương thích song song cả .NET Framework 4.7.2 và .NET 6.0/8.0/10.0+.
    /// </summary>
    public static class PacketSerializationHelper
    {
#if NETFRAMEWORK
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
#else
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
#endif

        #region Generic JSON Serialization

        public static byte[] SerializeJson<T>(T obj)
        {
            if (obj == null) return Array.Empty<byte>();

#if NETFRAMEWORK
            string json = Serializer.Serialize(obj);
#else
            string json = JsonSerializer.Serialize(obj, JsonOptions);
#endif
            return Encoding.UTF8.GetBytes(json);
        }

        public static T? DeserializeJson<T>(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return default;
            string json = Encoding.UTF8.GetString(payload);

#if NETFRAMEWORK
            return Serializer.Deserialize<T>(json);
#else
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
#endif
        }

        #endregion

        #region Optimized Binary Packing for FileChunkData

        /// <summary>
        /// Đóng gói FileChunk thành nhị phân trực tiếp (Zero Base64 overhead):
        /// - 4 bytes: TransferId UTF-8 byte length (L)
        /// - L bytes: TransferId UTF-8 string
        /// - 4 bytes: ChunkIndex
        /// - 4 bytes: TotalChunks
        /// - 8 bytes: Offset (long)
        /// - 4 bytes: Raw Data Length (D)
        /// - D bytes: Raw Binary Chunk Data
        /// </summary>
        public static byte[] PackFileChunk(FileChunk chunk)
        {
            if (chunk == null) throw new ArgumentNullException(nameof(chunk));

            byte[] idBytes = Encoding.UTF8.GetBytes(chunk.TransferId ?? string.Empty);
            byte[] rawData = chunk.Data ?? Array.Empty<byte>();

            int totalSize = 4 + idBytes.Length + 4 + 4 + 8 + 4 + rawData.Length;
            byte[] buffer = new byte[totalSize];

            using (var ms = new MemoryStream(buffer))
            using (var writer = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                writer.Write(idBytes.Length);
                writer.Write(idBytes);
                writer.Write(chunk.ChunkIndex);
                writer.Write(chunk.TotalChunks);
                writer.Write(chunk.Offset);
                writer.Write(rawData.Length);
                writer.Write(rawData);
            }

            return buffer;
        }

        /// <summary>
        /// Giải mã nhị phân payload thành FileChunk.
        /// </summary>
        public static FileChunk UnpackFileChunk(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
                throw new ArgumentException("Payload không thể rỗng khi giải mã FileChunk.", nameof(payload));

            using (var ms = new MemoryStream(payload))
            using (var reader = new BinaryReader(ms, Encoding.UTF8, true))
            {
                int idLen = reader.ReadInt32();
                byte[] idBytes = reader.ReadBytes(idLen);
                string transferId = Encoding.UTF8.GetString(idBytes);

                int chunkIndex = reader.ReadInt32();
                int totalChunks = reader.ReadInt32();
                long offset = reader.ReadInt64();
                int dataLen = reader.ReadInt32();
                byte[] data = reader.ReadBytes(dataLen);

                return new FileChunk(transferId, chunkIndex, totalChunks, offset, data);
            }
        }

        #endregion
    }
}
