namespace InfiniteSplitter.Diagnostics
{
    /// <summary>One loaded module: where it sits and how far it runs. A pointer chain
    /// is stored relative to this base, which is what makes it survive a restart.</summary>
    public sealed class ModuleInfo
    {
        public string Name;
        public long BaseAddress;
        public int Size;

        public override string ToString() => Name + " @ 0x" + BaseAddress.ToString("X") + " (" + Size + " bytes)";
    }
}
