using System;
using System.Collections.Generic;
using System.Linq;
using InfiniteSplitter.Diagnostics;
using InfiniteSplitter.Game;
using InfiniteSplitter.Memory;
using InfiniteSplitter.Mono;

namespace InfiniteSplitter.Tools
{
    /// <summary>
    /// Rebuilds the active-scene pointer chain after a Unity update, when the compiled-in
    /// one in NativeScene stops resolving. It locates the scene name's bytes, finds what
    /// points at them, repeats, and keeps only what is anchored in UnityPlayer.dll's own
    /// image — a heap address is different every launch, a module-relative one is not.
    ///
    /// Run it once per scene and intersect the results: a chain that holds in two
    /// different scenes with the same offsets is the chain, one that holds in only one
    /// is a coincidence of that session's heap. See docs/splitcheck.md.
    /// </summary>
    public static class SceneFinder
    {
        private const string UnityModule = "UnityPlayer.dll";

        /// <summary>How deep to follow pointers-to-pointers before giving up.</summary>
        private const int MaxDepth = 4;

        /// <summary>Cap per level, so a popular address cannot make the scan explode.</summary>
        private const int MaxTargetsPerLevel = 400;

        /// <summary>
        /// Resolves candidate chains continuously, printing whenever one changes, next
        /// to the managed approximation.
        ///
        /// This is the step that decides. Surviving two scans only proves a chain points
        /// at *a* loaded scene; a splitter needs the *active* one, updated at the door.
        /// Walk through a transition with this running: the real chain flips
        /// immediately, the managed field lags, and a coincidence goes stale or empty.
        /// </summary>
        public static int WatchChains(Infinitevania game, string[] args)
        {
            var unity = game.Process.FindModule(UnityModule);
            if (unity == null)
            {
                Console.Error.WriteLine($"{UnityModule} is not loaded");
                return 1;
            }

            string path = Args.Value(args, "--chains");
            if (string.IsNullOrEmpty(path))
            {
                Console.Error.WriteLine("usage: splitcheck --attach --watch-scene --chains <file>");
                Console.Error.WriteLine("       the file is the --out of a --find-scene run");
                return 2;
            }

            var chains = System.IO.File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                .Select(PointerChain.Parse)
                .Where(c => c != null)
                .ToList();

            if (chains.Count == 0)
            {
                Console.Error.WriteLine("no chains parsed from " + path);
                return 1;
            }

            Console.WriteLine($"watching {chains.Count} chain(s) against {UnityModule} @ 0x{unity.BaseAddress:X}");
            for (int i = 0; i < chains.Count; i++)
            {
                // The address each chain lands on, not just the text it reads. Two chains
                // resolving to the same address are two routes to one structure; two
                // reading the same string from different addresses are two copies, and
                // only one of those is the one the engine updates.
                long address = chains[i].Resolve(game.Memory, unity.BaseAddress);
                Console.WriteLine($"  [{i}] {chains[i]}");
                Console.WriteLine($"       -> 0x{address:X}  \"{chains[i].ResolveAscii(game.Memory, unity.BaseAddress)}\"");
            }
            Console.WriteLine();
            Console.WriteLine("Walk through a scene transition. The chain that flips AT the door is the one.");
            Console.WriteLine("'managed' is the current approximation — it should visibly lag.");
            Console.WriteLine();
            Console.WriteLine($"{"igt",-13} {"managed",-12} native");

            var igt = new IgtSource();
            string previous = null;

            while (true)
            {
                if (game.TryRefresh().IsErr) { System.Threading.Thread.Sleep(1000); continue; }

                game.TakeSnapshot();
                igt.Update(game.Snapshot, game.Fields);

                string managed = "?";
                if (game.Snapshot.GameManagerAlive && game.Fields.CurrentScene != null)
                {
                    managed = MonoReader.ReadString(game.Memory, game.Snapshot.Pointer(game.Fields.CurrentScene));
                    if (game.Fields.TimePaused != null && game.Snapshot.Bool(game.Fields.TimePaused)) managed = "menu";
                }

                var values = chains
                    .Select(c => ShortName(c.ResolveAscii(game.Memory, unity.BaseAddress)))
                    .ToList();

                string line = $"{managed,-12} " + string.Join(" ", values.Select((v, i) => $"[{i}]{v}"));
                if (line != previous)
                {
                    Console.WriteLine($"{igt.Format(),-13} {line}");
                    previous = line;
                }
                System.Threading.Thread.Sleep(200);
            }
        }

