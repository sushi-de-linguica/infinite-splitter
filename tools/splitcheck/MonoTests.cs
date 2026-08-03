using System;
using System.Collections.Generic;
using InfiniteSplitter;
using InfiniteSplitter.Memory;
using InfiniteSplitter.Mono;
using static InfiniteSplitter.Tools.Check;

namespace InfiniteSplitter.Tools
{
    /// <summary>
    /// The Mono resolver, against the synthetic heap in <see cref="MonoHeapBuilder"/>.
    /// Runs on Linux, needs no game. See docs/testing.md.
    /// </summary>
    public static class MonoTests
    {
        public static int Run()
        {
            int bad = 0;
            Console.WriteLine("mono");

            bad += OffsetProbeDecoding();
            bad += ExportParsing();
            bad += ResolveAgainstSyntheticHeap();
            bad += RelocatedClassCacheIsFound();
            bad += MissingClassCacheFailsLoudly();
            bad += NameOffsetIsRecovered();

            Console.WriteLine(bad == 0 ? "  ok" : $"  {bad} failure(s)");
            Console.WriteLine();
            return bad;
        }

        private static int OffsetProbeDecoding()
        {
            int bad = 0;

            bad += ExpectDecode(new byte[] { 0x48, 0x8B, 0x41, 0x30, 0xC3 }, 0x30, "mov rax,[rcx+disp8]");
            bad += ExpectDecode(new byte[] { 0x48, 0x8B, 0x81, 0xC0, 0x04, 0x00, 0x00, 0xC3 }, 0x4C0, "mov rax,[rcx+disp32]");
            bad += ExpectDecode(new byte[] { 0x8B, 0x41, 0x58, 0xC3 }, 0x58, "mov eax,[rcx+disp8]");
            bad += ExpectDecode(new byte[] { 0x8B, 0x81, 0x94, 0x00, 0x00, 0x00, 0xC3 }, 0x94, "mov eax,[rcx+disp32]");
            bad += ExpectDecode(new byte[] { 0x48, 0x8D, 0x41, 0x10, 0xC3 }, 0x10, "lea rax,[rcx+disp8]");
            bad += ExpectDecode(new byte[] { 0x0F, 0xB6, 0x41, 0x1A, 0xC3 }, 0x1A, "movzx eax,byte");
            bad += ExpectDecode(new byte[] { 0x0F, 0xB7, 0x41, 0x08, 0xC3 }, 0x08, "movzx eax,word");

            // A real function prologue must NOT decode as an accessor, or a large
            // function would silently hand back a garbage offset.
            if (MonoOffsetProbe.TryDecodeAccessor(new byte[] { 0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83 }, out int wrong))
            {
                Console.WriteLine($"  ERROR a function prologue decoded as an accessor (0x{wrong:X})");
                bad++;
            }

            // The exact bytes of the shipped mono_get_root_domain.
            var body = new byte[] { 0x48, 0x8B, 0x05, 0x69, 0x9E, 0x6A, 0x00, 0xC3 };
            const long at = 0x1800A71B0;
            if (!MonoOffsetProbe.TryDecodeRipRelative(body, at, out long slot) || slot != at + 7 + 0x6A9E69)
            {
                Console.WriteLine($"  ERROR rip-relative decode gave 0x{slot:X}, want 0x{at + 7 + 0x6A9E69:X}");
                bad++;
            }

            return bad;
        }

        private static int ExpectDecode(byte[] body, int want, string what)
        {
            if (MonoOffsetProbe.TryDecodeAccessor(body, out int got) && got == want) return 0;
            Console.WriteLine($"  ERROR {what}: got 0x{got:X}, want 0x{want:X}");
            return 1;
        }

