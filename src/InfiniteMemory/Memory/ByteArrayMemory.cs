using System;
using System.Collections.Generic;

namespace InfiniteSplitter.Memory
{
    /// <summary>
    /// An <see cref="IMemory"/> over plain byte arrays placed at chosen addresses:
    /// the test double the Mono resolver is developed against (see MonoHeapBuilder),
    /// and the backing store for a recorded snapshot.
    ///
    /// Reads outside any placed region come back as zeros, exactly as an unreadable
    /// address does against a real process — the failure mode under test has to match
    /// the one in production.
    /// </summary>
    public sealed class ByteArrayMemory : IMemory
    {
        private readonly List<Region> _regions = new List<Region>();

        private struct Region
        {
            public long Start;
            public byte[] Data;
            public long End => Start + Data.Length;
        }

        public ByteArrayMemory() { }

        public ByteArrayMemory(long address, byte[] data) { Place(address, data); }

        public ByteArrayMemory Place(long address, byte[] data)
        {
            _regions.Add(new Region { Start = address, Data = data });
            return this;
        }

        /// <summary>Reserves a writable region and returns it, for fixture builders.</summary>
        public byte[] Allocate(long address, int length)
        {
            var data = new byte[length];
            Place(address, data);
            return data;
        }

        public byte[] ReadBytes(long address, int length)
        {
            var result = new byte[length];
            TryReadInto(address, result, length);
            return result;
        }

        public bool TryReadInto(long address, byte[] buffer, int length)
        {
            if (buffer == null || length <= 0 || length > buffer.Length) return false;

            Array.Clear(buffer, 0, length);

            bool any = false;
            foreach (var region in _regions)
            {
                if (address >= region.End || address + length <= region.Start) continue;

                long from = Math.Max(address, region.Start);
                long to = Math.Min(address + length, region.End);
                Array.Copy(region.Data, from - region.Start, buffer, from - address, to - from);
                any = true;
            }
            return any;
        }

        public void WriteBytes(long address, byte[] bytes)
        {
            foreach (var region in _regions)
            {
                if (address >= region.End || address + bytes.Length <= region.Start) continue;

                long from = Math.Max(address, region.Start);
                long to = Math.Min(address + bytes.Length, region.End);
                Array.Copy(bytes, from - address, region.Data, from - region.Start, to - from);
            }
        }
    }
}
