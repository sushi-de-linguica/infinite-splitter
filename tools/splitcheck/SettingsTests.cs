using System;
using System.Collections.Generic;
using System.Linq;
using InfiniteSplitter.Core;
using InfiniteSplitter.Game;
using InfiniteSplitter.Settings;
using static InfiniteSplitter.Tools.Check;

namespace InfiniteSplitter.Tools
{
    /// <summary>
    /// Settings persistence. The rule under test throughout: a document that cannot be
    /// read must leave the component working with defaults, never dead and never
    /// silently empty.
    /// </summary>
    public static class SettingsTests
    {
        public static int Run()
        {
            int bad = 0;
            Console.WriteLine("settings");

            bad += XmlRoundTrip();
            bad += CorruptDocumentFallsBack();
            bad += UnversionedDocumentIsMigrated();
            bad += IniRoundTrip();
            bad += ImportsAConfigWithUnusedSections();
            bad += RetiredFieldsInAStoredLayoutAreIgnored();
            bad += LoadRemovalIsOnByDefault();
            bad += DefaultsBuildAValidCfg();
            bad += EveryPresetParses();
            bad += ScenePresetsNameRealScenes();
            bad += StatusSurvivesADeadAttach();

            Console.WriteLine(bad == 0 ? "  ok" : $"  {bad} failure(s)");
            Console.WriteLine();
            return bad;
        }

        private static int XmlRoundTrip()
        {
            var model = new SettingsViewModel
            {
                SendIgt = false,
                ZeroAtStart = true,
                Start = "flag:sword",
                Reset = "scene:menu",
                AutoReset = false,
                Splits = new List<SplitEntry>
                {
                    new SplitEntry("A", "flag:tenrohBestiary"),
                    new SplitEntry("B", "int:heartFragments>=4 & !scene:menu"),
                },
            };

            var restored = SettingsViewModel.Deserialize(model.Serialize());

            int bad = 0;
            bad += Expect(restored.SendIgt == false, "SendIgt");
            bad += Expect(restored.ZeroAtStart, "ZeroAtStart");
            bad += Expect(restored.AutoReset == false, "AutoReset");
            bad += Expect(restored.Start == "flag:sword", "Start");
            bad += Expect(restored.Reset == "scene:menu", "Reset");
            bad += Expect(restored.Splits.Count == 2, "split count " + restored.Splits.Count);
            bad += Expect(restored.Splits[1].Trigger == "int:heartFragments>=4 & !scene:menu",
                "the second trigger survived: " + restored.Splits[1].Trigger);
            return bad;
        }

        private static int CorruptDocumentFallsBack()
        {
            int bad = 0;
            foreach (var junk in new[] { "<InfiniteSplitter", "not xml at all", "<Other><x/></Other>" })
            {
                var model = SettingsViewModel.Deserialize(junk);
                bad += Expect(model != null && model.Splits.Count > 0,
                    $"junk '{junk}' should yield working defaults");
                bad += Expect(model.LoadProblems.Count > 0, $"junk '{junk}' should say what went wrong");
            }
            return bad;
        }

        private static int UnversionedDocumentIsMigrated()
        {
            const string old = "<InfiniteSplitter><Start>newgame</Start><Splits>" +
                               "<Split name=\"Sword\" trigger=\"flag:sword\" /></Splits></InfiniteSplitter>";

            var model = SettingsViewModel.Deserialize(old);
            int bad = Expect(model.Splits.Count == 1, "split count " + model.Splits.Count);
            bad += Expect(model.Version == SettingsViewModel.CurrentVersion, "version was stamped forward");
            bad += Expect(model.LoadProblems.Any(p => p.Contains("version")), "the missing version was reported");
            return bad;
        }

