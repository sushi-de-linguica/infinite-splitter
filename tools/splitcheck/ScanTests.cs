using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using InfiniteSplitter.Memory;
using static InfiniteSplitter.Tools.Check;

namespace InfiniteSplitter.Tools
{
    /// <summary>
    /// The pointer scanner, against a synthetic process: a fake address space in a byte
    /// array, shaped like the real thing — a static in a module's image pointing at a
    /// heap object whose field points at the scene's path string. See docs/testing.md.
    /// </summary>
    public static class ScanTests
    {
        // Addresses that look like the real ones: a module low, heap allocations high.
        private const long ModuleBase = 0x180000000;
        private const int ModuleSize = 0x2000;
        private const long ObjectBase = 0x2000000000;
        private const long StringBase = 0x3000000000;

        private const int StaticOffset = 0x800;      // UnityPlayer+0x800 holds the object
        private const int FieldOffset = 0x30;        // object+0x30 holds the string pointer

        private const string ScenePath = "Assets/Scenes/forest.unity";

        public static int Run()
        {
            int bad = 0;
            Console.WriteLine("scan");

            bad += FindsTheString();
            bad += FindsAPointerToIt();
            bad += WalksBackToTheModuleStatic();
            bad += FindsNothingForAnAbsentScene();
            bad += SpansChunkBoundaries();
            bad += ChainResolvesBackToTheString();
            bad += ChainSurvivesARebasedModule();
            bad += ChainRoundTripsAsText();
            bad += BrokenChainResolvesToNothing();
            bad += SceneNamesComeOutOfAssetPaths();
            bad += GarbageIsNotMistakenForAScene();
            bad += AFailedReadZeroesTheBuffer();

            Console.WriteLine(bad == 0 ? "  ok" : $"  {bad} failure(s)");
            Console.WriteLine();
            return bad;
        }

        private static int FindsTheString()
        {
            var (scanner, _) = Build();
            var hits = scanner.FindBytes(PointerScanner.AsciiZ(ScenePath));

            if (hits.Count != 1) return Fail($"expected 1 hit for the scene path, got {hits.Count}");
            if (hits[0] != StringBase) return Fail($"string found at 0x{hits[0]:X}, expected 0x{StringBase:X}");
            return 0;
        }

        private static int FindsAPointerToIt()
        {
            var (scanner, _) = Build();
            var hits = scanner.FindPointersTo(new[] { StringBase });

            var direct = hits.FirstOrDefault(h => h.At == ObjectBase + FieldOffset);
            if (direct == null) return Fail("no pointer found at the object's string field");
            if (direct.Offset != 0) return Fail($"direct pointer reported offset 0x{direct.Offset:X}, expected 0");
            return 0;
        }

        /// <summary>
        /// The point of the whole exercise: the static holds the object's head, not the
        /// field, so the second level only connects if the scan tolerates an offset.
        /// With maxOffset at 0 this test fails.
        /// </summary>
        private static int WalksBackToTheModuleStatic()
        {
            var (scanner, _) = Build();

            var level1 = scanner.FindPointersTo(new[] { StringBase });
            var level2 = scanner.FindPointersTo(level1.Select(h => h.At).ToList());

            var root = level2.FirstOrDefault(h => h.At == ModuleBase + StaticOffset);
            if (root == null) return Fail("the module static was not reached from the string");

            if (root.Value != ObjectBase)
                return Fail($"root holds 0x{root.Value:X}, expected the object head 0x{ObjectBase:X}");
            if (root.Offset != FieldOffset)
                return Fail($"root reported offset 0x{root.Offset:X}, expected 0x{FieldOffset:X}");
            if (root.Region == null || root.Region.Kind != MemoryRegionKind.Image)
                return Fail("the root was not attributed to an image region");
            return 0;
        }

        private static int FindsNothingForAnAbsentScene()
        {
            var (scanner, _) = Build();
            var hits = scanner.FindBytes(PointerScanner.AsciiZ("Assets/Scenes/volcano.unity"));
            return hits.Count == 0 ? 0 : Fail($"found {hits.Count} hit(s) for a scene that is not loaded");
        }

