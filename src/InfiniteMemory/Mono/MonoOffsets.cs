namespace InfiniteSplitter.Mono
{
    /// <summary>
    /// Field offsets inside Mono's own runtime structures, for the
    /// mono-2.0-bdwgc.dll that ships with Unity 2022.3.62f2.
    ///
    /// Every value was measured, not recalled: each doc comment names the exported
    /// function whose machine code the offset was read out of, so the table can be
    /// re-derived with objdump.
    ///
    ///   (probe)    re-derived from the loaded DLL at every attach by
    ///              <see cref="MonoOffsetProbe"/>, so a Unity update that moves it
    ///              is corrected automatically.
    ///   (assumed)  could not be decoded offline. Guarded by the self-validating
    ///              walks in <see cref="MonoRuntime"/>, which fail with
    ///              ClassNotFound / LayoutMismatch rather than lying.
    /// </summary>
    public static class MonoOffsets
    {
        // MonoDomain
        /// <summary>(probe) mono_domain_get_id: 8b 81 94 00 00 00 c3</summary>
        public const int DomainId = 0x94;

        /// <summary>mono_domain_assembly_foreach: mov 0xa0(%rbp),%rbx, then walked as a GSList</summary>
        public const int DomainAssemblies = 0xA0;

        /// <summary>(probe) mono_domain_get_friendly_name: 48 8b 81 b0 00 00 00 c3</summary>
        public const int DomainFriendlyName = 0xB0;

        // GSList
        public const int GSListData = 0x00;
        public const int GSListNext = 0x08;

        // MonoAssembly
        /// <summary>(probe) mono_assembly_get_image_internal: 48 8b 41 60 c3</summary>
        public const int AssemblyImage = 0x60;

        /// <summary>(probe) mono_assembly_get_name_internal: 48 8d 41 10 c3 — a `lea`,
        /// because aname is an embedded MonoAssemblyName whose first field is the name.</summary>
        public const int AssemblyName = 0x10;

        // MonoImage
        /// <summary>(probe) mono_image_get_filename: 48 8b 41 20 c3 — the DLL path</summary>
        public const int ImageFileName = 0x20;

        /// <summary>(probe) mono_image_get_name: 48 8b 41 30 c3 — the assembly name</summary>
        public const int ImageAssemblyName = 0x30;

        /// <summary>(probe) mono_image_get_assembly: 48 8b 81 c0 04 00 00 c3</summary>
        public const int ImageAssembly = 0x4C0;

        /// <summary>
        /// (measured live) The pointer to the class-cache bucket array.
        ///
        /// Not decoded from an accessor: the offline guess of 0x110 + 0x20 was wrong
        /// and found zero classes. 0x4F0 was found by scanning the MonoImage for an
        /// array whose every non-null entry is a MonoClass of this image.
        /// <see cref="MonoRuntime"/> keeps that search as a permanent fallback, so the
        /// same class of failure fixes itself next time.
        ///
        /// This is the pointer offset itself, not a struct base. The bucket count is
        /// never read — one less unverified offset to carry.
        /// </summary>
        public const int ImageClassCacheTable = 0x4F0;

        // MonoClass
        /// <summary>(probe) mono_class_get_rank: 0f b6 41 1a c3 — rank sits next to class_kind</summary>
        public const int ClassRank = 0x1A;

        /// <summary>mono_class_num_fields: movzbl 0x1b(%rcx),%eax</summary>
        public const int ClassKind = 0x1B;

        /// <summary>testl $0x100000,0x28(%rsi)</summary>
        public const int ClassFlags = 0x28;

        /// <summary>(probe) mono_class_get_nesting_type: 48 8b 41 38 c3</summary>
        public const int ClassNestedIn = 0x38;

        /// <summary>(probe) mono_class_get_image: 48 8b 41 40 c3</summary>
        public const int ClassImage = 0x40;

        /// <summary>(assumed) the accessor is wrapped in a GC-unsafe thunk and does not
        /// decode offline. Guarded by <see cref="MonoRuntime"/>'s candidate search.</summary>
        public const int ClassName = 0x48;

        /// <summary>(assumed) immediately after ClassName, same thunk problem.</summary>
        public const int ClassNameSpace = 0x50;

        /// <summary>
        /// Candidates tried, in order, if ClassName yields nothing readable. Excludes
        /// 0x38, 0x40 and 0x58 — nested_in, image and type_token are each confirmed by
        /// <see cref="MonoOffsetProbe"/>, so trying them could only add false positives.
        /// </summary>
        public static readonly int[] ClassNameCandidates = { 0x48, 0x50, 0x30, 0x60, 0x68 };

        /// <summary>(probe) mono_class_get_type_token: 8b 41 58 c3</summary>
        public const int ClassTypeToken = 0x58;

        /// <summary>mono_vtable_get_static_field_data: movsxd 0x5c(%rax),%rax</summary>
        public const int ClassVTableSize = 0x5C;

        /// <summary>mono_class_get_fields inner: mov 0x98(%rsi),%rdi, iterated with add $0x20</summary>
        public const int ClassFields = 0x98;

        /// <summary>mono_class_vtable inner: mov 0xd0(%rdi),%rsi</summary>
        public const int ClassRuntimeInfo = 0xD0;

        /// <summary>(probe) mono_class_get_userdata: 48 8b 81 e8 00 00 00 c3</summary>
        public const int ClassUserData = 0xE8;

        /// <summary>mono_class_num_fields jump-table arm: mov 0x100(%rdx),%eax</summary>
        public const int ClassFieldCount = 0x100;

        /// <summary>(assumed) sits immediately after field_count; the class_cache
        /// next_value callback is a `lea rax,[rcx+0x108]; ret` thunk.</summary>
        public const int ClassNextClassCache = 0x108;

        // MonoClassField
        public const int FieldType = 0x00;

        /// <summary>(probe) mono_field_get_name: 48 8b 41 08 c3</summary>
        public const int FieldName = 0x08;

        /// <summary>(probe) mono_field_get_parent: 48 8b 41 10 c3</summary>
        public const int FieldParent = 0x10;

        /// <summary>mono_field_get_offset: mov 0x18(%rbx),%eax</summary>
        public const int FieldOffset = 0x18;

        /// <summary>mono_class_get_fields iterates with add $0x20,%rdi</summary>
        public const int FieldSize = 0x20;

        // MonoClassRuntimeInfo
        /// <summary>uint16 max_domain, guarding the domain_vtables index</summary>
        public const int RuntimeInfoMaxDomain = 0x00;
        public const int RuntimeInfoDomainVTables = 0x08;

        // MonoVTable
        // All four read straight out of mono_vtable_get_static_field_data:
        //   f6 41 30 04   test byte [rcx+0x30],4      <- VTableFlags, VTableFlagHasStaticFields
        //   48 8b 01      mov rax,[rcx]               <- VTableKlass
        //   48 63 40 5c   movsxd rax,[rax+0x5c]       <- ClassVTableSize
        //   48 8b 44 c1 48  mov rax,[rcx+rax*8+0x48]  <- VTableBase
        public const int VTableKlass = 0x00;
        public const int VTableFlags = 0x30;
        public const byte VTableFlagHasStaticFields = 0x04;
        public const int VTableBase = 0x48;

        // MonoType
        /// <summary>(probe) mono_type_get_attrs: 0f b7 41 08 c3 — uint16.
        /// mono_field_get_flags starts `mov rax,[rcx]` (field->type) and reads this,
        /// so a field's static/public flags live here, not on the MonoClassField.</summary>
        public const int TypeAttrs = 0x08;

        /// <summary>(probe) mono_type_get_type: 0f be 41 0a c3 — the MonoTypeEnum byte.</summary>
        public const int TypeType = 0x0A;

        /// <summary>ECMA FieldAttributes.Static, tested against <see cref="TypeAttrs"/>.</summary>
        public const int FieldAttributeStatic = 0x0010;
    }
}
