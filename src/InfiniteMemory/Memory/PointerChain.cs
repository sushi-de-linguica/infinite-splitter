using System;
using System.Collections.Generic;
using System.Linq;

namespace InfiniteSplitter.Memory
{
    /// <summary>
    /// A module-relative pointer path: <c>Module+0xROOT -> +0xA -> +0xB</c>. The only
    /// form of a native offset that survives a restart — ASLR moves the module and the
    /// allocator moves the heap, so an absolute address from a scan is worthless the
    /// moment the game closes.
    ///
    /// A zero link ends the walk with a null result rather than an exception: a chain
    /// that stops resolving is a state (the scene is mid-load), not a fault.
    /// </summary>
    public sealed class PointerChain
    {
        public string Module;

        /// <summary>Offset of the root pointer from the module's base address.</summary>
        public long Root;

        /// <summary>Applied after each dereference, in order.</summary>
        public readonly List<int> Offsets = new List<int>();

        public PointerChain() { }

        public PointerChain(string module, long root, IEnumerable<int> offsets)
        {
            Module = module;
            Root = root;
            if (offsets != null) Offsets.AddRange(offsets);
        }

        /// <summary>
        /// Walks the chain and returns the address it lands on, or 0 if any link is
        /// null. The final offset is applied *after* the last dereference, so the
        /// result is an address to read from, not a pointer to one.
        /// </summary>
        public long Resolve(IMemory memory, long moduleBase)
        {
            if (moduleBase == 0) return 0;

            long address = moduleBase + Root;
            foreach (var offset in Offsets)
            {
                long value = memory.ReadPointer(address);
                if (value == 0) return 0;
                address = value + offset;
            }
            return address;
        }

        /// <summary>
        /// Resolves and reads the 8-bit string at the end. Unity's <c>core::string</c>
        /// is plain bytes, unlike Mono's UTF-16 System.String.
        /// </summary>
        public string ResolveAscii(IMemory memory, long moduleBase, int maxLength = 256)
        {
            long address = Resolve(memory, moduleBase);
            return address == 0 ? "" : memory.ReadAsciiString(address, maxLength);
        }

        public override string ToString() =>
            Module + "+0x" + Root.ToString("X") + string.Concat(Offsets.Select(o => " -> +0x" + o.ToString("X")));

        /// <summary>Round-trips <see cref="ToString"/>, so a chain can live in a file
        /// or be diffed between two scans as plain text.</summary>
        public static PointerChain Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var parts = text.Split(new[] { "->" }, StringSplitOptions.None)
                            .Select(p => p.Trim()).ToArray();

            int plus = parts[0].LastIndexOf('+');
            if (plus <= 0) return null;

            var chain = new PointerChain { Module = parts[0].Substring(0, plus).Trim() };
            if (!TryHex(parts[0].Substring(plus + 1), out long root)) return null;
            chain.Root = root;

            for (int i = 1; i < parts.Length; i++)
            {
                if (!TryHex(parts[i].TrimStart('+'), out long offset)) return null;
                chain.Offsets.Add((int)offset);
            }
            return chain;
        }

        private static bool TryHex(string text, out long value)
        {
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            return long.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                                 System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}