        /// <summary>
        /// The scanner reads in 1 MB chunks, overlapped by the needle length. A string
        /// straddling a boundary is the one case a naive chunked scan drops.
        /// </summary>
        private static int SpansChunkBoundaries()
        {
            const long baseAddress = 0x4000000000;
            const int size = (1 << 20) + 0x1000;
            var needle = PointerScanner.AsciiZ(ScenePath);

            // Straddle: start a few bytes before the 1 MB mark.
            int at = (1 << 20) - 5;

            var data = new byte[size];
            Array.Copy(needle, 0, data, at, needle.Length);

            var memory = new ByteArrayMemory(baseAddress, data);
            var regions = new List<MemoryRegion>
            {
                new MemoryRegion { Start = baseAddress, Size = size, Kind = MemoryRegionKind.Private },
            };

            var hits = new PointerScanner(memory, regions).FindBytes(needle);
            if (hits.Count != 1) return Fail($"chunk-straddling string: expected 1 hit, got {hits.Count}");
            if (hits[0] != baseAddress + at) return Fail("chunk-straddling string found at the wrong address");
            return 0;
        }

        /// <summary>
        /// The scan's own output, walked forwards — what turns a list of addresses into
        /// a checked result, and the same call the live finder makes before printing.
        /// </summary>
        private static int ChainResolvesBackToTheString()
        {
            var (_, memory) = Build();
            var chain = new PointerChain(UnityModule, StaticOffset, new[] { FieldOffset, 0 });

            string read = chain.ResolveAscii(memory, ModuleBase);
            return read == ScenePath ? 0 : Fail($"chain resolved to \"{read}\", expected \"{ScenePath}\"");
        }

        /// <summary>
        /// The whole reason a chain is stored module-relative: ASLR puts the module
        /// somewhere else next launch and the chain has to survive it.
        /// </summary>
        private static int ChainSurvivesARebasedModule()
        {
            const long rebased = 0x7FFE00000000;

            var memory = new ByteArrayMemory();
            var module = memory.Allocate(rebased, ModuleSize);
            Array.Copy(BitConverter.GetBytes(ObjectBase), 0, module, StaticOffset, 8);

            var obj = memory.Allocate(ObjectBase, 0x100);
            Array.Copy(BitConverter.GetBytes(StringBase), 0, obj, FieldOffset, 8);

            var text = memory.Allocate(StringBase, 0x40);
            var bytes = Encoding.ASCII.GetBytes(ScenePath);
            Array.Copy(bytes, text, bytes.Length);

            var chain = new PointerChain(UnityModule, StaticOffset, new[] { FieldOffset, 0 });
            string read = chain.ResolveAscii(memory, rebased);
            return read == ScenePath ? 0 : Fail($"rebased chain resolved to \"{read}\"");
        }

        private static int ChainRoundTripsAsText()
        {
            var original = new PointerChain(UnityModule, 0x1C41880, new[] { 0x40, 0x10, 0x0 });
            var parsed = PointerChain.Parse(original.ToString());

            if (parsed == null) return Fail("a chain did not parse back from its own text");
            if (parsed.ToString() != original.ToString())
                return Fail($"round trip changed \"{original}\" into \"{parsed}\"");
            return 0;
        }

        /// <summary>
        /// A null link is an ordinary state — the scene is mid-load, the singleton is
        /// not up — and must read as "nothing", never as an exception or a stale answer.
        /// </summary>
        private static int BrokenChainResolvesToNothing()
        {
            var (_, memory) = Build();

            // An offset that lands on a region full of zeros.
            var broken = new PointerChain(UnityModule, StaticOffset + 0x100, new[] { FieldOffset, 0 });
            if (broken.Resolve(memory, ModuleBase) != 0) return Fail("a null root did not resolve to 0");
            if (broken.ResolveAscii(memory, ModuleBase) != "") return Fail("a null root produced a string");

            // And a module base of zero, which is what an unloaded module looks like.
            var good = new PointerChain(UnityModule, StaticOffset, new[] { FieldOffset, 0 });
            if (good.Resolve(memory, 0) != 0) return Fail("a missing module did not resolve to 0");
            return 0;
        }

