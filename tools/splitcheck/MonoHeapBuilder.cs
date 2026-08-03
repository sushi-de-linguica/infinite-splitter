using System;
using System.Collections.Generic;
using System.Text;
using InfiniteSplitter.Memory;
using InfiniteSplitter.Mono;

namespace InfiniteSplitter.Tools
{
    /// <summary>
    /// Builds a fake Mono process in a byte array: a PE module with a real export
    /// directory and real instruction bytes, a MonoDomain, an assembly list, an image,
    /// a chained class cache, field arrays, a vtable and static storage.
    ///
    /// It pins every offset in <see cref="MonoOffsets"/> and every self-validation rule
    /// in <see cref="MonoRuntime"/> without the game, and the knobs below corrupt it on
    /// purpose so the failure paths are tested too. See docs/testing.md.
    /// </summary>
    public sealed class MonoHeapBuilder
    {
        public const long ModuleBase = 0x180000000;
        public const long Domain = 0x200000000;
        public const long AssemblyNode = 0x200001000;
        public const long Assembly = 0x200002000;
        public const long Image = 0x200003000;
        public const long BucketTable = 0x200004000;
        public const long ClassGameManager = 0x200005000;
        public const long ClassMenu = 0x200005400;
        /// <summary>Filler classes, one per bucket, so the bucket array looks real to
        /// the class-cache search, which insists on several valid entries.</summary>
        public const long ClassFillerBase = 0x200010000;   // clear of FieldArray/MenuFieldArray
        public const int FillerCount = 6;
        public const long FieldArray = 0x200006000;
        public const long MenuFieldArray = 0x200006800;
        public const long RuntimeInfo = 0x200007000;
        public const long VTable = 0x200008000;
        public const long StaticData = 0x200009000;
        public const long GameManagerObject = 0x20000A000;
        public const long Strings = 0x20000B000;
        public const long Types = 0x20000C000;

        private const int BucketCount = 8;
        private const int GameManagerVTableSize = 3;

        /// <summary>The instance field offsets the fixture writes, asserted by the tests.</summary>
        public const int HourCount1Offset = 0x18;
        public const int SecondsCount1Offset = 0x1C;
        public const int SwordOffset = 0x20;
        public const int InstanceStaticOffset = 0x08;

        private readonly ByteArrayMemory _memory = new ByteArrayMemory();
        private readonly Dictionary<string, long> _strings = new Dictionary<string, long>(StringComparer.Ordinal);
        private long _stringCursor = Strings;
        private readonly Dictionary<int, long> _types = new Dictionary<int, long>();
        private long _typeCursor = Types;

        /// <summary>Set before <see cref="Build"/> to move the class cache, so the
        /// resolver's search for it can be tested.</summary>
        public int ClassCacheOffset = MonoOffsets.ImageClassCacheTable;

        /// <summary>Set false to omit the bucket-array pointer entirely, so nothing in
        /// the image points at a class cache and the search must come up empty.</summary>
        public bool PlaceClassCachePointer = true;

        public ByteArrayMemory Build()
        {
            BuildModule();
            BuildDomain();
            BuildImage();
            BuildClasses();
            BuildStatics();
            return _memory;
        }

