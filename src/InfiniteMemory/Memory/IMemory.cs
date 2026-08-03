namespace InfiniteSplitter.Memory
{
    /// <summary>
    /// Somewhere bytes can be read from. The point of the interface is that a
    /// <see cref="ByteArrayMemory"/> can stand in for a live process, which is what
    /// lets the Mono resolver be tested on Linux with no game running.
    /// </summary>
    public interface IMemory
    {
        /// <summary>
        /// Reads <paramref name="length"/> bytes. Never throws and never returns null:
        /// an unreadable address yields a zero-filled buffer. A null link in a pointer
        /// chain is an ordinary state, so failure has to be a value; callers guard
        /// themselves by validating what they read.
        /// </summary>
        byte[] ReadBytes(long address, int length);

        /// <summary>
        /// The same read into a caller-owned buffer, so a per-frame path does not have
        /// to allocate. Returns false when nothing could be read.
        ///
        /// **A failed read zeroes the buffer**, exactly as <see cref="ReadBytes"/>
        /// returns zeros. Leaving the previous contents in place would let a reused
        /// buffer serve last frame's state as if it were current — a stale flag reads
        /// as a live one, and the split fires on nothing.
        /// </summary>
        bool TryReadInto(long address, byte[] buffer, int length);

        /// <summary>
        /// Present for symmetry with <see cref="ByteArrayMemory"/>. The process handle
        /// is opened without VM_WRITE, so this throws against a real process.
        /// </summary>
        void WriteBytes(long address, byte[] bytes);
    }
}