        /// <summary>"Assets/Scenes/volcano.unity" -> "volcano". Anything else is shown
        /// as-is, so a chain that has gone stale reveals what it actually holds.</summary>
        private static string ShortName(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return "-";
            int slash = assetPath.LastIndexOf('/');
            string name = slash >= 0 ? assetPath.Substring(slash + 1) : assetPath;
            return name.EndsWith(".unity", StringComparison.Ordinal)
                ? name.Substring(0, name.Length - ".unity".Length)
                : "?" + name;
        }

        public static int Run(Infinitevania game, string[] args)
        {
            string scene = Args.Value(args, "--find-scene");
            if (string.IsNullOrEmpty(scene))
            {
                Console.Error.WriteLine("usage: splitcheck --attach --find-scene <sceneName> [--out <file>]");
                Console.Error.WriteLine("       the scene must be the one currently loaded, e.g. forest");
                return 2;
            }

            var process = game.Process;
            var regions = process.Regions();
            if (regions.Count == 0)
            {
                Console.Error.WriteLine("no readable regions — VirtualQueryEx returned nothing");
                return 1;
            }

            var unity = process.FindModule(UnityModule);
            if (unity == null)
                Console.WriteLine($"warning: {UnityModule} is not loaded; chains cannot be anchored");

            var scanner = new PointerScanner(game.Memory, regions);

            Console.WriteLine($"# scene chain scan for \"{scene}\"");
            Console.WriteLine($"regions        = {regions.Count} ({scanner.TotalBytes / (1024 * 1024)} MB readable)");
            if (unity != null)
                Console.WriteLine($"{UnityModule,-14} = 0x{unity.BaseAddress:X} + {unity.Size / 1024} KB");
            Console.WriteLine($"mono module    = 0x{game.MonoModuleBase:X}   (managed strings live past it, and are UTF-16)");
            Console.WriteLine();

            // Unity's core::string is 8-bit. The full asset path is the most specific
            // needle available; the bare name is the fallback, since a short
            // core::string is stored inline in its owner rather than heap-allocated.
            var needles = new (string Label, byte[] Bytes)[]
            {
                ("path ascii", PointerScanner.AsciiZ($"Assets/Scenes/{scene}.unity")),
                ("name ascii", PointerScanner.AsciiZ(scene)),
                ("name utf16", PointerScanner.Utf16(scene)),
            };

            var byLabel = new Dictionary<string, List<long>>();
            foreach (var needle in needles)
            {
                var hits = scanner.FindBytes(needle.Bytes, limit: 2048);
                byLabel[needle.Label] = hits;
                Console.WriteLine($"{needle.Label,-12} {hits.Count,6} hit(s)   {Summarize(hits, scanner, unity)}");
            }
            Console.WriteLine();

            // Only ONE needle feeds the pointer walk, the most specific one that hit.
            // The asset path occurs a couple of times; the bare scene name occurs in the
            // hundreds ("forest" is a substring of half the level's texture and prefab
            // names) and walking those buries the real chain in coincidences. UTF-16
            // hits are never followed: those are the managed strings being replaced.
            var stringHits = byLabel["path ascii"];
            string used = "path ascii";
            if (stringHits.Count == 0)
            {
                stringHits = byLabel["name ascii"];
                used = "name ascii";
                Console.WriteLine("note: the asset path was not found; falling back to the bare name, " +
                                  "which is far noisier.");
            }

            if (stringHits.Count == 0)
            {
                Console.WriteLine("no 8-bit occurrence of the scene name — is that scene actually loaded?");
                return 1;
            }
            Console.WriteLine($"walking from the {stringHits.Count} \"{used}\" hit(s).");
            Console.WriteLine();

            var anchored = new List<Chain>();
            var targets = Dedup(stringHits).Take(MaxTargetsPerLevel).ToList();

            // Every level is kept, not just the last: reconstructing a chain means
            // walking all the way back to the string, and a hit only knows its own
            // parent's address. Keeping one previous level truncates every chain to two
            // hops and invents repeats where the walk re-enters a seen address.
            var levels = new List<Dictionary<long, PointerHit>>();

            for (int depth = 1; depth <= MaxDepth && targets.Count > 0; depth++)
            {
                var hits = scanner.FindPointersTo(targets, maxOffset: 0x200);
                Console.WriteLine($"depth {depth}: {targets.Count,5} target(s) -> {hits.Count,6} pointer(s)   " +
                                  $"{CountByKind(hits)}");

                var next = new Dictionary<long, PointerHit>();
                foreach (var hit in hits)
                {
                    if (!next.ContainsKey(hit.At)) next[hit.At] = hit;

                    // Anchored in UnityPlayer's image: a candidate root.
                    if (unity != null && hit.At >= unity.BaseAddress && hit.At < unity.BaseAddress + unity.Size)
                        anchored.Add(BuildChain(hit, levels, unity, depth));
                }

                levels.Add(next);
                targets = next.Keys.Take(MaxTargetsPerLevel).ToList();
            }
            Console.WriteLine();

            // Every chain is walked again from the module base and must read back the
            // path we searched for. That is what catches a chain reconstructed wrongly,
            // and what turns the scan from "here are some addresses" into a result.
            string expected = $"Assets/Scenes/{scene}.unity";
            var verified = new List<Chain>();
            foreach (var chain in anchored)
            {
                string read = chain.ToPointerChain().ResolveAscii(game.Memory, unity.BaseAddress);
                chain.Reads = read;
                if (string.Equals(read, expected, StringComparison.Ordinal)) verified.Add(chain);
            }

            Console.WriteLine($"verified: {verified.Count} of {anchored.Count} chain(s) re-read \"{expected}\"");
            Console.WriteLine();
            anchored = verified;

            if (anchored.Count == 0)
            {
                Console.WriteLine($"no chain reached {UnityModule}'s image within {MaxDepth} levels.");
                Console.WriteLine("The root may be a heap singleton rather than a static, or deeper than the cap.");
                Console.WriteLine("Raise MaxDepth, or scan for the SceneManager singleton instead of the name.");
                return 1;
            }

            Console.WriteLine($"## {anchored.Count} chain(s) anchored in {UnityModule}");
            Console.WriteLine("Re-run this in a different scene; only chains printed BOTH times are real.");
            Console.WriteLine();
            foreach (var chain in anchored.OrderBy(c => c.Offsets.Count).ThenBy(c => c.Root))
                Console.WriteLine("  " + chain);

            string outPath = Args.Value(args, "--out");
            if (!string.IsNullOrEmpty(outPath))
            {
                System.IO.File.WriteAllLines(outPath, anchored.Select(c => c.ToString()));
                Console.WriteLine();
                Console.WriteLine($"written to {outPath} — diff it against another scene's run");
            }
            return 0;
        }

