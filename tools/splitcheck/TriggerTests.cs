using System;
using System.Collections.Generic;
using System.Linq;
using InfiniteSplitter.Core;
using InfiniteSplitter.Game;
using static InfiniteSplitter.Tools.Check;

namespace InfiniteSplitter.Tools
{
    /// <summary>
    /// The expression language, evaluated against a stub probe. The cases here are the
    /// ones where a mistake is invisible in play: an atom that names a scene the game
    /// does not have parses, validates and then simply never fires.
    /// </summary>
    public static class TriggerTests
    {
        public static int Run()
        {
            int bad = 0;
            Console.WriteLine("trigger");

            bad += EndingPicksTheRightCutscene();
            bad += EndingScenesAreInTheBuildList();
            bad += EndingRejectsAnythingElse();
            bad += SceneLadder();

            Console.WriteLine(bad == 0 ? "  ok" : $"  {bad} failure(s)");
            Console.WriteLine();
            return bad;
        }

        /// <summary>
        /// Which source answers `scene:`, and — the case this exists for — when none of
        /// them may. Quitting a run to the menu split a route's `scene:forest` because
        /// the native read goes dark for a window mid-transition and the managed
        /// fallback still named the area of the last save, which was forest. A scene
        /// nobody is in must never answer.
        /// </summary>
        private static int SceneLadder()
        {
            int bad = 0;
            var cases = new[]
            {
                // native,   alive, inMenu, changing, lastSaved,  expected
                ("forest",   true,  false,  false,    "lobby",    "forest"),   // native wins outright
                ("forest",   true,  true,   true,     "lobby",    "forest"),   // even mid-transition
                ("",         false, false,  false,    "forest",   ""),         // no singleton, no answer
                ("",         true,  true,   true,     "forest",   "menu"),     // the menu is exact
                ("",         true,  false,  true,     "forest",   ""),         // THE BUG: was "forest"
                ("",         true,  false,  false,    "lobby",    "lobby"),    // settled: the approximation
                ("",         true,  false,  false,    null,       ""),
            };

            foreach (var (native, alive, inMenu, changing, lastSaved, expected) in cases)
            {
                string got = MemoryProbe.ResolveScene(native, alive, inMenu, changing, lastSaved);
                if (got != expected)
                    bad += Fail($"scene ladder (native '{native}', alive {alive}, menu {inMenu}, " +
                                $"changing {changing}, saved '{lastSaved}') gave '{got}', want '{expected}'");
            }
            return bad;
        }

        /// <summary>
        /// `ending:any` is shorthand for the OR of the two, and the numbered forms must
        /// stay mutually exclusive — a run only ever takes one of them.
        /// </summary>
        private static int EndingPicksTheRightCutscene()
        {
            int bad = 0;
            var cases = new[]
            {
                // scene,               ending:1, ending:2, ending:any
                ("final cutscene 1",    true,     false,    true),
                ("final cutscene 2",    false,    true,     true),
                ("FINAL CUTSCENE 1",    true,     false,    true),   // scene: is case-insensitive
                ("castle",              false,    false,    false),
                ("credits",             false,    false,    false),
                ("",                    false,    false,    false),  // the load window
            };

            foreach (var (scene, first, second, any) in cases)
            {
                var probe = new SceneProbe { Scene = scene };
                bad += Expect(Eval("ending:1", probe) == first, $"ending:1 in \"{scene}\"");
                bad += Expect(Eval("ending:2", probe) == second, $"ending:2 in \"{scene}\"");
                bad += Expect(Eval("ending:any", probe) == any, $"ending:any in \"{scene}\"");

                // The shorthand and the expression it replaces must not drift apart.
                bad += Expect(
                    Eval("scene:final cutscene 1|scene:final cutscene 2", probe) == any,
                    $"ending:any matches the hand-written OR in \"{scene}\"");
            }
            return bad;
        }

        /// <summary>
        /// The scene names live in Trigger.cs, where nothing else can check them. This
        /// is what ties them to the game's build list.
        /// </summary>
        private static int EndingScenesAreInTheBuildList()
        {
            int bad = 0;
            foreach (var scene in Atom.EndingScenes)
                bad += Expect(BuildListScenes.Contains(scene, StringComparer.Ordinal),
                              $"'{scene}' is not a scene the game ships");

            bad += Expect(Atom.EndingScenes.Length == 2, "the game has two endings");
            return bad;
        }

        private static int EndingRejectsAnythingElse()
        {
            int bad = 0;
            foreach (var text in new[] { "ending", "ending:", "ending:3", "ending:0", "ending:both", "ending:1.0" })
            {
                var errors = new List<string>();
                var trigger = Trigger.Parse(text, errors);
                bad += Expect(trigger == null && errors.Count > 0, $"'{text}' is rejected");
            }
            return bad;
        }

        private static bool Eval(string text, IProbe probe)
        {
            var errors = new List<string>();
            var trigger = Trigger.Parse(text, errors);
            if (trigger == null || errors.Count > 0)
            {
                Fail($"'{text}' did not parse: {string.Join("; ", errors)}");
                return false;
            }
            return trigger.Eval(probe);
        }

        /// <summary>Answers the scene and nothing else; every other atom reads as false,
        /// which is what the cases here expect of them.</summary>
        private sealed class SceneProbe : IProbe
        {
            public string Scene { get; set; } = "";

            public bool Flag(string field) => false;
            public double Number(string field) => 0;
            public bool HasSkill(string skill) => false;
            public bool Collectible(string id) => false;
            public bool Activated(string id) => false;
            public int CountPrefix(string prefix) => 0;
            public bool Credits => false;
            public bool NewGame => false;
            public double Igt => 0;
            public void ReportUnknown(string what) { }
        }
    }
}