        private static int IniRoundTrip()
        {
            var model = SettingsViewModel.CreateDefault();
            model.ZeroAtStart = true; model.SendIgt = false; model.Reset = "scene:menu";
            var restored = CfgBridge.FromIni(CfgBridge.ToIni(model), out var problems);

            int bad = 0;
            foreach (var problem in problems)
                if (!problem.StartsWith("warning:")) { Console.WriteLine("  ERROR ini: " + problem); bad++; }

            bad += Expect(restored.ZeroAtStart, "ZeroAtStart survived the INI round trip");
            bad += Expect(restored.SendIgt == false, "SendIgt survived");
            bad += Expect(restored.Reset == "scene:menu", "Reset survived");
            bad += Expect(restored.Splits.Count == model.Splits.Count,
                $"split count {restored.Splits.Count} vs {model.Splits.Count}");
            for (int i = 0; i < model.Splits.Count && i < restored.Splits.Count; i++)
                bad += Expect(restored.Splits[i].Name == model.Splits[i].Name &&
                              restored.Splits[i].Trigger == model.Splits[i].Trigger,
                    $"split {i + 1} survived: {restored.Splits[i]}");
            return bad;
        }

        /// <summary>A .cfg carrying sections this component does not use — and settings it
        /// no longer has — must still import cleanly, with those ignored rather than
        /// rejected. Someone's saved config predates every removal.</summary>
        private static int ImportsAConfigWithUnusedSections()
        {
            const string foreignConfig = @"
[server]
host = 127.0.0.1
port = 16834

[timer]
igt = on
slot = auto
pause_on_load = on

[run]
start = newgame
reset = never
restart_on_new_game = on

[hud]
enabled = on

[hotkeys]
reload = F10
split  = F11

[splits]
Sword          = flag:sword
Tenroh         = flag:tenrohBestiary
End            = credits
";
            var model = CfgBridge.FromIni(foreignConfig, out var problems);

            int bad = 0;
            foreach (var problem in problems)
                if (!problem.StartsWith("warning:")) { Console.WriteLine("  ERROR importing: " + problem); bad++; }

            bad += Expect(problems.Exists(p => p.Contains("pause_on_load") && p.Contains("remove_loads")),
                "the retired pause_on_load is reported and points at its replacement");
            bad += Expect(problems.Exists(p => p.Contains("slot")),
                "the retired slot is reported rather than silently dropped");
            bad += Expect(model.Reset == "never", "reset came across");
            bad += Expect(model.Splits.Count == 3, "split count " + model.Splits.Count);
            bad += Expect(model.Splits[1].Name == "Tenroh", "split names came across");
            return bad;
        }

        /// <summary>
        /// The other half of the same promise: a layout LiveSplit stored before those
        /// settings were removed still deserializes, splits and all. XmlSerializer
        /// ignores elements the type no longer has, and this is what pins that down.
        /// </summary>
        private static int RetiredFieldsInAStoredLayoutAreIgnored()
        {
            const string stored = "<InfiniteSplitter version=\"1\">" +
                                  "<SendIgt>true</SendIgt><Slot>3</Slot>" +
                                  "<PauseOnLoad>true</PauseOnLoad><PauseOnPauseMenu>true</PauseOnPauseMenu>" +
                                  "<ZeroAtStart>true</ZeroAtStart>" +
                                  "<Splits><Split name=\"Sword\" trigger=\"flag:sword\" /></Splits>" +
                                  "</InfiniteSplitter>";

            var model = SettingsViewModel.Deserialize(stored);
            int bad = Expect(model.Splits.Count == 1, "the splits survived: " + model.Splits.Count);
            bad += Expect(model.ZeroAtStart, "a setting after the removed ones still came across");

            // A setting added after the layout was stored has no element to read, so
            // it keeps its default — which is how load removal reaches an existing
            // install without a migration.
            return bad + Expect(model.RemoveLoads, "a setting the document predates keeps its default");
        }

