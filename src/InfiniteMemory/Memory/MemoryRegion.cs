namespace InfiniteSplitter.Memory
{
    /// <summary>
    /// One committed, readable span of a process's address space. Deliberately not a
    /// Win32 type, so a scan can be driven against a <see cref="ByteArrayMemory"/>
    /// with no process anywhere.
    /// </summary>
    public sealed class MemoryRegion
    {
        public long Start;
        public long Size;

        /// <summary>Which kind of region a hit lands in is most of what a pointer scan
        /// is trying to find out, so it survives into the results.</summary>
        public MemoryRegionKind Kind;

        public long End => Start + Size;

        public bool Contains(long address) => address >= Start && address < End;

        public override string ToString() =>
            "0x" + Start.ToString("X") + "-0x" + End.ToString("X") + " " + Kind + " (" + Size + " bytes)";
    }

    public enum MemoryRegionKind
    {
        Unknown = 0,

        /// <summary>A mapped module — a DLL or the exe. Statics live here.</summary>
        Image,

        /// <summary>A mapped file that is not a module.</summary>
        Mapped,

        /// <summary>Ordinary committed heap.</summary>
        Private,
    }
}
