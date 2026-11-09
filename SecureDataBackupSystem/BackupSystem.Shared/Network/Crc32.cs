using System;

namespace BackupSystem.Shared.Network
{
    /// <summary>
    /// Thuật toán tính mã kiểm tra tính toàn vẹn CRC-32 (IEEE 802.3) tốc độ cao dựa trên bảng tra (Lookup Table).
    /// </summary>
    public static class Crc32
    {
        private const uint Polynomial = 0xEDB88320u;
        private static readonly uint[] Table = new uint[256];

        static Crc32()
        {
            for (uint i = 0; i < 256; i++)
            {
                uint entry = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((entry & 1) == 1)
                        entry = (entry >> 1) ^ Polynomial;
                    else
                        entry >>= 1;
                }
                Table[i] = entry;
            }
        }

        /// <summary>
        /// Tính giá trị CRC-32 cho mảng byte dữ liệu.
        /// </summary>
        public static uint Compute(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
                return 0;

            return Compute(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Tính giá trị CRC-32 cho đoạn dữ liệu cụ thể trong buffer.
        /// </summary>
        public static uint Compute(byte[] buffer, int offset, int count)
        {
            if (buffer == null || count <= 0)
                return 0;

            uint crc = 0xFFFFFFFFu;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                byte index = (byte)((crc & 0xFF) ^ buffer[i]);
                crc = (crc >> 8) ^ Table[index];
            }

            return ~crc;
        }
    }
}

