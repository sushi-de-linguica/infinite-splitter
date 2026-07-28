using System;
using System.Collections.Generic;

namespace InfiniteSplitter.Memory
{
    /// <summary>
    /// Walks a loaded module's PE export directory to get name -> absolute address.
    ///
    /// Read from the loaded image through <see cref="IMemory"/>, not from the file on
    /// disk: it works even if the two differ, and it lets the parser be tested against
    /// a synthetic PE in a byte array.
    /// </summary>
    public static class PeExports
    {
        public static Result<Dictionary<string, long>> Parse(IMemory memory, long moduleBase)
        {
            try
            {
                if (memory.ReadUInt16(moduleBase) != 0x5A4D)   // "MZ"
                    return Result<Dictionary<string, long>>.Err(RefreshErrorReason.LayoutMismatch, "no MZ header");

                int peOffset = memory.ReadInt32(moduleBase + 0x3C);
                long pe = moduleBase + peOffset;
                if (memory.ReadUInt32(pe) != 0x00004550)       // "PE\0\0"
                    return Result<Dictionary<string, long>>.Err(RefreshErrorReason.LayoutMismatch, "no PE signature");

                // COFF header is 20 bytes; the optional header follows.
                long optional = pe + 24;
                ushort magic = memory.ReadUInt16(optional);
                if (magic != 0x20B)                            // PE32+
                    return Result<Dictionary<string, long>>.Err(RefreshErrorReason.TargetNot64Bit, "module is not PE32+");

                // Data directories start at optional+112 for PE32+; export is #0.
                int exportRva = memory.ReadInt32(optional + 112);
                int exportSize = memory.ReadInt32(optional + 116);
                if (exportRva == 0 || exportSize == 0)
                    return Result<Dictionary<string, long>>.Err(RefreshErrorReason.LayoutMismatch, "module has no export directory");

                long dir = moduleBase + exportRva;
                int numberOfNames = memory.ReadInt32(dir + 0x18);
                int addressTableRva = memory.ReadInt32(dir + 0x1C);
                int namePointerRva = memory.ReadInt32(dir + 0x20);
                int ordinalTableRva = memory.ReadInt32(dir + 0x24);

                if (numberOfNames <= 0 || numberOfNames > 200000)
                    return Result<Dictionary<string, long>>.Err(RefreshErrorReason.LayoutMismatch,
                        "implausible export count " + numberOfNames);

                var exports = new Dictionary<string, long>(numberOfNames, StringComparer.Ordinal);

                // One bulk read each rather than numberOfNames*3 syscalls: mono has
                // ~1200 exports and this runs at every attach.
                var nameRvas = memory.ReadBytes(moduleBase + namePointerRva, numberOfNames * 4);
                var ordinals = memory.ReadBytes(moduleBase + ordinalTableRva, numberOfNames * 2);

                for (int i = 0; i < numberOfNames; i++)
                {
                    int nameRva = BitConverter.ToInt32(nameRvas, i * 4);
                    if (nameRva == 0) continue;

                    string name = memory.ReadAsciiString(moduleBase + nameRva, 512);
                    if (name.Length == 0) continue;

                    ushort ordinal = BitConverter.ToUInt16(ordinals, i * 2);
                    int functionRva = memory.ReadInt32(moduleBase + addressTableRva + ordinal * 4);
                    if (functionRva == 0) continue;

                    // A forwarded export points inside the export directory itself, at
                    // a string rather than at code.
                    if (functionRva >= exportRva && functionRva < exportRva + exportSize) continue;

                    exports[name] = moduleBase + functionRva;
                }

                return Result<Dictionary<string, long>>.Ok(exports);
            }
            catch (Exception e)
            {
                return Result<Dictionary<string, long>>.Err(RefreshErrorReason.UnknownException, "parsing exports", e);
            }
        }
    }
}
