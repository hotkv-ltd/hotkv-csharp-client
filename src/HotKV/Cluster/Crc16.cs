// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Text;

namespace HotKV.Cluster
{
    /// <summary>CRC16/XMODEM (polynomial 0x1021, initial value 0, no reflect), the checksum HotKV cluster hash slots are derived from.</summary>
    public static class Crc16
    {
        /// <summary>Computes the CRC16/XMODEM checksum of a UTF-8 encoded string.</summary>
        public static ushort Compute(string data) => Compute(Encoding.UTF8.GetBytes(data));

        /// <summary>Computes the CRC16/XMODEM checksum of the given bytes.</summary>
        public static ushort Compute(byte[] data) => Compute(data, 0, data.Length);

        /// <summary>Computes the CRC16/XMODEM checksum of a byte range.</summary>
        public static ushort Compute(byte[] data, int offset, int count)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            ushort crc = 0;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
                }
            }
            return crc;
        }
    }

    /// <summary>Maps a key to one of the cluster's 16384 hash slots, honoring "{hash tag}" pinning.</summary>
    public static class ClusterKeySlot
    {
        /// <summary>Total number of hash slots in a HotKV cluster.</summary>
        public const int SlotCount = 16384;

        /// <summary>Computes the hash slot for a UTF-8 encoded key.</summary>
        public static int Compute(string key) => Compute(Encoding.UTF8.GetBytes(key));

        /// <summary>
        /// Computes the hash slot for a key, given as raw bytes for full
        /// binary safety. If the key contains a non-empty "{...}" hash tag,
        /// only the bytes inside the first such tag are hashed (so
        /// "foo{bar}" and "bar" land in the same slot); otherwise the whole
        /// key is hashed.
        /// </summary>
        public static int Compute(byte[] keyBytes)
        {
            if (keyBytes is null)
            {
                throw new ArgumentNullException(nameof(keyBytes));
            }
            int start = Array.IndexOf(keyBytes, (byte)'{');
            if (start >= 0)
            {
                int end = -1;
                for (int i = start + 1; i < keyBytes.Length; i++)
                {
                    if (keyBytes[i] == (byte)'}')
                    {
                        end = i;
                        break;
                    }
                }
                if (end > start + 1)
                {
                    return Crc16.Compute(keyBytes, start + 1, end - start - 1) % SlotCount;
                }
            }
            return Crc16.Compute(keyBytes) % SlotCount;
        }
    }
}
