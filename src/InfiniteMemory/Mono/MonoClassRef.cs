using System;
using System.Collections.Generic;

namespace InfiniteSplitter.Mono
{
    /// <summary>
    /// A resolved MonoClass: address, name, fields by name, static storage.
    ///
    /// Resolved once at attach and valid for the life of the process — this Mono is
    /// bdwgc, the Boehm collector, which does not move objects. Only the instance
    /// pointer held in a static field needs re-reading per tick.
    /// </summary>
    public sealed class MonoClassRef
    {
        public long Address;
        public string Name = "";
        public string Namespace = "";

        public readonly Dictionary<string, MonoFieldRef> Fields =
            new Dictionary<string, MonoFieldRef>(StringComparer.Ordinal);

        /// <summary>Field order as Mono lays them out, for diagnostics.</summary>
        public readonly List<MonoFieldRef> FieldList = new List<MonoFieldRef>();

        /// <summary>The class's static field storage, or 0 when it has no statics or
        /// its static constructor has not run yet. Zero is a state, not an error.</summary>
        public long StaticData;

        public long VTable;

        /// <summary>Case-sensitive first, then case-insensitive: the game's own naming
        /// is inconsistent (SorcererBestiary has a capital S, every other bestiary flag
        /// does not), so a config written from memory should still resolve.</summary>
        public MonoFieldRef Find(string name)
        {
            if (Fields.TryGetValue(name, out var exact)) return exact;
            foreach (var field in FieldList)
                if (string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
                    return field;
            return null;
        }

        public long StaticFieldAddress(string name)
        {
            var field = Find(name);
            if (field == null || !field.IsStatic || StaticData == 0) return 0;
            return StaticData + field.Offset;
        }

        /// <summary>Largest instance-field extent, used to size a one-shot snapshot read.
        /// Derived rather than read from MonoClass.instance_size, whose accessor does
        /// not decode — one fewer offset to be wrong about.</summary>
        public int InstanceExtent()
        {
            int max = 0;
            foreach (var field in FieldList)
            {
                if (field.IsStatic) continue;
                int end = field.Offset + Math.Max(8, field.Kind.Size());
                if (end > max) max = end;
            }
            return max == 0 ? 0 : max + 16;   // slack for anything we mis-sized
        }

        public override string ToString() =>
            (string.IsNullOrEmpty(Namespace) ? Name : Namespace + "." + Name) +
            $" @ 0x{Address:X} ({FieldList.Count} fields)";
    }
}
