using System;
using System.Collections.Generic;
using InfiniteSplitter.Diagnostics;
using InfiniteSplitter.Memory;
using InfiniteSplitter.Mono;

namespace InfiniteSplitter.Game
{
    /// <summary>
    /// The game, as this project needs it: attach, resolve, and hand out the classes
    /// everything else reads from. Only GameManager is required; the rest degrade to
    /// a reduced feature set rather than failing the attach.
    /// </summary>
    public sealed class Infinitevania : IDisposable
    {
        public const string ProcessName = "Infinitevania";
        public const string GameAssembly = "Assembly-CSharp";

        private readonly ProcessHook _hook;

        public MonoRuntime Runtime { get; private set; }
        public MonoClassRef GameManager { get; private set; }
        public MonoClassRef UIManager { get; private set; }
        public MonoClassRef RunLogger { get; private set; }
        public MonoClassRef Menu { get; private set; }

        public long MonoModuleBase { get; private set; }

        /// <summary>Where GameManager.instance lives. The pointer at this address is the
        /// only thing that has to be re-read per tick — this collector never moves the
        /// object.</summary>
        public long GameManagerInstanceSlot { get; private set; }
        public long UIManagerInstanceSlot { get; private set; }

        public bool IsHooked => _hook.IsHooked;
        public IMemory Memory => _hook;
        public int ProcessId => _hook.ProcessId;

        /// <summary>The process itself, for module and region enumeration. Only the
        /// memory scans in splitcheck need it.</summary>
        public IProcessWrapper Process => _hook.Process;

        /// <summary>Non-fatal notes from the attach, shown in the Debug tab.</summary>
        public readonly List<string> Notes = new List<string>();

        /// <summary>The active scene. Falls back on its own when the live read is
        /// unavailable; see <see cref="NativeScene"/>.</summary>
        public NativeScene Scene { get; } = new NativeScene();

        public Infinitevania(IProcessWrapper process = null)
        {
            _hook = new ProcessHook(ProcessName, process);
            _hook.Hooked += OnHooked;
            _hook.Exited += OnExited;
        }

        public Result TryRefresh() => _hook.TryRefresh();

        private Result OnHooked()
        {
            Notes.Clear();

            if (IntPtr.Size != 8)
                return Result.Err(RefreshErrorReason.HostNot64Bit,
                    "this process is 32-bit; pointers in the 64-bit game cannot be read");

            var module = _hook.Process.FindModule(MonoRuntime.MonoModuleName);
            if (module == null)
                return Result.Err(RefreshErrorReason.MonoModuleNotFound, MonoRuntime.MonoModuleName);

            MonoModuleBase = module.BaseAddress;

            var runtime = new MonoRuntime(_hook);
            var attach = runtime.Attach(module.BaseAddress, GameAssembly);
            if (attach.IsErr) return attach;
            Runtime = runtime;

            if (runtime.Probe.Overrides.Count > 0)
                Notes.Add("Mono offsets corrected at runtime: " + string.Join(", ", runtime.Probe.Overrides.ToArray()));
            if (runtime.ResolvedClassNameOffset != MonoOffsets.ClassName)
                Notes.Add($"MonoClass.name resolved to 0x{runtime.ResolvedClassNameOffset:X} " +
                          $"(compiled-in default was 0x{MonoOffsets.ClassName:X})");

            // GameManager is required; the rest degrade gracefully.
            var gameManager = runtime.FindClass("GameManager");
            if (gameManager.IsErr) return Result.Err(gameManager.Error);
            GameManager = gameManager.Value;

            var instance = GameManager.Find("instance");
            if (instance == null)
                return Result.Err(RefreshErrorReason.FieldNotFound, "GameManager.instance");
            if (GameManager.StaticData == 0)
                return Result.Err(RefreshErrorReason.StaticDataNull,
                    "GameManager's static storage is not up yet — the game is still starting");
            GameManagerInstanceSlot = GameManager.StaticData + instance.Offset;

            UIManager = Optional(runtime, "UIManager");
            if (UIManager != null)
            {
                var uiInstance = UIManager.Find("instance");
                if (uiInstance != null && UIManager.StaticData != 0)
                    UIManagerInstanceSlot = UIManager.StaticData + uiInstance.Offset;
            }

            RunLogger = Optional(runtime, "RunLogger");
            if (RunLogger != null && RunLogger.StaticData != 0)
            {
                var data = RunLogger.Find("data");
                if (data != null && data.IsStatic) _runsListSlot = RunLogger.StaticData + data.Offset;
                else Notes.Add("the run list is unavailable — new-game detection falls back to the clock");
            }

            Menu = Optional(runtime, "Menu");

            // Optional by design: the scene read is the only part of this project
            // resting on a fixed pointer chain rather than on resolved metadata, so it
            // degrades to the approximation instead of failing the attach.
            Scene.Bind(_hook.Process);
            if (Scene.Problem != null) Notes.Add(Scene.Problem);

            Fields = GameFields.Resolve(GameManager, UIManager);
            foreach (var missing in Fields.Missing)
                Notes.Add("field not found: " + missing);

            return Result.Ok();
        }

