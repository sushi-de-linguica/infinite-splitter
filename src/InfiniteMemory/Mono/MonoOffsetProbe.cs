using System;
using System.Collections.Generic;
using InfiniteSplitter.Memory;

namespace InfiniteSplitter.Mono
{
    /// <summary>
    /// Re-derives Mono struct offsets from the loaded runtime instead of trusting
    /// the constants in <see cref="MonoOffsets"/>.
    ///
    /// Mono exports a pile of one-instruction accessors — mono_class_get_image is
    /// literally `mov rax,[rcx+0x40]; ret` — so decoding the displacement out of the
    /// body reads each offset off the shipped DLL at every attach. Only accessors
    /// that are a single instruction can be recovered this way; the rest stay
    /// constants.
    /// </summary>
    public sealed class MonoOffsetProbe
    {
        public int DomainId = MonoOffsets.DomainId;
        public int AssemblyImage = MonoOffsets.AssemblyImage;
        public int AssemblyName = MonoOffsets.AssemblyName;
        public int ImageFileName = MonoOffsets.ImageFileName;
        public int ImageAssemblyName = MonoOffsets.ImageAssemblyName;
        public int ImageAssembly = MonoOffsets.ImageAssembly;
        public int ClassImage = MonoOffsets.ClassImage;
        public int ClassNestedIn = MonoOffsets.ClassNestedIn;
        public int ClassTypeToken = MonoOffsets.ClassTypeToken;
        public int FieldName = MonoOffsets.FieldName;
        public int FieldParent = MonoOffsets.FieldParent;
        public int TypeAttrs = MonoOffsets.TypeAttrs;
        public int TypeType = MonoOffsets.TypeType;

        /// <summary>Offsets that came back different from the compiled-in constant.
        /// Surfaced in the Debug tab: the early warning that the game has
        /// been updated under us.</summary>
        public readonly List<string> Overrides = new List<string>();

        /// <summary>Accessors we could not decode; the constant was kept.</summary>
        public readonly List<string> Undecoded = new List<string>();

        public void Decode(IMemory memory, IReadOnlyDictionary<string, long> exports)
        {
            Overrides.Clear();
            Undecoded.Clear();

            Try(memory, exports, "mono_domain_get_id", MonoOffsets.DomainId, v => DomainId = v);
            Try(memory, exports, "mono_assembly_get_image_internal", MonoOffsets.AssemblyImage, v => AssemblyImage = v);
            Try(memory, exports, "mono_assembly_get_name_internal", MonoOffsets.AssemblyName, v => AssemblyName = v);
            Try(memory, exports, "mono_image_get_filename", MonoOffsets.ImageFileName, v => ImageFileName = v);
            Try(memory, exports, "mono_image_get_name", MonoOffsets.ImageAssemblyName, v => ImageAssemblyName = v);
            Try(memory, exports, "mono_image_get_assembly", MonoOffsets.ImageAssembly, v => ImageAssembly = v);
            Try(memory, exports, "mono_class_get_image", MonoOffsets.ClassImage, v => ClassImage = v);
            Try(memory, exports, "mono_class_get_nesting_type", MonoOffsets.ClassNestedIn, v => ClassNestedIn = v);
            Try(memory, exports, "mono_class_get_type_token", MonoOffsets.ClassTypeToken, v => ClassTypeToken = v);
            Try(memory, exports, "mono_field_get_name", MonoOffsets.FieldName, v => FieldName = v);
            Try(memory, exports, "mono_field_get_parent", MonoOffsets.FieldParent, v => FieldParent = v);
            Try(memory, exports, "mono_type_get_attrs", MonoOffsets.TypeAttrs, v => TypeAttrs = v);
            Try(memory, exports, "mono_type_get_type", MonoOffsets.TypeType, v => TypeType = v);
        }

