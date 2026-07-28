using System;
using System.Collections.Generic;
using System.Text;

namespace InfiniteSplitter.Memory
{
    /// <summary>
    /// Finds byte patterns, and pointers to what it found, across a process's
    /// committed memory. It exists for one job: rebuilding the scene-name pointer chain
    /// when a Unity update invalidates the compiled-in one.
    ///
    /// A maintenance tool, not a runtime path. Nothing on the per-tick path scans; a
    /// scan's output is a fixed offset chain that gets compiled in.
    /// </summary>
    public sealed class PointerScanner
    {
        private readonly IMemory _memory;
        private readonly IReadOnlyList<MemoryRegion> _regions;

        /// <summary>Read granularity. VirtualQueryEx already splits regions by state
        /// and protection, so a chunk this size never straddles an unreadable page.</summary>
        private const int ChunkSize = 1 << 20;

        public PointerScanner(IMemory memory, IReadOnlyList<MemoryRegion> regions)
        {
            _memory = memory;
            _regions = regions;
        }

        /// <summary>Total bytes the scanner will walk, for progress reporting.</summary>
        public long TotalBytes
        {
            get
            {
                long total = 0;
                foreach (var region in _regions) total += region.Size;
                return total;
            }
        }

        /// <summary>
        /// Every address whose bytes start with <paramref name="needle"/>. Hits need
        /// not be aligned: a short Unity <c>core::string</c> lives inline in its owning
        /// struct rather than behind a pointer.
        /// </summary>
        public List<long> FindBytes(byte[] needle, Func<MemoryRegion, bool> where = null, int limit = 4096)
        {
            var hits = new List<long>();
            if (needle == null || needle.Length == 0) return hits;

            foreach (var region in _regions)
            {
                if (where != null && !where(region)) continue;

                // Overlap by needle-1, so a match spanning a chunk boundary is found.
                long overlap = needle.Length - 1;
                for (long offset = 0; offset < region.Size; offset += ChunkSize - overlap)
                {
                    int length = (int)Math.Min(ChunkSize, region.Size - offset);
                    if (length < needle.Length) break;

                    var buffer = _memory.ReadBytes(region.Start + offset, length);
                    int at = 0;
                    while (true)
                    {
                        at = IndexOf(buffer, needle, at);
                        if (at < 0) break;
                        hits.Add(region.Start + offset + at);
                        if (hits.Count >= limit) return hits;
                        at++;
                    }
                }
            }
            return hits;
        }

        /// <summary>
        /// Every 8-aligned qword that points at one of <paramref name="targets"/> — or
        /// at up to <paramref name="maxOffset"/> bytes before one, since a pointer
        /// lands on the head of a struct and the interesting field sits inside it.
        /// </summary>
        public List<PointerHit> FindPointersTo(
            ICollection<long> targets, int maxOffset = 0x200,
            Func<MemoryRegion, bool> where = null, int limit = 8192)
        {
            var hits = new List<PointerHit>();
            if (targets == null || targets.Count == 0) return hits;

            // value -> the target it reaches. A 0x200-byte window costs 65 entries per
            // target, which stays small for the handful a scene name produces.
            var wanted = new Dictionary<long, long>();
            foreach (var target in targets)
                for (long delta = 0; delta <= maxOffset; delta += 8)
                    wanted[target - delta] = target;      // last write wins; nearest target is fine

            foreach (var region in _regions)
            {
                if (where != null && !where(region)) continue;

                long first = ((region.Start + 7) & ~7L) - region.Start;
                for (long offset = first; offset + 8 <= region.Size; offset += ChunkSize)
                {
                    int length = (int)Math.Min(ChunkSize, region.Size - offset);
                    length -= length % 8;
                    if (length < 8) break;

                    var buffer = _memory.ReadBytes(region.Start + offset, length);
                    for (int i = 0; i + 8 <= length; i += 8)
                    {
                        long value = BitConverter.ToInt64(buffer, i);
                        if (value == 0 || !wanted.TryGetValue(value, out long target)) continue;

                        hits.Add(new PointerHit
                        {
                            At = region.Start + offset + i,
                            Value = value,
                            Target = target,
                            Offset = (int)(target - value),
                            Region = region,
                        });
                        if (hits.Count >= limit) return hits;
                    }
                }
            }
            return hits;
        }

        /// <summary>The region an address falls in, or null.</summary>
        public MemoryRegion RegionOf(long address)
        {
            foreach (var region in _regions)
                if (region.Contains(address)) return region;
            return null;
        }

        /// <summary>
        /// A UTF-16 needle. Unity's <c>core::string</c> is 8-bit and Mono's
        /// System.String is not, so which encoding matched tells a native hit from a
        /// managed one.
        /// </summary>
        public static byte[] Utf16(string text) => Encoding.Unicode.GetBytes(text);

        /// <summary>An 8-bit needle, NUL included so a prefix does not match.</summary>
        public static byte[] AsciiZ(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            var result = new byte[bytes.Length + 1];
            Array.Copy(bytes, result, bytes.Length);
            return result;
        }

        private static int IndexOf(byte[] haystack, byte[] needle, int from)
        {
            byte first = needle[0];
            int last = haystack.Length - needle.Length;
            for (int i = from; i <= last; i++)
            {
                if (haystack[i] != first) continue;

                int j = 1;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }
    }
}
