using System;
using System.Runtime.InteropServices;

namespace InfiniteSplitter.Native
{
    /// <summary>
    /// The entire Win32 surface this project uses.
    ///
    /// What is absent is the point: no VirtualAllocEx, no CreateRemoteThread, no
    /// WriteProcessMemory. The handle is opened read-only, so nothing here can modify
    /// the game — the premise of the whole component.
    /// </summary>
    internal static class NativeMethods
    {
        [Flags]
        internal enum ProcessAccess : uint
        {
            QueryInformation = 0x0400,
            VmRead = 0x0010,
        }

        /// <summary>x64 layout, 48 bytes. The two __alignment holes are real.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct MemoryBasicInformation
        {
            internal IntPtr BaseAddress;
            internal IntPtr AllocationBase;
            internal uint AllocationProtect;
            internal uint Alignment1;
            internal IntPtr RegionSize;
            internal uint State;
            internal uint Protect;
            internal uint Type;
            internal uint Alignment2;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr VirtualQueryEx(
            IntPtr process, IntPtr address, out MemoryBasicInformation buffer, IntPtr length);

        internal const uint MemCommit = 0x1000;

        internal const uint MemImage = 0x1000000;
        internal const uint MemMapped = 0x40000;
        internal const uint MemPrivate = 0x20000;

        internal const uint PageNoAccess = 0x01;
        internal const uint PageGuard = 0x100;

        /// <summary>The protections a ReadProcessMemory can actually come back from.</summary>
        internal const uint PageReadable =
            0x02 |   // PAGE_READONLY
            0x04 |   // PAGE_READWRITE
            0x08 |   // PAGE_WRITECOPY
            0x20 |   // PAGE_EXECUTE_READ
            0x40 |   // PAGE_EXECUTE_READWRITE
            0x80;    // PAGE_EXECUTE_WRITECOPY

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(ProcessAccess desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadProcessMemory(
            IntPtr process, IntPtr baseAddress, byte[] buffer, int size, ref int bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWow64Process(IntPtr process, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

        internal const int ErrorAccessDenied = 5;
    }
}
