using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using InfiniteSplitter;
using InfiniteSplitter.Game;
using InfiniteSplitter.Memory;
using InfiniteSplitter.Mono;
using InfiniteSplitter.Core;

namespace InfiniteSplitter.Tools
{
    /// <summary>
    /// The live modes, so the memory layer can be brought up and debugged with
    /// LiveSplit entirely out of the picture.
    /// </summary>
    public static class Attach
    {
        public static int Run(string[] args)
        {
            using var game = new Infinitevania();

            Console.Write("attaching to " + Infinitevania.ProcessName + " ");
            Result refresh = default;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                refresh = game.TryRefresh();
                if (refresh.IsOk) break;
                Console.Write(".");
                System.Threading.Thread.Sleep(500);
            }
            Console.WriteLine();

            if (refresh.IsErr)
            {
                Console.Error.WriteLine("FAILED: " + refresh.Error.Describe());
                Console.Error.WriteLine("        " + refresh.Error);
                return 1;
            }

            if (Args.Has(args, "--watch")) return Watch(game);

            if (Args.Has(args, "--ids")) return Ids(game);

            if (Args.Has(args, "--find-scene")) return SceneFinder.Run(game, args);

            if (Args.Has(args, "--watch-scene")) return SceneFinder.WatchChains(game, args);

            string expression = Args.Value(args, "--eval");
            if (expression != null) return Eval(game, expression);

            return Dump(game);
        }

