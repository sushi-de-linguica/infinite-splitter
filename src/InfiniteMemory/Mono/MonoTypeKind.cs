namespace InfiniteSplitter.Mono
{
    /// <summary>
    /// MonoTypeEnum, with the raw values kept so the mapping stays checkable against
    /// Mono's headers. Read from MonoType+0x0A, per mono_type_get_type:
    /// `movsx eax,byte [rcx+0xa]; ret`.
    /// </summary>
    public enum MonoTypeKind : byte
    {
        End = 0x00,
        Void = 0x01,
        Boolean = 0x02,
        Char = 0x03,
        SByte = 0x04,
        Byte = 0x05,
        Int16 = 0x06,
        UInt16 = 0x07,
        Int32 = 0x08,
        UInt32 = 0x09,
        Int64 = 0x0A,
        UInt64 = 0x0B,
        Single = 0x0C,
        Double = 0x0D,
        String = 0x0E,
        Pointer = 0x0F,
        ByRef = 0x10,
        ValueType = 0x11,
        Class = 0x12,
        Var = 0x13,
        Array = 0x14,
        GenericInst = 0x15,
        TypedByRef = 0x16,
        IntPtr = 0x18,
        UIntPtr = 0x19,
        FnPtr = 0x1B,
        Object = 0x1C,
        SzArray = 0x1D,
        MVar = 0x1E,
    }

    public static class MonoTypeKindExtensions
    {
        /// <summary>Can be read as a double without losing what the caller meant.</summary>
        public static bool IsNumeric(this MonoTypeKind kind) => kind switch
        {
            MonoTypeKind.Boolean or MonoTypeKind.Char or
            MonoTypeKind.SByte or MonoTypeKind.Byte or
            MonoTypeKind.Int16 or MonoTypeKind.UInt16 or
            MonoTypeKind.Int32 or MonoTypeKind.UInt32 or
            MonoTypeKind.Int64 or MonoTypeKind.UInt64 or
            MonoTypeKind.Single or MonoTypeKind.Double => true,
            _ => false,
        };

        /// <summary>A reference — the field holds a pointer, not a value.</summary>
        public static bool IsReference(this MonoTypeKind kind) => kind switch
        {
            MonoTypeKind.String or MonoTypeKind.Class or MonoTypeKind.Object or
            MonoTypeKind.Array or MonoTypeKind.SzArray or MonoTypeKind.GenericInst => true,
            _ => false,
        };

        /// <summary>Bytes the value occupies inline in the object.</summary>
        public static int Size(this MonoTypeKind kind) => kind switch
        {
            MonoTypeKind.Boolean or MonoTypeKind.SByte or MonoTypeKind.Byte => 1,
            MonoTypeKind.Char or MonoTypeKind.Int16 or MonoTypeKind.UInt16 => 2,
            MonoTypeKind.Int32 or MonoTypeKind.UInt32 or MonoTypeKind.Single => 4,
            MonoTypeKind.Int64 or MonoTypeKind.UInt64 or MonoTypeKind.Double => 8,
            _ => kind.IsReference() ? 8 : 0,   // 0 = unknown; the caller must not read it
        };

        /// <summary>What a config author would call it, for error messages.</summary>
        public static string Display(this MonoTypeKind kind) => kind switch
        {
            MonoTypeKind.Boolean => "bool",
            MonoTypeKind.Int32 => "int",
            MonoTypeKind.Single => "float",
            MonoTypeKind.Double => "double",
            MonoTypeKind.String => "string",
            _ => kind.ToString(),
        };
    }
}