        private void Try(IMemory memory, IReadOnlyDictionary<string, long> exports,
                         string export, int compiledIn, Action<int> assign)
        {
            if (!exports.TryGetValue(export, out long address)) { Undecoded.Add(export + " (not exported)"); return; }

            var body = memory.ReadBytes(address, 12);
            if (!TryDecodeAccessor(body, out int decoded)) { Undecoded.Add(export); return; }

            assign(decoded);
            if (decoded != compiledIn)
                Overrides.Add($"{export}: 0x{compiledIn:X} -> 0x{decoded:X}");
        }

        /// <summary>
        /// Matches the shapes Mono's trivial accessors compile to under MSVC x64,
        /// where the single argument arrives in rcx:
        ///
        ///   48 8b 41 &lt;d8&gt;   c3      mov rax,[rcx+disp8]    (pointer)
        ///   48 8b 81 &lt;d32&gt;  c3      mov rax,[rcx+disp32]
        ///   8b 41 &lt;d8&gt;      c3      mov eax,[rcx+disp8]    (int32)
        ///   8b 81 &lt;d32&gt;     c3      mov eax,[rcx+disp32]
        ///   48 8d 41 &lt;d8&gt;   c3      lea rax,[rcx+disp8]    (embedded struct)
        ///   0f b6 41 &lt;d8&gt;   c3      movzx eax,byte [rcx+disp8]
        ///   0f b7 41 &lt;d8&gt;   c3      movzx eax,word [rcx+disp8]
        ///   0f be 41 &lt;d8&gt;   c3      movsx eax,byte [rcx+disp8]   (mono_type_get_type)
        /// </summary>
        public static bool TryDecodeAccessor(byte[] body, out int offset)
        {
            offset = 0;
            if (body == null || body.Length < 4) return false;   // 8b 41 d8 c3 is the shortest shape

            // 48 8b 41 d8 c3 / 48 8d 41 d8 c3
            if (body.Length >= 5 && body[0] == 0x48 && (body[1] == 0x8B || body[1] == 0x8D) && body[2] == 0x41 && body[4] == 0xC3)
            {
                offset = body[3];
                return true;
            }
            // 48 8b 81 d32 c3 / 48 8d 81 d32 c3
            if (body.Length >= 8 && body[0] == 0x48 && (body[1] == 0x8B || body[1] == 0x8D) &&
                body[2] == 0x81 && body[7] == 0xC3)
            {
                offset = BitConverter.ToInt32(body, 3);
                return true;
            }
            // 8b 41 d8 c3
            if (body[0] == 0x8B && body[1] == 0x41 && body[3] == 0xC3)
            {
                offset = body[2];
                return true;
            }
            // 8b 81 d32 c3
            if (body.Length >= 7 && body[0] == 0x8B && body[1] == 0x81 && body[6] == 0xC3)
            {
                offset = BitConverter.ToInt32(body, 2);
                return true;
            }
            // 0f b6/b7/be/bf 41 d8 c3  (movzx/movsx from byte or word)
            if (body.Length >= 5 && body[0] == 0x0F &&
                (body[1] == 0xB6 || body[1] == 0xB7 || body[1] == 0xBE || body[1] == 0xBF) &&
                body[2] == 0x41 && body[4] == 0xC3)
            {
                offset = body[3];
                return true;
            }
            return false;
        }

        /// <summary>
        /// Decodes `mov rax,[rip+disp32]; ret` — how mono_get_root_domain returns the
        /// address of its static root-domain slot. The export table hands us the
        /// function's location, so no signature scanning is needed.
        /// </summary>
        public static bool TryDecodeRipRelative(byte[] body, long bodyAddress, out long target)
        {
            target = 0;
            if (body == null || body.Length < 7) return false;
            if (body[0] != 0x48 || body[1] != 0x8B || body[2] != 0x05) return false;

            int displacement = BitConverter.ToInt32(body, 3);
            target = bodyAddress + 7 + displacement;   // rip points past the 7-byte instruction
            return true;
        }
    }
}