        /// <summary>
        /// The scenes as the game names them. "castle initial" has a space, so the name
        /// check cannot be a plain identifier test — that is the case worth pinning.
        /// </summary>
        private static int SceneNamesComeOutOfAssetPaths()
        {
            var cases = new[]
            {
                ("Assets/Scenes/forest.unity", "forest"),
                ("Assets/Scenes/volcano.unity", "volcano"),
                ("Assets/Scenes/castle.unity", "castle"),
                ("Assets/Scenes/castle initial.unity", "castle initial"),
                ("Assets/Scenes/lobby.unity", "lobby"),
                ("Assets/Scenes/menu.unity", "menu"),
            };

            foreach (var (path, expected) in cases)
            {
                string actual = InvokeNameFrom(path);
                if (actual != expected) return Fail($"\"{path}\" gave \"{actual}\", expected \"{expected}\"");
            }
            return 0;
        }

        /// <summary>
        /// A wrong chain lands on arbitrary bytes, and every one of these must read as
        /// "no scene" so the caller falls back instead of splitting on noise.
        /// </summary>
        private static int GarbageIsNotMistakenForAScene()
        {
            var rejected = new[]
            {
                "",
                "forest",                              // a bare name, not an asset path
                "Assets/Scenes/forest",                // no extension
                "forest.unity",                        // no Assets/ segment
                "Library/metadata/ab/whatever.unity",  // an asset path, but not a scene tree
                "Assets/Scenes/.unity",    // control bytes where a name should be
            };

            foreach (var text in rejected)
            {
                string actual = InvokeNameFrom(text);
                if (actual.Length != 0) return Fail($"\"{text}\" was accepted as scene \"{actual}\"");
            }
            return 0;
        }

        /// <summary>
        /// The snapshot reuses its buffers between ticks, so a read that fails must
        /// leave zeros rather than last tick's bytes. Stale state read as current is
        /// exactly how a split fires on nothing — the one failure this project refuses.
        /// </summary>
        private static int AFailedReadZeroesTheBuffer()
        {
            var (_, memory) = Build();

            var buffer = new byte[64];
            for (int i = 0; i < buffer.Length; i++) buffer[i] = 0xCD;   // last tick's contents

            int bad = Expect(!memory.TryReadInto(0x7F0000000000, buffer, buffer.Length),
                "an unmapped address reports failure");

            foreach (byte b in buffer)
                if (b != 0) return bad + Fail("a failed read left stale bytes in the buffer");

            // And a read that succeeds still fills it.
            bad += Expect(memory.TryReadInto(StringBase, buffer, buffer.Length), "a mapped address reads");
            bad += Expect(buffer[0] == (byte)'A', $"the buffer holds the scene path, got 0x{buffer[0]:X2}");
            return bad;
        }

        /// <summary>NameFrom is internal; splitcheck reaches it through the assembly's
        /// InternalsVisibleTo, the same way it reaches GameSnapshot's setters.</summary>
        private static string InvokeNameFrom(string path) =>
            InfiniteSplitter.Game.NativeScene.NameFrom(path);

        private const string UnityModule = "UnityPlayer.dll";

        /// <summary>
        /// module image  [ModuleBase .. +0x2000)   static at +0x800 -> ObjectBase
        /// heap          [ObjectBase .. +0x100)    field  at +0x30  -> StringBase
        /// heap          [StringBase .. +0x40)     "Assets/Scenes/forest.unity\0"
        /// </summary>
        private static (PointerScanner, ByteArrayMemory) Build()
        {
            var memory = new ByteArrayMemory();

            var module = memory.Allocate(ModuleBase, ModuleSize);
            Array.Copy(BitConverter.GetBytes(ObjectBase), 0, module, StaticOffset, 8);

            var obj = memory.Allocate(ObjectBase, 0x100);
            Array.Copy(BitConverter.GetBytes(StringBase), 0, obj, FieldOffset, 8);

            var text = memory.Allocate(StringBase, 0x40);
            var bytes = Encoding.ASCII.GetBytes(ScenePath);
            Array.Copy(bytes, text, bytes.Length);

            var regions = new List<MemoryRegion>
            {
                new MemoryRegion { Start = ModuleBase, Size = ModuleSize, Kind = MemoryRegionKind.Image },
                new MemoryRegion { Start = ObjectBase, Size = 0x100, Kind = MemoryRegionKind.Private },
                new MemoryRegion { Start = StringBase, Size = 0x40, Kind = MemoryRegionKind.Private },
            };

            return (new PointerScanner(memory, regions), memory);
        }
    }
}
