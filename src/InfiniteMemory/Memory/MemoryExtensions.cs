using System;
using System.Text;

namespace InfiniteSplitter.Memory
{
    /// <summary>
    /// Typed reads over <see cref="IMemory"/>. All of them inherit ReadBytes'
    /// contract: an unreadable address reads as zero, never as an exception.
    /// </summary>
    public static class MemoryExtensions
    {
        public static byte ReadByte(this IMemory m, long address) => m.ReadBytes(address, 1)[0];

        public static bool ReadBool(this IMemory m, long address) => m.ReadBytes(address, 1)[0] != 0;

        public static short ReadInt16(this IMemory m, long address) => BitConverter.ToInt16(m.ReadBytes(address, 2), 0);

        public static ushort ReadUInt16(this IMemory m, long address) => BitConverter.ToUInt16(m.ReadBytes(address, 2), 0);

        public static int ReadInt32(this IMemory m, long address) => BitConverter.ToInt32(m.ReadBytes(address, 4), 0);

        public static uint ReadUInt32(this IMemory m, long address) => BitConverter.ToUInt32(m.ReadBytes(address, 4), 0);

        public static long ReadInt64(this IMemory m, long address) => BitConverter.ToInt64(m.ReadBytes(address, 8), 0);

        public static float ReadSingle(this IMemory m, long address) => BitConverter.ToSingle(m.ReadBytes(address, 4), 0);

        public static double ReadDouble(this IMemory m, long address) => BitConverter.ToDouble(m.ReadBytes(address, 8), 0);

        /// <summary>A pointer. x64 only — the component refuses a 32-bit host outright.</summary>
        public static long ReadPointer(this IMemory m, long address) => BitConverter.ToInt64(m.ReadBytes(address, 8), 0);

        /// <summary>
        /// A NUL-terminated ASCII string, as Mono stores class and field names.
        ///
        /// Read in small chunks: a large read straddling an unmapped page comes back
        /// all zeros, which would turn a perfectly good name into an empty string.
        /// </summary>
        public static string ReadAsciiString(this IMemory m, long address, int maxLength = 256)
        {
            if (address == 0) return "";

            var sb = new StringBuilder();
            const int chunk = 32;
            for (int read = 0; read < maxLength; read += chunk)
            {
                var bytes = m.ReadBytes(address + read, Math.Min(chunk, maxLength - read));
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (bytes[i] == 0) return sb.ToString();
                    sb.Append((char)bytes[i]);
                }
            }
            return sb.ToString();
        }

        /// <summary>True if every character is printable ASCII and there is at least
        /// one. The cheapest "did I just follow a garbage pointer" check there is.</summary>
        public static bool LooksLikeIdentifier(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var c in text)
                if (c < 0x20 || c > 0x7E) return false;
            return true;
        }

        public static string ToHex(this long value) => "0x" + value.ToString("X");
    }
}