        private MonoClassRef Optional(MonoRuntime runtime, string name)
        {
            var result = runtime.FindClass(name);
            if (result.IsOk) return result.Value;
            Notes.Add("optional class " + name + " unavailable: " + result.Error.Describe());
            return null;
        }

        private void OnExited()
        {
            Runtime = null;
            GameManager = UIManager = RunLogger = Menu = null;
            GameManagerInstanceSlot = UIManagerInstanceSlot = 0;
            MonoModuleBase = 0;
            Fields = null;
            _runsListSlot = 0;
            _runsFieldOffset = _listSizeOffset = -1;
            _gameManagerBuffer = _uiManagerBuffer = null;
            Snapshot.Clear();
            Scene.Detached();
            Notes.Clear();
        }

        /// <summary>The live GameManager object, or 0 before the singleton exists
        /// (the title screen). Zero is a state, not an error.</summary>
        public long ReadGameManagerInstance() =>
            GameManagerInstanceSlot == 0 ? 0 : _hook.ReadPointer(GameManagerInstanceSlot);

        public long ReadUIManagerInstance() =>
            UIManagerInstanceSlot == 0 ? 0 : _hook.ReadPointer(UIManagerInstanceSlot);

        /// <summary>Fields resolved at attach; null until then.</summary>
        public GameFields Fields { get; private set; }

        /// <summary>The most recent snapshot, reused between ticks so the per-frame
        /// path does not allocate 60 times a second.</summary>
        public GameSnapshot Snapshot { get; } = new GameSnapshot();

        /// <summary>
        /// Takes this tick's snapshot: two bulk reads plus the RunLogger counter, so a
        /// handful of ReadProcessMemory calls however elaborate the split config is.
        ///
        /// The two buffers are reused between ticks — they are sized from the resolved
        /// field extents, which only change at attach.
        /// </summary>
        public void TakeSnapshot()
        {
            if (!IsHooked || Fields == null) { Snapshot.Clear(); return; }

            long gameManager = ReadGameManagerInstance();
            Snapshot.SetGameManager(gameManager,
                Fill(ref _gameManagerBuffer, gameManager, Fields.GameManagerExtent));

            long uiManager = ReadUIManagerInstance();
            Snapshot.SetUIManager(uiManager,
                Fill(ref _uiManagerBuffer, uiManager, Fields.UIManagerExtent));

            Snapshot.RunCount = ReadRunCount();
        }

        /// <summary>
        /// Reads <paramref name="extent"/> bytes of the object into the buffer, growing
        /// it if the extent changed. Null means "the object does not exist", which is
        /// how the snapshot reports a singleton that is not up yet — a failed read is
        /// not the same thing, and comes back zeroed rather than stale.
        /// </summary>
        private byte[] Fill(ref byte[] buffer, long address, int extent)
        {
            if (address == 0 || extent <= 0) return null;
            if (buffer == null || buffer.Length != extent) buffer = new byte[extent];

            _hook.TryReadInto(address, buffer, extent);
            return buffer;
        }

        /// <summary>
        /// The length of the run list, which rises once per new game. Being a level
        /// rather than an edge, a slow poll cannot miss it.
        ///
        /// Returns -1 when unavailable: the list's storage does not exist until the
        /// first new game of a session, which is a state, not an error.
        /// </summary>
        private int ReadRunCount()
        {
            if (_runsListSlot == 0) return -1;

            long wrapper = _hook.ReadPointer(_runsListSlot);
            if (wrapper == 0) return -1;

            // Both layouts are resolved from the live objects rather than hardcoded:
            // neither the wrapper nor the generic list it holds can be looked up by
            // name in the class cache.
            if (_runsFieldOffset < 0)
            {
                var wrapperClass = Runtime.DescribeObject(wrapper);
                if (wrapperClass.IsErr) return -1;
                var runs = wrapperClass.Value.Find("runs");
                if (runs == null) { Notes.Add("the run list wrapper has no 'runs' field"); return -1; }
                _runsFieldOffset = runs.Offset;
            }

            long list = _hook.ReadPointer(wrapper + _runsFieldOffset);
            if (list == 0) return -1;

            if (_listSizeOffset < 0)
            {
                var listClass = Runtime.DescribeObject(list);
                if (listClass.IsErr) return -1;
                var size = listClass.Value.Find("_size") ?? listClass.Value.Find("_count");
                if (size == null) { Notes.Add("List<T> has neither _size nor _count"); return -1; }
                _listSizeOffset = size.Offset;
            }

            int count = _hook.ReadInt32(list + _listSizeOffset);
            return count < 0 || count > 1000000 ? -1 : count;
        }

        private long _runsListSlot;
        private int _runsFieldOffset = -1;
        private int _listSizeOffset = -1;

        private byte[] _gameManagerBuffer;
        private byte[] _uiManagerBuffer;

        public void Dispose() => _hook.Dispose();
    }
}