        private static int LoadRemovalIsOnByDefault()
        {
            int bad = Expect(SettingsViewModel.CreateDefault().RemoveLoads, "a fresh model removes loads");
            bad += Expect(CfgBridge.ToCfg(SettingsViewModel.CreateDefault(), out _).RemoveLoads,
                "the default cfg removes loads");
            bad += Expect(Cfg.Parse(Cfg.Default).RemoveLoads, "the shipped cfg removes loads");

            // Turning it back off has to survive, or the default would be a one-way door.
            var off = SettingsViewModel.CreateDefault();
            off.RemoveLoads = false;
            bad += Expect(!SettingsViewModel.Deserialize(off.Serialize()).RemoveLoads,
                "an explicit 'off' survives the XML round trip");
            return bad + Expect(!CfgBridge.FromIni(CfgBridge.ToIni(off), out _).RemoveLoads,
                "an explicit 'off' survives the INI round trip");
        }

        private static int DefaultsBuildAValidCfg()
        {
            var cfg = CfgBridge.ToCfg(SettingsViewModel.CreateDefault(), out var problems);

            int bad = 0;
            foreach (var problem in problems) { Console.WriteLine("  ERROR defaults: " + problem); bad++; }
            bad += Expect(cfg.Splits.Count == 9, "the default route has " + cfg.Splits.Count + " splits");
            bad += Expect(cfg.Start != null, "the default start trigger parsed");
            bad += Expect(cfg.Reset != null, "the default reset trigger parsed");

            // A bad trigger must be reported rather than dropped in silence.
            var broken = SettingsViewModel.CreateDefault();
            broken.Splits.Add(new SplitEntry("Bad", "wat:nonsense"));
            CfgBridge.ToCfg(broken, out var brokenProblems);
            bad += Expect(brokenProblems.Count == 1, "a broken trigger is reported once");
            return bad;
        }

        /// <summary>
        /// Everything the dropdown offers, and everything a fresh install starts with,
        /// has to survive the parser. Neither list is reachable from the UI on Linux,
        /// which is why <see cref="Presets"/> lives in the memory assembly.
        /// </summary>
        private static int EveryPresetParses()
        {
            int bad = 0;
            foreach (var (where, text) in ShippedTriggers())
            {
                var errors = new List<string>();
                var trigger = Trigger.Parse(text, errors);

                if (trigger == null) { Console.WriteLine($"  ERROR {where}: '{text}' does not parse"); bad++; }
                foreach (var error in errors) { Console.WriteLine($"  ERROR {where}: {error}"); bad++; }
            }
            return bad;
        }

        /// <summary>
        /// A shipped trigger naming a scene the game does not have compiles, parses and
        /// validates — it shows up only as a split that never fires, in someone's run.
        /// That is the failure this pins down.
        /// </summary>
        private static int ScenePresetsNameRealScenes()
        {
            int bad = 0;
            foreach (var (where, text) in ShippedTriggers())
            {
                var trigger = Trigger.Parse(text, new List<string>());
                if (trigger == null) continue;      // already reported by EveryPresetParses

                foreach (var atom in trigger.Atoms)
                {
                    if (atom.Kind != AtomKind.Scene) continue;
                    if (BuildListScenes.Contains(atom.Arg, StringComparer.OrdinalIgnoreCase)) continue;

                    Console.WriteLine($"  ERROR {where}: '{atom.Arg}' is not a scene the game ships");
                    bad++;
                }
            }
            return bad;
        }

        private static IEnumerable<(string Where, string Trigger)> ShippedTriggers() =>
            Presets.All.Select(p => ("preset " + p, p.Trigger))
                .Concat(SettingsViewModel.DefaultSplits().Select(s => ("default split " + s.Name, s.Trigger)));

        /// <summary>
        /// The Debug tab is what a player is told to read when a split misbehaves, so
        /// it has to render before the attach has produced anything — including with
        /// nothing attached at all, which is what it shows most of the time.
        /// </summary>
        private static int StatusSurvivesADeadAttach()
        {
            int bad = 0;

            string nothing = StatusReport.Describe(null, null);
            bad += Expect(nothing.Contains("not attached"), "no game reads as 'not attached': " + nothing.Trim());

            using (var game = new Infinitevania())
            {
                string detached = StatusReport.Describe(game, null);
                bad += Expect(detached.Contains("not attached"),
                    "an un-hooked game reads as 'not attached': " + detached.Trim());
            }
            return bad;
        }
    }
}
