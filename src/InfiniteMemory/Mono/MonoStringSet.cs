using System;
using System.Collections.Generic;
using InfiniteSplitter.Memory;

namespace InfiniteSplitter.Mono
{
    /// <summary>
    /// Reads a HashSet&lt;string&gt; out of the game — the storage behind the
    /// `collectible:`, `activation:` and `count:` triggers. Out of process a method
    /// cannot be called, only its storage read, so this walks the set itself:
    ///
    ///     _buckets int[]   _slots Slot[]   _count   _lastIndex   _freeList
    ///     Slot { int hashCode; int next; T value; }
    ///
    /// Field offsets come from the live object's own class, and `value`'s position
    /// inside a Slot is measured — see <see cref="ResolveValueOffset"/>. Contents are
    /// cached and re-read only when the set's size changes, because a string read per
    /// entry is far too much per frame.
    /// </summary>
    public sealed class MonoStringSet
    {
        private const int SlotSize = 16;          // int hashCode, int next, pointer value (x64)
        private const int MaxEntries = 100000;

        private readonly MonoRuntime _runtime;
        private readonly IMemory _memory;

        private long _classAddress;
        private int _slotsOffset = -1;
        private int _lastIndexOffset = -1;
        private int _countOffset = -1;
        private int _valueOffset = -1;

        private long _cachedSlots;
        private int _cachedLastIndex = -1;
        private int _cachedCount = -1;

        private readonly List<string> _entries = new List<string>();

        /// <summary>The same ids as <see cref="_entries"/>, for lookup. A trigger asks
        /// Contains once per atom per tick, and a set can hold a thousand ids.</summary>
        private readonly HashSet<string> _index = new HashSet<string>(StringComparer.Ordinal);

        public MonoStringSet(MonoRuntime runtime, IMemory memory)
        {
            _runtime = runtime;
            _memory = memory;
        }

        /// <summary>Everything in the set, as of the last refresh.</summary>
        public IReadOnlyList<string> Entries => _entries;

        /// <summary>True once a layout has been worked out for this set's class.</summary>
        public bool Resolved => _slotsOffset >= 0;

        /// <summary>What went wrong, if the layout could not be worked out.</summary>
        public string Problem { get; private set; }

        /// <summary>Everything the reader knows, so an empty set can be told apart
        /// from a failed one.</summary>
        public string Describe(long setObject)
        {
            if (setObject == 0) return "the field is null (no save loaded yet?)";

            long vtable = _memory.ReadPointer(setObject);
            long klass = vtable == 0 ? 0 : _memory.ReadPointer(vtable + MonoOffsets.VTableKlass);
            string name = klass == 0 ? "?" :
                _memory.ReadAsciiString(_memory.ReadPointer(klass + _runtime.ResolvedClassNameOffset), 128);

            string layout = _slotsOffset < 0
                ? "layout unresolved" + (Problem == null ? "" : ": " + Problem)
                : $"_slots+0x{_slotsOffset:X} _lastIndex+0x{_lastIndexOffset:X} " +
                  $"_count+0x{_countOffset:X} value+0x{_valueOffset:X}";

            string live = "";
            if (_slotsOffset >= 0)
            {
                long slots = _memory.ReadPointer(setObject + _slotsOffset);
                live = $"  slots=0x{slots:X} lastIndex={_memory.ReadInt32(setObject + _lastIndexOffset)}" +
                       (_countOffset >= 0 ? $" count={_memory.ReadInt32(setObject + _countOffset)}" : "");
            }

            return $"object=0x{setObject:X} class={name}  {layout}{live}";
        }

