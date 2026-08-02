using System;
using InfiniteSplitter.Mono;

namespace InfiniteSplitter.Game
{
    /// <summary>
    /// One tick's worth of game state, taken as a couple of bulk reads.
    ///
    /// The trigger interface asks a question per atom (`Flag("sword")`,
    /// `Number("souls")`, …), and a ReadProcessMemory each would put a syscall count
    /// on the hot path proportional to the size of someone's config. Copying the whole
    /// GameManager object once makes every later question a byte index into an array —
    /// and makes a recorded snapshot a replayable test fixture.
    /// </summary>
    public sealed class GameSnapshot
    {
        public byte[] GameManager { get; private set; }
        public byte[] UIManager { get; private set; }

        public long GameManagerAddress { get; private set; }
        public long UIManagerAddress { get; private set; }

        /// <summary>False on the title screen, before the singleton exists. Ordinary:
        /// every read then answers false/0 and nothing fires.</summary>
        public bool GameManagerAlive => GameManager != null;
        public bool UIManagerAlive => UIManager != null;

        /// <summary>RunLogger's run count: rises by one per new game. -1 when unavailable.</summary>
        public int RunCount { get; internal set; } = -1;

        /// <summary>Whether a scene transition has been started and has not completed.
        /// Filled by <see cref="Infinitevania.TakeSnapshot"/>, which is the layer that
        /// can follow the string pointer out of the snapshot.</summary>
        public bool SceneLoadInFlight { get; internal set; }

        internal void SetGameManager(long address, byte[] bytes)
        {
            GameManagerAddress = address;
            GameManager = bytes;
        }

        internal void SetUIManager(long address, byte[] bytes)
        {
            UIManagerAddress = address;
            UIManager = bytes;
        }

        internal void Clear()
        {
            GameManager = UIManager = null;
            GameManagerAddress = UIManagerAddress = 0;
            RunCount = -1;
            SceneLoadInFlight = false;
        }

        public bool Bool(MonoFieldRef field) => Byte(GameManager, field) != 0;

        public bool BoolUI(MonoFieldRef field) => Byte(UIManager, field) != 0;

        public int Int32(MonoFieldRef field) =>
            TryAt(GameManager, field, sizeof(int), out int at) ? BitConverter.ToInt32(GameManager, at) : 0;

        public float Single(MonoFieldRef field) =>
            TryAt(GameManager, field, sizeof(float), out int at) ? BitConverter.ToSingle(GameManager, at) : 0f;

        public long Pointer(MonoFieldRef field) =>
            TryAt(GameManager, field, sizeof(long), out int at) ? BitConverter.ToInt64(GameManager, at) : 0L;

        public long PointerUI(MonoFieldRef field) =>
            TryAt(UIManager, field, sizeof(long), out int at) ? BitConverter.ToInt64(UIManager, at) : 0L;

        /// <summary>
        /// A field as a double, whatever numeric type it is. False for anything that is
        /// not a number, so the caller reports the mistake instead of comparing garbage.
        /// </summary>
        public bool TryNumber(MonoFieldRef field, out double value)
        {
            value = 0;
            if (field == null || !GameManagerAlive || field.IsStatic) return false;

            var buffer = GameManager;
            int at;
            switch (field.Kind)
            {
                case MonoTypeKind.Boolean: value = Byte(buffer, field) != 0 ? 1 : 0; break;
                case MonoTypeKind.SByte: value = (sbyte)Byte(buffer, field); break;
                case MonoTypeKind.Byte: value = Byte(buffer, field); break;
                case MonoTypeKind.Char:
                case MonoTypeKind.UInt16: value = TryAt(buffer, field, 2, out at) ? BitConverter.ToUInt16(buffer, at) : 0; break;
                case MonoTypeKind.Int16: value = TryAt(buffer, field, 2, out at) ? BitConverter.ToInt16(buffer, at) : 0; break;
                case MonoTypeKind.Int32: value = TryAt(buffer, field, 4, out at) ? BitConverter.ToInt32(buffer, at) : 0; break;
                case MonoTypeKind.UInt32: value = TryAt(buffer, field, 4, out at) ? BitConverter.ToUInt32(buffer, at) : 0; break;
                case MonoTypeKind.Int64: value = TryAt(buffer, field, 8, out at) ? BitConverter.ToInt64(buffer, at) : 0; break;
                case MonoTypeKind.UInt64: value = TryAt(buffer, field, 8, out at) ? BitConverter.ToUInt64(buffer, at) : 0; break;
                case MonoTypeKind.Single: value = TryAt(buffer, field, 4, out at) ? BitConverter.ToSingle(buffer, at) : 0; break;
                case MonoTypeKind.Double: value = TryAt(buffer, field, 8, out at) ? BitConverter.ToDouble(buffer, at) : 0; break;
                default: return false;
            }
            return true;
        }

        private static byte Byte(byte[] buffer, MonoFieldRef field)
        {
            if (buffer == null || field == null || field.IsStatic) return 0;
            int offset = field.Offset;
            return offset < 0 || offset >= buffer.Length ? (byte)0 : buffer[offset];
        }

        /// <summary>
        /// Where a field's bytes start inside the snapshot, if it is safe to read
        /// <paramref name="size"/> of them there. A static field never is: its offset
        /// indexes the class's static storage, not the object.
        /// </summary>
        private static bool TryAt(byte[] buffer, MonoFieldRef field, int size, out int at)
        {
            at = field?.Offset ?? -1;
            return buffer != null && field != null && !field.IsStatic
                   && at >= 0 && at + size <= buffer.Length;
        }
    }
}