        /// <summary>
        /// Walks the recorded levels back from an anchored pointer to the string,
        /// turning absolute addresses into the module-relative root plus the offsets
        /// applied at each dereference. Only that form survives a restart.
        /// </summary>
        private static Chain BuildChain(PointerHit hit, List<Dictionary<long, PointerHit>> levels,
                                        ModuleInfo unity, int depth)
        {
            // The hit's own offset is the dereference nearest the root and the string is
            // at the far end, so the list is built back to front.
            var offsets = new List<int> { hit.Offset };
            var current = hit;

            // A hit at depth d points at an address recorded in levels[d-2]. One level
            // per step, never sideways: that is what keeps a chain from re-entering an
            // address it already passed through.
            for (int level = depth - 2; level >= 0; level--)
            {
                if (!levels[level].TryGetValue(current.Target, out var parent)) break;
                offsets.Add(parent.Offset);
                current = parent;
            }

            return new Chain
            {
                Root = hit.At - unity.BaseAddress,
                Offsets = offsets,
                Depth = depth,
                Leaf = current.Target,
            };
        }

        private sealed class Chain
        {
            public long Root;
            public List<int> Offsets = new List<int>();
            public int Depth;
            public long Leaf;

            /// <summary>What walking the chain actually read back, once verified.</summary>
            public string Reads;

            public PointerChain ToPointerChain() => new PointerChain(UnityModule, Root, Offsets);

            /// <summary>The offsets alone, which is what has to match across two scenes:
            /// the root moves with the module, the shape of the walk does not.</summary>
            public string Shape =>
                $"+0x{Root:X}" + string.Concat(Offsets.Select(o => $" -> +0x{o:X}"));

            public override string ToString() => ToPointerChain().ToString();
        }

        private static string Summarize(List<long> hits, PointerScanner scanner, ModuleInfo unity)
        {
            if (hits.Count == 0) return "";

            int inUnity = 0, image = 0, priv = 0, mapped = 0;
            foreach (var hit in hits)
            {
                if (unity != null && hit >= unity.BaseAddress && hit < unity.BaseAddress + unity.Size) inUnity++;
                var region = scanner.RegionOf(hit);
                if (region == null) continue;
                if (region.Kind == MemoryRegionKind.Image) image++;
                else if (region.Kind == MemoryRegionKind.Private) priv++;
                else if (region.Kind == MemoryRegionKind.Mapped) mapped++;
            }
            return $"[unity {inUnity}, image {image}, private {priv}, mapped {mapped}]";
        }

        private static string CountByKind(List<PointerHit> hits)
        {
            int image = hits.Count(h => h.Region?.Kind == MemoryRegionKind.Image);
            int priv = hits.Count(h => h.Region?.Kind == MemoryRegionKind.Private);
            return $"[image {image}, private {priv}]";
        }

        private static List<long> Dedup(List<long> values) => values.Distinct().ToList();
    }
}