        private static int ExportParsing()
        {
            var memory = new MonoHeapBuilder().Build();
            var exports = PeExports.Parse(memory, MonoHeapBuilder.ModuleBase);

            if (exports.IsErr)
            {
                Console.WriteLine("  ERROR export parsing failed: " + exports.Error);
                return 1;
            }

            int bad = 0;
            foreach (var required in new[] { "mono_get_root_domain", "mono_class_get_image", "mono_field_get_offset_missing" })
            {
                bool present = exports.Value.ContainsKey(required);
                bool expected = required != "mono_field_get_offset_missing";
                if (present != expected)
                {
                    Console.WriteLine($"  ERROR export '{required}' present={present}, want {expected}");
                    bad++;
                }
            }
            return bad;
        }

        private static int ResolveAgainstSyntheticHeap()
        {
            var memory = new MonoHeapBuilder().Build();
            var runtime = new MonoRuntime(memory);

            var attach = runtime.Attach(MonoHeapBuilder.ModuleBase, "Assembly-CSharp");
            if (attach.IsErr)
            {
                Console.WriteLine("  ERROR attach failed: " + attach.Error);
                return 1;
            }

            int bad = 0;
            bad += Expect(runtime.RootDomain == MonoHeapBuilder.Domain, $"root domain 0x{runtime.RootDomain:X}");
            bad += Expect(runtime.DomainId == 0, "domain id " + runtime.DomainId);
            bad += Expect(runtime.Image == MonoHeapBuilder.Image, $"image 0x{runtime.Image:X}");
            bad += Expect(runtime.ImageName == "Assembly-CSharp", "image name " + runtime.ImageName);

            // Both classes must come out of the chained bucket.
            bad += Expect(runtime.ClassCount == 2 + MonoHeapBuilder.FillerCount,
                "class count " + runtime.ClassCount);

            var menu = runtime.FindClass("Menu");
            bad += Expect(menu.IsOk, "Menu was not found through next_class_cache");

            var found = runtime.FindClass("GameManager");
            if (found.IsErr)
            {
                Console.WriteLine("  ERROR GameManager not found: " + found.Error);
                return bad + 1;
            }

            var gameManager = found.Value;
            bad += Expect(gameManager.Address == MonoHeapBuilder.ClassGameManager, "class address");
            bad += Expect(gameManager.FieldList.Count == 4, "field count " + gameManager.FieldList.Count);
            bad += Expect(gameManager.Find("hourCount1")?.Offset == MonoHeapBuilder.HourCount1Offset, "hourCount1 offset");
            bad += Expect(gameManager.Find("sword")?.Offset == MonoHeapBuilder.SwordOffset, "sword offset");

            // The game really does have SorcererBestiary next to kairuzBestiary, so a
            // config may not match case exactly.
            bad += Expect(gameManager.Find("HOURCOUNT1") != null, "case-insensitive field lookup");

            // Static and instance offsets share one struct member, so mixing them up
            // would silently read whatever sits at that offset in the object.
            bad += Expect(gameManager.Find("instance").IsStatic, "instance should be static");
            bad += Expect(!gameManager.Find("hourCount1").IsStatic, "hourCount1 should not be static");
            bad += Expect(gameManager.StaticFieldAddress("hourCount1") == 0,
                "an instance field must not resolve as static storage");
            bad += Expect(gameManager.Find("hourCount1").Kind == MonoTypeKind.Int32, "hourCount1 type");
            bad += Expect(gameManager.Find("secondsCount1").Kind == MonoTypeKind.Single, "secondsCount1 type");
            bad += Expect(gameManager.Find("sword").Kind == MonoTypeKind.Boolean, "sword type");

            // The static-field formula, end to end.
            bad += Expect(gameManager.VTable == MonoHeapBuilder.VTable, "vtable");
            bad += Expect(gameManager.StaticData == MonoHeapBuilder.StaticData, $"static data 0x{gameManager.StaticData:X}");

            long instanceSlot = gameManager.StaticFieldAddress("instance");
            long instance = memory.ReadPointer(instanceSlot);
            bad += Expect(instance == MonoHeapBuilder.GameManagerObject, $"instance 0x{instance:X}");

            // And the values behind it, which is the point of the whole exercise.
            int hour = memory.ReadInt32(instance + gameManager.Find("hourCount1").Offset);
            float seconds = memory.ReadSingle(instance + gameManager.Find("secondsCount1").Offset);
            bool sword = memory.ReadBool(instance + gameManager.Find("sword").Offset);
            bad += Expect(hour == 3, "hourCount1 = " + hour);
            bad += Expect(Math.Abs(seconds - 41.5f) < 0.001, "secondsCount1 = " + seconds);
            bad += Expect(sword, "sword = " + sword);

            return bad;
        }