        /// <summary>Everything the resolver worked out — see docs/testing.md.</summary>
        private static int Dump(Infinitevania game)
        {
            var runtime = game.Runtime;
            var sb = new StringBuilder();

            sb.AppendLine("# infinite-splitter out-of-process dump");
            sb.AppendLine("pid                     = " + game.ProcessId);
            sb.AppendLine("mono_module             = " + Hex(game.MonoModuleBase));
            sb.AppendLine("mono_exports            = " + runtime.Exports.Count);
            sb.AppendLine("root_domain             = " + Hex(runtime.RootDomain));
            sb.AppendLine("domain_id               = " + runtime.DomainId);
            sb.AppendLine("assembly                = " + Hex(runtime.Assembly));
            sb.AppendLine("image                   = " + Hex(runtime.Image));
            sb.AppendLine("image_name              = " + runtime.ImageName);
            sb.AppendLine("image_file              = " + runtime.ImageFileName);
            sb.AppendLine("class_count             = " + runtime.ClassCount);
            sb.AppendLine("class_name_offset       = " + Hex(runtime.ResolvedClassNameOffset));
            sb.AppendLine("class_cache_offset      = " + Hex(runtime.ResolvedClassCacheOffset));
            sb.AppendLine();

            sb.AppendLine("## probed offsets (decoded from the loaded DLL)");
            sb.AppendLine("domain_id               = " + Hex(runtime.Probe.DomainId));
            sb.AppendLine("assembly_image          = " + Hex(runtime.Probe.AssemblyImage));
            sb.AppendLine("assembly_name           = " + Hex(runtime.Probe.AssemblyName));
            sb.AppendLine("image_filename          = " + Hex(runtime.Probe.ImageFileName));
            sb.AppendLine("image_assembly_name     = " + Hex(runtime.Probe.ImageAssemblyName));
            sb.AppendLine("image_assembly          = " + Hex(runtime.Probe.ImageAssembly));
            sb.AppendLine("class_image             = " + Hex(runtime.Probe.ClassImage));
            sb.AppendLine("class_nested_in         = " + Hex(runtime.Probe.ClassNestedIn));
            sb.AppendLine("class_type_token        = " + Hex(runtime.Probe.ClassTypeToken));
            sb.AppendLine("field_name              = " + Hex(runtime.Probe.FieldName));
            sb.AppendLine("field_parent            = " + Hex(runtime.Probe.FieldParent));
            if (runtime.Probe.Overrides.Count > 0)
                sb.AppendLine("OVERRIDES               = " + string.Join(", ", runtime.Probe.Overrides));
            if (runtime.Probe.Undecoded.Count > 0)
                sb.AppendLine("undecoded               = " + string.Join(", ", runtime.Probe.Undecoded));
            sb.AppendLine();

            foreach (var note in game.Notes) sb.AppendLine("note: " + note);
            if (game.Notes.Count > 0) sb.AppendLine();

            DumpClass(sb, "GameManager", game.GameManager, game.GameManagerInstanceSlot, game.ReadGameManagerInstance());
            DumpClass(sb, "UIManager", game.UIManager, game.UIManagerInstanceSlot, game.ReadUIManagerInstance());
            DumpClass(sb, "RunLogger", game.RunLogger, 0, 0);
            DumpClass(sb, "Menu", game.Menu, 0, 0);

            // Live values, so the dump doubles as a smoke test that the offsets point at
            // something real rather than merely resolving.
            long instance = game.ReadGameManagerInstance();
            if (instance != 0 && game.GameManager != null)
            {
                sb.AppendLine("## live GameManager values");
                foreach (var name in new[]
                         {
                             "hourCount1", "minutesCount1", "secondsCount1",
                             "canSave1", "canSave2", "canSave3", "canSave4", "canSave5",
                             "timePaused", "loading", "sword", "elderBestiary",
                             "souls", "heartFragments", "maxHealth", "currentScene",
                         })
                {
                    var field = game.GameManager.Find(name);
                    if (field == null) { sb.AppendLine($"{name,-24}= <no such field>"); continue; }
                    sb.AppendLine($"{name,-24}= {ReadGuess(game.Memory, instance + field.Offset, name)}  (+0x{field.Offset:X})");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("## live GameManager values");
                sb.AppendLine("GameManager.instance is null — load a save or start a new game, then re-run.");
                sb.AppendLine();
            }

            Console.Write(sb.ToString());
            return 0;
        }

        private static void DumpClass(StringBuilder sb, string label, MonoClassRef klass, long slot, long instance)
        {
            sb.AppendLine("## " + label);
            if (klass == null) { sb.AppendLine("unavailable"); sb.AppendLine(); return; }

            sb.AppendLine("class                   = " + Hex(klass.Address));
            sb.AppendLine("vtable                  = " + Hex(klass.VTable));
            sb.AppendLine("static_data             = " + Hex(klass.StaticData));
            sb.AppendLine("field_count             = " + klass.FieldList.Count);
            if (slot != 0)
            {
                sb.AppendLine("instance_slot           = " + Hex(slot));
                sb.AppendLine("instance                = " + Hex(instance));
            }

            foreach (var field in klass.FieldList)
                sb.AppendLine($"  field {field.Name,-40} +0x{field.Offset:X}");
            sb.AppendLine();
        }

        /// <summary>Best-effort rendering by field name, enough to eyeball that the
        /// offsets are right.</summary>
        private static string ReadGuess(IMemory memory, long address, string name)
        {
            if (name.StartsWith("seconds", StringComparison.Ordinal))
                return memory.ReadSingle(address).ToString("0.###", CultureInfo.InvariantCulture);
            if (name.StartsWith("canSave", StringComparison.Ordinal) ||
                name == "timePaused" || name == "loading" || name == "sword" || name.EndsWith("Bestiary", StringComparison.Ordinal))
                return memory.ReadBool(address).ToString();
            if (name == "currentScene")
                return "\"" + MonoReader.ReadString(memory, memory.ReadPointer(address), 512) + "\"";
            return memory.ReadInt32(address).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A live table at 4 Hz — how the IGT is checked: open the game's pause menu and
        /// the number here must match what it shows, digit for digit.
        /// </summary>
        private static int Watch(Infinitevania game)
        {
            var igt = new IgtSource();
            var detector = new NewGameDetector();
            Console.WriteLine("watching — Ctrl+C to stop");
            Console.WriteLine();
            Console.WriteLine($"{"igt",-14} {"raw",-14} {"cut",-14} {"slot",4} {"timePaused",-11} " +
                              $"{"loading",-8} {"sceneLoad",-10} {"uiPaused",-9} {"runs",5} {"rate",6}  scene");
            Console.WriteLine("  (rate is igt seconds per wall second — should sit at 1.000 while playing)");
            Console.WriteLine("  (cut is the loading time removed so far, so raw - cut = igt)");

            int lastRunCount = -1;
            bool explainedRunCount = false;
            double igtAtStart = -1;
            var wall = System.Diagnostics.Stopwatch.StartNew();

            while (true)
            {
                var refresh = game.TryRefresh();
                if (refresh.IsErr)
                {
                    Console.WriteLine("  " + refresh.Error.Describe());
                    System.Threading.Thread.Sleep(1000);
                    continue;
                }

                game.TakeSnapshot();
                var snapshot = game.Snapshot;
                var fields = game.Fields;

                if (!snapshot.GameManagerAlive)
                {
                    Console.Write("\r  GameManager.instance is null (title screen?)                    ");
                    System.Threading.Thread.Sleep(250);
                    continue;
                }

                igt.Update(snapshot, fields);
                if (igtAtStart < 0 && igt.Value > 0) { igtAtStart = igt.Value; wall.Restart(); }

                double rate = wall.Elapsed.TotalSeconds > 1 && igtAtStart >= 0
                    ? (igt.Value - igtAtStart) / wall.Elapsed.TotalSeconds
                    : 0;

                bool timePaused = fields.TimePaused != null && snapshot.Bool(fields.TimePaused);
                bool loading = fields.Loading != null && snapshot.Bool(fields.Loading);
                bool uiPaused = fields.UiPaused != null && snapshot.UIManagerAlive && snapshot.BoolUI(fields.UiPaused);

                string native = game.Scene.Read(game.Memory);
                string sceneShown;
                if (native.Length > 0)
                {
                    sceneShown = native + " (native)";
                }
                else
                {
                    string scene = fields.CurrentScene == null
                        ? "?"
                        : MonoReader.ReadString(game.Memory, snapshot.Pointer(fields.CurrentScene));
                    sceneShown = timePaused ? "menu (exact)" : scene + " (approx)";
                }

                detector.Update(snapshot.RunCount, timePaused, loading, snapshot.GameManagerAlive);

                Console.Write($"\r{igt.Format(),-14} {Trigger.FormatTime(igt.Raw),-14} " +
                              $"{Trigger.FormatTime(igt.Excluded),-14} {igt.ActiveSlot,4} " +
                              $"{timePaused,-11} {loading,-8} {snapshot.SceneLoadInFlight,-10} {uiPaused,-9} " +
                              $"{snapshot.RunCount,5}  {rate,6:0.000}  {sceneShown,-24}");

                if (detector.Pulse)
                    Console.WriteLine($"\n  *** new game detected ({detector.Source}) ***");
                else if (snapshot.RunCount != lastRunCount && lastRunCount >= 0)
                    Console.WriteLine($"\n  *** run count {lastRunCount} -> {snapshot.RunCount} " +
                                      $"(the game logged a run; detector says '{detector.Source}') ***");

                // runs == -1 is a state, not a fault, and nothing depends on it: the
                // column is here to cross-check the detector, not to feed it.
                if (snapshot.RunCount < 0 && !explainedRunCount)
                {
                    Console.WriteLine("\n  note: runs = -1 — the game has not logged a run this session. " +
                                      "New-game detection does not use it.");
                    explainedRunCount = true;
                }
                lastRunCount = snapshot.RunCount;

                System.Threading.Thread.Sleep(250);
            }
        }

        /// <summary>
        /// Evaluates a trigger expression against the running game — how a route is
        /// authored: write the condition, stand in front of the boss, watch it flip.
        /// </summary>
        private static int Eval(Infinitevania game, string expression)
        {
            var errors = new List<string>();
            var trigger = Trigger.Parse(expression, errors);
            if (trigger == null || errors.Count > 0)
            {
                foreach (var error in errors) Console.Error.WriteLine("ERROR " + error);
                return 1;
            }

            var probe = new MemoryProbe(game) { Log = m => Console.WriteLine("  " + m) };
            var igt = new IgtSource();

            Console.WriteLine($"evaluating: {trigger.Source}");
            Console.WriteLine("  (Ctrl+C to stop; the value is re-evaluated 4 times a second)");
            Console.WriteLine();

            bool? previous = null;
            while (true)
            {
                if (game.TryRefresh().IsErr) { System.Threading.Thread.Sleep(1000); continue; }

                game.TakeSnapshot();
                igt.Update(game.Snapshot, game.Fields);
                probe.NewGameDetector.Update(game.Snapshot.RunCount,
                    game.Fields.TimePaused != null && game.Snapshot.Bool(game.Fields.TimePaused),
                    game.Fields.Loading != null && game.Snapshot.Bool(game.Fields.Loading),
                    game.Snapshot.GameManagerAlive);
                probe.Bind(game.Snapshot, game.Fields, igt.Value);

                bool value = trigger.Eval(probe);
                if (previous != value)
                {
                    Console.WriteLine($"  {igt.Format()}  {(value ? "TRUE " : "false")}  " +
                                      $"scene={probe.Scene}");
                    previous = value;
                }
                System.Threading.Thread.Sleep(250);
            }
        }

        /// <summary>
        /// Every collectible and activation id the save currently holds — how to find
        /// out what to write in a `collectible:` or `activation:` trigger.
        /// </summary>
        private static int Ids(Infinitevania game)
        {
            var probe = new MemoryProbe(game) { Log = m => Console.Error.WriteLine("  " + m) };
            var igt = new IgtSource();

            game.TakeSnapshot();
            igt.Update(game.Snapshot, game.Fields);
            probe.Bind(game.Snapshot, game.Fields, igt.Value);

            if (!game.Snapshot.GameManagerAlive)
            {
                Console.Error.WriteLine("GameManager.instance is null — load a save first.");
                return 1;
            }

            Console.WriteLine(probe.DescribeSets());
            Console.WriteLine();
            Console.WriteLine($"collected collectibles ({probe.CollectedCount}):");
            foreach (var id in probe.CollectedIds) Console.WriteLine("  " + id);
            Console.WriteLine();
            Console.WriteLine($"persistent activations ({probe.ActivatedCount}):");
            foreach (var id in probe.ActivatedIds) Console.WriteLine("  " + id);
            return 0;
        }

        private static string Hex(long value) => "0x" + value.ToString("X");
        private static string Hex(int value) => "0x" + value.ToString("X");
    }
}