        /// <summary>Re-reads the set if it has changed; two int reads when it has not.</summary>
        public bool Refresh(long setObject)
        {
            if (setObject == 0) { Forget(); _cachedCount = -1; return false; }
            if (!EnsureLayout(setObject)) return false;

            long slots = _memory.ReadPointer(setObject + _slotsOffset);
            int lastIndex = _memory.ReadInt32(setObject + _lastIndexOffset);
            int count = _countOffset >= 0 ? _memory.ReadInt32(setObject + _countOffset) : lastIndex;

            if (slots == _cachedSlots && lastIndex == _cachedLastIndex && count == _cachedCount)
                return true;   // unchanged since last time

            _cachedSlots = slots;
            _cachedLastIndex = lastIndex;
            _cachedCount = count;
            Forget();

            if (slots == 0 || lastIndex <= 0 || lastIndex > MaxEntries) return true;

            // One bulk read of the slot array, then a string read per occupied entry.
            var raw = _memory.ReadBytes(slots + MonoReader.ArrayHeaderSize, lastIndex * SlotSize);

            for (int i = 0; i < lastIndex; i++)
            {
                int hashCode = BitConverter.ToInt32(raw, i * SlotSize);
                if (hashCode < 0) continue;                       // a freed slot

                long value = BitConverter.ToInt64(raw, i * SlotSize + _valueOffset);
                if (value == 0) continue;

                string text = MonoReader.ReadString(_memory, value, 512);
                if (text.Length == 0) continue;

                _entries.Add(text);
                _index.Add(text);
            }

            return true;
        }

        private void Forget()
        {
            _entries.Clear();
            _index.Clear();
        }

        public bool Contains(string id) => _index.Contains(id);

        public int CountWithPrefix(string prefix)
        {
            int total = 0;
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].StartsWith(prefix, StringComparison.Ordinal))
                    total++;
            return total;
        }

        private bool EnsureLayout(long setObject)
        {
            long vtable = _memory.ReadPointer(setObject);
            if (vtable == 0) return false;
            long klass = _memory.ReadPointer(vtable + MonoOffsets.VTableKlass);
            if (klass == 0) return false;

            if (klass == _classAddress && _slotsOffset >= 0) return true;
            if (klass == _classAddress && Problem != null) return false;   // already failed

            _classAddress = klass;
            _slotsOffset = _lastIndexOffset = _countOffset = _valueOffset = -1;

            var described = _runtime.DescribeClass(klass);
            if (described.IsErr) { Problem = "class unreadable: " + described.Error; return false; }

            // Mono has shipped both the reference-source names and the m_ prefixed ones.
            var slots = described.Value.Find("_slots") ?? described.Value.Find("m_slots");
            var lastIndex = described.Value.Find("_lastIndex") ?? described.Value.Find("m_lastIndex");
            var count = described.Value.Find("_count") ?? described.Value.Find("m_count");

            if (slots == null || lastIndex == null)
            {
                Problem = "HashSet<string> has no _slots/_lastIndex — layout changed";
                return false;
            }

            _slotsOffset = slots.Offset;
            _lastIndexOffset = lastIndex.Offset;
            _countOffset = count?.Offset ?? -1;

            if (!ResolveValueOffset(setObject))
            {
                _slotsOffset = -1;
                Problem = "could not find the string inside a HashSet slot";
                return false;
            }

            Problem = null;
            return true;
        }

        /// <summary>
        /// Finds where the string pointer sits inside a Slot by trying the plausible
        /// positions and keeping the one that yields readable strings.
        ///
        /// `Slot { int hashCode; int next; T value; }` puts it at +8, but that depends
        /// on the struct's field order and padding surviving this Mono build. Measuring
        /// costs one pass over a handful of slots, and it is the same approach that
        /// caught the class-cache offset being wrong.
        /// </summary>
        private bool ResolveValueOffset(long setObject)
        {
            long slots = _memory.ReadPointer(setObject + _slotsOffset);
            int lastIndex = _memory.ReadInt32(setObject + _lastIndexOffset);
            if (slots == 0 || lastIndex <= 0) return false;      // nothing to measure against yet

            int sample = Math.Min(lastIndex, 32);
            var raw = _memory.ReadBytes(slots + MonoReader.ArrayHeaderSize, sample * SlotSize);

            foreach (int candidate in new[] { 8, 0 })
            {
                int readable = 0, occupied = 0;
                for (int i = 0; i < sample; i++)
                {
                    if (BitConverter.ToInt32(raw, i * SlotSize) < 0) continue;
                    occupied++;

                    long value = BitConverter.ToInt64(raw, i * SlotSize + candidate);
                    if (value == 0) continue;
                    if (MonoReader.ReadString(_memory, value, 512).Length > 0) readable++;
                }

                if (occupied > 0 && readable * 2 >= occupied)
                {
                    _valueOffset = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