        private void BuildModule()
        {
            // 64 KB is enough for headers, the export directory and a few bodies.
            var image = _memory.Allocate(ModuleBase, 0x10000);

            const int peOffset = 0x80;
            const int optional = peOffset + 24;
            const int exportRva = 0x1000;

            Put16(image, 0, 0x5A4D);                    // MZ
            Put32(image, 0x3C, peOffset);
            Put32(image, peOffset, 0x00004550);         // PE\0\0
            Put16(image, optional, 0x20B);              // PE32+
            Put32(image, optional + 112, exportRva);    // data directory 0: export
            Put32(image, optional + 116, 0x200);

            // Bodies first, so the export table can point at them. Each is the real
            // instruction sequence the offset probe decodes.
            var bodies = new List<(string name, byte[] code)>
            {
                ("mono_get_root_domain", RipRelative(0x2000, 0x3000)),
                ("mono_domain_get_id", new byte[] { 0x8B, 0x81, 0x94, 0x00, 0x00, 0x00, 0xC3 }),
                ("mono_assembly_get_image_internal", new byte[] { 0x48, 0x8B, 0x41, 0x60, 0xC3 }),
                ("mono_assembly_get_name_internal", new byte[] { 0x48, 0x8D, 0x41, 0x10, 0xC3 }),
                ("mono_image_get_filename", new byte[] { 0x48, 0x8B, 0x41, 0x20, 0xC3 }),
                ("mono_image_get_name", new byte[] { 0x48, 0x8B, 0x41, 0x30, 0xC3 }),
                ("mono_image_get_assembly", new byte[] { 0x48, 0x8B, 0x81, 0xC0, 0x04, 0x00, 0x00, 0xC3 }),
                ("mono_class_get_image", new byte[] { 0x48, 0x8B, 0x41, 0x40, 0xC3 }),
                ("mono_class_get_nesting_type", new byte[] { 0x48, 0x8B, 0x41, 0x38, 0xC3 }),
                ("mono_class_get_type_token", new byte[] { 0x8B, 0x41, 0x58, 0xC3 }),
                ("mono_field_get_name", new byte[] { 0x48, 0x8B, 0x41, 0x08, 0xC3 }),
                ("mono_field_get_parent", new byte[] { 0x48, 0x8B, 0x41, 0x10, 0xC3 }),
                // Deliberately undecodable: proves the probe keeps the constant.
                ("mono_class_get_name", new byte[] { 0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83 }),
            };

            int bodyRva = 0x2000;
            var functionRvas = new List<int>();
            foreach (var (_, code) in bodies)
            {
                Array.Copy(code, 0, image, bodyRva, code.Length);
                functionRvas.Add(bodyRva);
                bodyRva += 0x20;
            }

            // A real PE sorts export names. The parser does not rely on it, but keeping
            // the fixture honest costs nothing.
            bodies.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            int count = bodies.Count;
            const int addressTableRva = 0x1100;
            const int namePointerRva = 0x1200;
            const int ordinalTableRva = 0x1300;
            int nameStringRva = 0x1400;

            Put32(image, exportRva + 0x18, count);
            Put32(image, exportRva + 0x1C, addressTableRva);
            Put32(image, exportRva + 0x20, namePointerRva);
            Put32(image, exportRva + 0x24, ordinalTableRva);

            for (int i = 0; i < count; i++)
            {
                var (name, code) = bodies[i];

                // The sort reordered the list, so find where this body actually landed.
                int rva = FindBody(image, code);

                Put32(image, addressTableRva + i * 4, rva);
                Put16(image, ordinalTableRva + i * 2, (ushort)i);
                Put32(image, namePointerRva + i * 4, nameStringRva);

                var bytes = Encoding.ASCII.GetBytes(name);
                Array.Copy(bytes, 0, image, nameStringRva, bytes.Length);
                image[nameStringRva + bytes.Length] = 0;
                nameStringRva += bytes.Length + 1;
            }

            // The root-domain slot the RIP-relative body points at.
            Put64(image, 0x3000, Domain);
        }

        private static int FindBody(byte[] image, byte[] code)
        {
            for (int rva = 0x2000; rva < 0x3000; rva += 0x20)
            {
                bool match = true;
                for (int i = 0; i < code.Length; i++)
                    if (image[rva + i] != code[i]) { match = false; break; }
                if (match) return rva;
            }
            throw new InvalidOperationException("fixture body not found");
        }

        /// <summary>`mov rax,[rip+disp32]; ret`, resolving to targetRva.</summary>
        private static byte[] RipRelative(int bodyRva, int targetRva)
        {
            int displacement = targetRva - (bodyRva + 7);
            var code = new byte[8] { 0x48, 0x8B, 0x05, 0, 0, 0, 0, 0xC3 };
            BitConverter.GetBytes(displacement).CopyTo(code, 3);
            return code;
        }

