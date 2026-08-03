using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using InfiniteSplitter.Memory;
using InfiniteSplitter.Native;
using SysProcess = System.Diagnostics.Process;

namespace InfiniteSplitter.Diagnostics
{
    /// <summary>
    /// A real Windows process, opened read-only. Module enumeration goes through
    /// System.Diagnostics.Process.Modules rather than a Toolhelp snapshot: only two
    /// modules are ever needed by name, and the managed API handles the races.
    /// </summary>
    public sealed class ProcessWrapper : IProcessWrapper
    {
        private SysProcess _process;
        private IntPtr _handle = IntPtr.Zero;
        private List<ModuleInfo> _modules = new List<ModuleInfo>();

        public bool IsAttached => _process != null && _handle != IntPtr.Zero;
        public int Id => _process?.Id ?? 0;

        public bool HasExited
        {
            get
            {
                try { return _process == null || _process.HasExited; }
                catch { return true; }
            }
        }

        public IReadOnlyList<ModuleInfo> Modules => _modules;

        public Result Attach(string processName)
        {
            Detach();

            SysProcess found;
            try
            {
                found = SysProcess.GetProcesses()
                    .FirstOrDefault(p => string.Equals(p.ProcessName, processName, StringComparison.OrdinalIgnoreCase)
                                         && !SafeHasExited(p));
            }
            catch (Exception e)
            {
                return Result.Err(RefreshErrorReason.UnknownException, "enumerating processes", e);
            }

            if (found == null) return Result.Err(RefreshErrorReason.ProcessNotRunning, processName);

            var handle = NativeMethods.OpenProcess(
                NativeMethods.ProcessAccess.QueryInformation | NativeMethods.ProcessAccess.VmRead,
                false, found.Id);

            if (handle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                var reason = error == NativeMethods.ErrorAccessDenied
                    ? RefreshErrorReason.AccessDenied
                    : RefreshErrorReason.UnknownException;
                return Result.Err(reason, "OpenProcess failed with " + error, new Win32Exception(error));
            }

            _process = found;
            _handle = handle;

            // A 32-bit game would mean 4-byte pointers everywhere; x64 only.
            if (NativeMethods.IsWow64Process(handle, out bool wow64) && wow64)
            {
                Detach();
                return Result.Err(RefreshErrorReason.TargetNot64Bit, processName);
            }

            var modules = RefreshModules();
            if (modules.IsErr) { Detach(); return modules; }

            return Result.Ok();
        }

        /// <summary>Re-reads the module list; worth repeating while the game is still
        /// starting, as mono-2.0-bdwgc.dll is not loaded on the first frames.</summary>
        public Result RefreshModules()
        {
            if (_process == null) return Result.Err(RefreshErrorReason.ProcessNotRunning);
            try
            {
                _process.Refresh();
                var list = new List<ModuleInfo>();
                foreach (System.Diagnostics.ProcessModule module in _process.Modules)
                {
                    list.Add(new ModuleInfo
                    {
                        Name = module.ModuleName,
                        BaseAddress = module.BaseAddress.ToInt64(),
                        Size = module.ModuleMemorySize,
                    });
                }
                _modules = list;
                return Result.Ok();
            }
            catch (Win32Exception e) when (e.NativeErrorCode == NativeMethods.ErrorAccessDenied)
            {
                return Result.Err(RefreshErrorReason.AccessDenied, "enumerating modules", e);
            }
            catch (Exception e)
            {
                return Result.Err(RefreshErrorReason.UnknownException, "enumerating modules", e);
            }
        }

        public ModuleInfo FindModule(string name)
        {
            foreach (var module in _modules)
                if (string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase))
                    return module;
            return null;
        }

        /// <summary>Walks the address space with VirtualQueryEx, keeping what can
        /// actually be read. Guard pages hold nothing worth scanning.</summary>
        public IReadOnlyList<MemoryRegion> Regions()
        {
            var regions = new List<MemoryRegion>();
            if (_handle == IntPtr.Zero) return regions;

            long address = 0;
            long ceiling = 0x7FFFFFFFFFFF;      // the top of x64 user space
            int size = Marshal.SizeOf(typeof(NativeMethods.MemoryBasicInformation));

            while (address < ceiling)
            {
                if (NativeMethods.VirtualQueryEx(_handle, (IntPtr)address, out var info, (IntPtr)size) == IntPtr.Zero)
                    break;

                long regionSize = info.RegionSize.ToInt64();
                if (regionSize <= 0) break;

                bool readable = info.State == NativeMethods.MemCommit
                                && (info.Protect & NativeMethods.PageReadable) != 0
                                && (info.Protect & NativeMethods.PageGuard) == 0
                                && (info.Protect & NativeMethods.PageNoAccess) == 0;

                if (readable)
                {
                    regions.Add(new MemoryRegion
                    {
                        Start = info.BaseAddress.ToInt64(),
                        Size = regionSize,
                        Kind = info.Type == NativeMethods.MemImage ? MemoryRegionKind.Image
                             : info.Type == NativeMethods.MemMapped ? MemoryRegionKind.Mapped
                             : info.Type == NativeMethods.MemPrivate ? MemoryRegionKind.Private
                             : MemoryRegionKind.Unknown,
                    });
                }

                address = info.BaseAddress.ToInt64() + regionSize;
            }
            return regions;
        }

        public byte[] ReadBytes(long address, int length)
        {
            var buffer = new byte[length];
            TryReadInto(address, buffer, length);
            return buffer;
        }

        public bool TryReadInto(long address, byte[] buffer, int length)
        {
            if (buffer == null || length <= 0 || length > buffer.Length) return false;
            if (_handle == IntPtr.Zero) { Array.Clear(buffer, 0, length); return false; }

            int read = 0;
            if (NativeMethods.ReadProcessMemory(_handle, (IntPtr)address, buffer, length, ref read) && read == length)
                return true;

            // IMemory's contract: unreadable reads as zero. A partial read counts as
            // unreadable — the tail would be whatever the caller's buffer already held.
            Array.Clear(buffer, 0, length);
            return false;
        }

        public void WriteBytes(long address, byte[] bytes) =>
            throw new NotSupportedException("The game process is opened read-only.");

        public void Detach()
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
            _process?.Dispose();
            _process = null;
            _modules = new List<ModuleInfo>();
        }

        public void Dispose() => Detach();

        private static bool SafeHasExited(SysProcess p)
        {
            try { return p.HasExited; } catch { return true; }
        }
    }
}