        /// <summary>Relocate the class-cache pointer; the resolver's search must still
        /// find every class.</summary>
        private static int RelocatedClassCacheIsFound()
        {
            const int moved = MonoOffsets.ImageClassCacheTable + 0x40;
            var builder = new MonoHeapBuilder { ClassCacheOffset = moved };
            var runtime = new MonoRuntime(builder.Build());

            var attach = runtime.Attach(MonoHeapBuilder.ModuleBase, "Assembly-CSharp");
            if (attach.IsErr)
            {
                Console.WriteLine("  ERROR a relocated class cache was not found: " + attach.Error);
                return 1;
            }

            int bad = 0;
            bad += Expect(runtime.ResolvedClassCacheOffset == moved,
                $"class cache resolved to 0x{runtime.ResolvedClassCacheOffset:X}, want 0x{moved:X}");
            bad += Expect(runtime.FindClass("GameManager").IsOk, "GameManager after the cache moved");
            return bad;
        }

        /// <summary>
        /// With no class cache to find, the resolver must say so rather than produce
        /// something plausible. The rule that matters most in this project.
        /// </summary>
        private static int MissingClassCacheFailsLoudly()
        {
            var builder = new MonoHeapBuilder { PlaceClassCachePointer = false };
            var runtime = new MonoRuntime(builder.Build());

            var attach = runtime.Attach(MonoHeapBuilder.ModuleBase, "Assembly-CSharp");
            if (attach.IsOk)
            {
                Console.WriteLine("  ERROR an image with no class cache still 'succeeded'");
                return 1;
            }
            if (attach.Error.Reason != RefreshErrorReason.LayoutMismatch)
            {
                Console.WriteLine($"  ERROR missing class cache gave {attach.Error.Reason}, want LayoutMismatch");
                return 1;
            }
            return 0;
        }

        /// <summary>
        /// MonoClass.name could not be measured offline, so the resolver searches a
        /// candidate set. Build a heap whose names live elsewhere and it must find them.
        /// </summary>
        private static int NameOffsetIsRecovered()
        {
            var memory = new MonoHeapBuilder().Build();

            // 0x30 rather than an offset the probe already owns: clobbering ClassImage
            // (0x40) would break the walk itself instead of testing the name search.
            const int relocated = 0x30;
            var all = new List<long> { MonoHeapBuilder.ClassGameManager, MonoHeapBuilder.ClassMenu };
            for (int i = 0; i < MonoHeapBuilder.FillerCount; i++)
                all.Add(MonoHeapBuilder.ClassFillerBase + i * 0x200);

            foreach (long klass in all)
            {
                long name = memory.ReadPointer(klass + MonoOffsets.ClassName);
                memory.WriteBytes(klass + relocated, BitConverter.GetBytes(name));
                memory.WriteBytes(klass + MonoOffsets.ClassName, BitConverter.GetBytes(0L));
            }

            var runtime = new MonoRuntime(memory);
            var attach = runtime.Attach(MonoHeapBuilder.ModuleBase, "Assembly-CSharp");
            if (attach.IsErr)
            {
                Console.WriteLine("  ERROR relocated MonoClass.name was not recovered: " + attach.Error);
                return 1;
            }

            int bad = 0;
            bad += Expect(runtime.ResolvedClassNameOffset == relocated,
                $"name offset resolved to 0x{runtime.ResolvedClassNameOffset:X}, want 0x{relocated:X}");
            bad += Expect(runtime.FindClass("GameManager").IsOk, "GameManager after the name offset moved");
            return bad;
        }
    }
}