        private void BuildDomain()
        {
            var domain = _memory.Allocate(Domain, 0x200);
            Put32(domain, MonoOffsets.DomainId, 0);
            Put64(domain, MonoOffsets.DomainAssemblies, AssemblyNode);

            var node = _memory.Allocate(AssemblyNode, 0x20);
            Put64(node, MonoOffsets.GSListData, Assembly);
            Put64(node, MonoOffsets.GSListNext, 0);

            var assembly = _memory.Allocate(Assembly, 0x100);
            Put64(assembly, MonoOffsets.AssemblyImage, Image);
            Put64(assembly, MonoOffsets.AssemblyName, Str("Assembly-CSharp"));
        }

        private void BuildImage()
        {
            var image = _memory.Allocate(Image, 0x800);
            Put64(image, MonoOffsets.ImageFileName, Str("C:\\game\\Assembly-CSharp.dll"));
            Put64(image, MonoOffsets.ImageAssemblyName, Str("Assembly-CSharp"));
            Put64(image, MonoOffsets.ImageAssembly, Assembly);

            if (PlaceClassCachePointer) Put64(image, ClassCacheOffset, BucketTable);

            var buckets = _memory.Allocate(BucketTable, BucketCount * 8);
            // GameManager and Menu chained off one bucket, to exercise next_class_cache.
            Put64(buckets, 3 * 8, ClassGameManager);
            for (int i = 0; i < FillerCount; i++)
                Put64(buckets, (i < 3 ? i : i + 1) * 8, ClassFillerBase + i * 0x200);
        }

        private void BuildClasses()
        {
            var gameManager = _memory.Allocate(ClassGameManager, 0x200);
            Put64(gameManager, MonoOffsets.ClassImage, Image);
            Put64(gameManager, MonoOffsets.ClassName, Str("GameManager"));
            Put64(gameManager, MonoOffsets.ClassNameSpace, Str(""));
            Put32(gameManager, MonoOffsets.ClassVTableSize, GameManagerVTableSize);
            Put64(gameManager, MonoOffsets.ClassFields, FieldArray);
            Put64(gameManager, MonoOffsets.ClassRuntimeInfo, RuntimeInfo);
            Put32(gameManager, MonoOffsets.ClassFieldCount, 4);
            Put64(gameManager, MonoOffsets.ClassNextClassCache, ClassMenu);

            var menu = _memory.Allocate(ClassMenu, 0x200);
            Put64(menu, MonoOffsets.ClassImage, Image);
            Put64(menu, MonoOffsets.ClassName, Str("Menu"));
            Put64(menu, MonoOffsets.ClassNameSpace, Str(""));
            Put64(menu, MonoOffsets.ClassFields, MenuFieldArray);
            Put32(menu, MonoOffsets.ClassFieldCount, 1);
            Put64(menu, MonoOffsets.ClassNextClassCache, 0);

            var fields = _memory.Allocate(FieldArray, 4 * MonoOffsets.FieldSize);
            WriteField(fields, 0, "instance", ClassGameManager, InstanceStaticOffset, MonoTypeKind.Class, isStatic: true);
            WriteField(fields, 1, "hourCount1", ClassGameManager, HourCount1Offset, MonoTypeKind.Int32);
            WriteField(fields, 2, "secondsCount1", ClassGameManager, SecondsCount1Offset, MonoTypeKind.Single);
            WriteField(fields, 3, "sword", ClassGameManager, SwordOffset, MonoTypeKind.Boolean);

            var menuFields = _memory.Allocate(MenuFieldArray, MonoOffsets.FieldSize);
            WriteField(menuFields, 0, "clickSlot", ClassMenu, 0x10, MonoTypeKind.Int32, isStatic: true);

            for (int i = 0; i < FillerCount; i++)
            {
                long address = ClassFillerBase + i * 0x200;
                var filler = _memory.Allocate(address, 0x200);
                Put64(filler, MonoOffsets.ClassImage, Image);
                Put64(filler, MonoOffsets.ClassName, Str("Filler" + i));
                Put64(filler, MonoOffsets.ClassNameSpace, Str(""));
                Put32(filler, MonoOffsets.ClassFieldCount, 0);
                Put64(filler, MonoOffsets.ClassNextClassCache, 0);
            }
        }

