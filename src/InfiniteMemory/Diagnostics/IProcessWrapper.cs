using System;
using System.Collections.Generic;
using InfiniteSplitter.Memory;

namespace InfiniteSplitter.Diagnostics
{
    /// <summary>
    /// The bit of System.Diagnostics.Process this project needs, behind an
    /// interface so <see cref="ProcessHook"/> can be driven by a fake in tests.
    /// </summary>
    public interface IProcessWrapper : IMemory, IDisposable
    {
        bool IsAttached { get; }
        int Id { get; }
        bool HasExited { get; }

        Result Attach(string processName);
        void Detach();

        IReadOnlyList<ModuleInfo> Modules { get; }
        ModuleInfo FindModule(string name);

        /// <summary>
        /// Every committed, readable span of the address space, in order. Used only
        /// by the offset-derivation scans; nothing on the per-tick path calls it.
        /// </summary>
        IReadOnlyList<MemoryRegion> Regions();
    }
}
