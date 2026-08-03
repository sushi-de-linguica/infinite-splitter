namespace InfiniteSplitter.Mono
{
    /// <summary>One resolved MonoClassField: where it sits and what it holds.</summary>
    public sealed class MonoFieldRef
    {
        public string Name;

        /// <summary>
        /// For an instance field, the byte offset from the object pointer — Mono's
        /// instance offsets already include the 16-byte MonoObject header on x64. For
        /// a static field it is the offset into the class's static storage instead.
        /// Both live in this one member, so always check <see cref="IsStatic"/>.
        /// </summary>
        public int Offset;

        public long TypePointer;
        public long Address;

        public MonoTypeKind Kind;

        public bool IsStatic;

        public override string ToString() =>
            $"{Kind.Display()} {Name} @ {(IsStatic ? "static+" : "+")}0x{Offset:X}";
    }
}