        private void WriteField(byte[] array, int index, string name, long parent, int offset,
                                MonoTypeKind kind = MonoTypeKind.Int32, bool isStatic = false)
        {
            int at = index * MonoOffsets.FieldSize;
            Put64(array, at + MonoOffsets.FieldType, Type(kind, isStatic));
            Put64(array, at + MonoOffsets.FieldName, Str(name));
            Put64(array, at + MonoOffsets.FieldParent, parent);
            Put32(array, at + MonoOffsets.FieldOffset, offset);
        }

        /// <summary>
        /// A MonoType. Field flags live here, not on the MonoClassField —
        /// mono_field_get_flags loads field->type first, then reads attrs at +0x08.
        /// The MonoTypeEnum byte is at +0x0A, per mono_type_get_type.
        /// </summary>
        private long Type(MonoTypeKind kind, bool isStatic)
        {
            int key = (int)kind | (isStatic ? 0x1000 : 0);
            if (_types.TryGetValue(key, out long existing)) return existing;

            var buffer = _memory.Allocate(_typeCursor, 0x20);
            Put16(buffer, MonoOffsets.TypeAttrs, (ushort)(isStatic ? MonoOffsets.FieldAttributeStatic : 0x0006));
            buffer[MonoOffsets.TypeType] = (byte)kind;

            long address = _typeCursor;
            _types[key] = address;
            _typeCursor += 0x20;
            return address;
        }

        private void BuildStatics()
        {
            var runtimeInfo = _memory.Allocate(RuntimeInfo, 0x40);
            Put16(runtimeInfo, MonoOffsets.RuntimeInfoMaxDomain, 1);
            Put64(runtimeInfo, MonoOffsets.RuntimeInfoDomainVTables, VTable);

            var vtable = _memory.Allocate(VTable, 0x200);
            Put64(vtable, MonoOffsets.VTableKlass, ClassGameManager);
            vtable[MonoOffsets.VTableFlags] = MonoOffsets.VTableFlagHasStaticFields;
            Put64(vtable, MonoOffsets.VTableBase + 8 * GameManagerVTableSize, StaticData);

            var statics = _memory.Allocate(StaticData, 0x40);
            Put64(statics, InstanceStaticOffset, GameManagerObject);

            var instance = _memory.Allocate(GameManagerObject, 0x100);
            Put32(instance, HourCount1Offset, 3);
            BitConverter.GetBytes(41.5f).CopyTo(instance, SecondsCount1Offset);
            instance[SwordOffset] = 1;
        }

        private long Str(string value)
        {
            if (_strings.TryGetValue(value, out long existing)) return existing;

            var bytes = Encoding.ASCII.GetBytes(value);
            var buffer = _memory.Allocate(_stringCursor, bytes.Length + 1);
            Array.Copy(bytes, buffer, bytes.Length);

            long address = _stringCursor;
            _strings[value] = address;
            _stringCursor += bytes.Length + 1 + 8;
            return address;
        }

        private static void Put16(byte[] buffer, int offset, ushort value) =>
            BitConverter.GetBytes(value).CopyTo(buffer, offset);

        private static void Put32(byte[] buffer, int offset, int value) =>
            BitConverter.GetBytes(value).CopyTo(buffer, offset);

        private static void Put64(byte[] buffer, int offset, long value) =>
            BitConverter.GetBytes(value).CopyTo(buffer, offset);
    }
}
