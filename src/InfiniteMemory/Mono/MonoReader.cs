using System;
using System.Collections.Generic;
using System.Text;
using InfiniteSplitter.Memory;

namespace InfiniteSplitter.Mono
{
    /// <summary>A resolved view of a List&lt;T&gt; on the Mono heap.</summary>
    public struct MonoListView
    {
        /// <summary>The backing T[] object.</summary>
        public long Items;
        /// <summary>Where element 0 begins — the array object plus its header.</summary>
        public long Elements;
        public int Count;
    }

    /// <summary>
    /// Reads the corlib types the game exposes: System.String and List&lt;T&gt;.
    ///
    /// No corlib field offset is hardcoded — `_items` and `_size` are looked up by
    /// name on the object's own class, so a different List&lt;T&gt; layout keeps
    /// working. The only constants here belong to the Mono object model itself,
    /// not to any particular class.
    /// </summary>
    public static class MonoReader
    {
        /// <summary>MonoArray on x64: vtable, synchronisation, bounds, max_length, then
        /// the elements.</summary>
        public const int ArrayHeaderSize = 0x20;

        /// <summary>MonoString: length at +0x10, UTF-16 characters at +0x14.</summary>
        public const int StringLengthOffset = 0x10;
        public const int StringCharsOffset = 0x14;

        public static string ReadString(IMemory memory, long address, int maxChars = 1024)
        {
            if (address == 0) return "";
            int length = memory.ReadInt32(address + StringLengthOffset);
            if (length <= 0 || length > maxChars) return "";
            return Encoding.Unicode.GetString(memory.ReadBytes(address + StringCharsOffset, length * 2));
        }

        /// <summary>
        /// Resolves a List&lt;T&gt;. The layout is cached per class address, so the
        /// per-tick cost is two reads once the first instance has been seen.
        /// </summary>
        public static bool TryReadList(MonoRuntime runtime, IMemory memory, long listObject,
                                       Dictionary<long, ListLayout> cache, out MonoListView view)
        {
            view = default(MonoListView);
            if (runtime == null || listObject == 0) return false;

            long vtable = memory.ReadPointer(listObject);
            if (vtable == 0) return false;
            long klass = memory.ReadPointer(vtable + MonoOffsets.VTableKlass);
            if (klass == 0) return false;

            if (!cache.TryGetValue(klass, out var layout))
            {
                var described = runtime.DescribeClass(klass);
                if (described.IsErr) return false;

                // Mono's List<T> has used both names across versions.
                var items = described.Value.Find("_items") ?? described.Value.Find("_array");
                var size = described.Value.Find("_size") ?? described.Value.Find("_count");
                if (items == null || size == null) return false;

                layout = new ListLayout { ItemsOffset = items.Offset, SizeOffset = size.Offset };
                cache[klass] = layout;
            }

            long array = memory.ReadPointer(listObject + layout.ItemsOffset);
            int count = memory.ReadInt32(listObject + layout.SizeOffset);
            if (array == 0 || count < 0 || count > 1000000) return false;

            view = new MonoListView
            {
                Items = array,
                Elements = array + ArrayHeaderSize,
                Count = count,
            };
            return true;
        }

        public sealed class ListLayout
        {
            public int ItemsOffset;
            public int SizeOffset;
        }

        /// <summary>Reads a List&lt;int&gt;, which is what a List of an int-backed enum
        /// such as Skills looks like in storage.</summary>
        public static bool TryReadInt32List(MonoRuntime runtime, IMemory memory, long listObject,
                                            Dictionary<long, ListLayout> cache, List<int> into)
        {
            into.Clear();
            if (!TryReadList(runtime, memory, listObject, cache, out var view)) return false;
            if (view.Count == 0) return true;

            var bytes = memory.ReadBytes(view.Elements, view.Count * 4);
            for (int i = 0; i < view.Count; i++)
                into.Add(BitConverter.ToInt32(bytes, i * 4));
            return true;
        }
    }
}
