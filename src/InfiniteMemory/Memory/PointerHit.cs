namespace InfiniteSplitter.Memory
{
    /// <summary>One qword that points into a target, and how far into it.</summary>
    public sealed class PointerHit
    {
        /// <summary>Where the pointer itself lives — the next level's target.</summary>
        public long At;

        public long Value;
        public long Target;

        /// <summary>Target - Value: how far into the pointed-at block the target sits.</summary>
        public int Offset;

        public MemoryRegion Region;

        public override string ToString() =>
            "0x" + At.ToString("X") + " -> 0x" + Value.ToString("X") + "+0x" + Offset.ToString("X");
    }
}
