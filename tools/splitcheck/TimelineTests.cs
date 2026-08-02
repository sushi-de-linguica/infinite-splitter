using System;
using System.Collections.Generic;
using InfiniteSplitter.Core;
using static InfiniteSplitter.Tools.Check;

namespace InfiniteSplitter.Tools
{
    /// <summary>
    /// Where the run's clock comes from, over a run's whole shape. The case that
    /// matters most is the last one: the game's clock does not stop when the run does,
    /// so a finished run that keeps following it reports a time nobody ran.
    /// </summary>
    public static class TimelineTests
    {
        public static int Run()
        {
            int bad = 0;
            Console.WriteLine("timeline");

            bad += FinishedRunHoldsItsLastSplit();
            bad += UndoResumesFollowingTheClock();
            bad += StartAndResetClearTheHeldTime();

            Console.WriteLine(bad == 0 ? "  ok" : $"  {bad} failure(s)");
            Console.WriteLine();
            return bad;
        }

        /// <summary>
        /// The ending fires at 19:02 and the game keeps counting through the cutscene,
        /// the credits and the results screen — only the main menu freezes its clock.
        /// The board has to stay on 19:02.
        /// </summary>
        private static int FinishedRunHoldsItsLastSplit()
        {
            var (timeline, link, probe) = Fixture("flag:a", "flag:b");

            timeline.DoStart("test");
            probe.Igt = 600;
            probe.Flags.Add("a");
            timeline.Tick(probe);
            int bad = Expect(timeline.Phase == Phase.Running, "still running after the first split");

            probe.Igt = 1142;                  // 19:02
            probe.Flags.Add("b");
            timeline.Tick(probe);

            bad += Expect(timeline.Phase == Phase.Ended, "the run ended on the last split");
            bad += Near(timeline.FinalIgt, 1142, "the held time is the last split's");
            bad += Near(link.LastGameTime, 1142, "and that is what was written");

            // The game's clock runs on for another 45 s of cutscene and credits.
            probe.Igt = 1187;
            timeline.Tick(probe);
            bad += Near(timeline.FinalIgt, 1142, "the held time does not follow the game's clock");
            return bad;
        }

        private static int UndoResumesFollowingTheClock()
        {
            var (timeline, _, probe) = Fixture("flag:a");

            timeline.DoStart("test");
            probe.Igt = 900;
            probe.Flags.Add("a");
            timeline.Tick(probe);
            int bad = Near(timeline.FinalIgt, 900, "held after the final split");

            timeline.DoUndo("test");
            bad += Expect(timeline.Phase == Phase.Running, "undo puts the run back in progress");
            bad += Near(timeline.FinalIgt, 0, "and drops the held time, so the clock is followed again");
            return bad;
        }

        private static int StartAndResetClearTheHeldTime()
        {
            var (timeline, _, probe) = Fixture("flag:a");

            timeline.DoStart("test");
            probe.Igt = 900;
            probe.Flags.Add("a");
            timeline.Tick(probe);

            timeline.DoStart("a second attempt");
            int bad = Near(timeline.FinalIgt, 0, "a new attempt starts with nothing held");

            probe.Igt = 300;
            timeline.Tick(probe);              // flag:a is still set, so it splits and ends
            timeline.DoReset("test");
            bad += Near(timeline.FinalIgt, 0, "a reset clears the held time");
            return bad;
        }

        private static (Timeline, RecordingLink, FlagProbe) Fixture(params string[] splits)
        {
            var cfg = new Cfg { AutoStart = false, AutoReset = false, RestartOnNewGame = false };
            for (int i = 0; i < splits.Length; i++)
                cfg.Splits.Add(new SplitDef
                {
                    Name = "split " + (i + 1),
                    Raw = splits[i],
                    Trigger = Trigger.Parse(splits[i], new List<string>()),
                    Line = i + 1,
                });

            var link = new RecordingLink();
            return (new Timeline(cfg, link), link, new FlagProbe());
        }

        /// <summary>Records the last game time written; every other command is a no-op,
        /// since these cases are about the number, not the sequencing.</summary>
        private sealed class RecordingLink : ILink
        {
            public double LastGameTime = double.NaN;

            public void StartTimer() { }
            public void Split() { }
            public void Undo() { }
            public void ResetRun() { }
            public void SetGameTime(double seconds) { LastGameTime = seconds; }
            public void Note(string message) { }
        }

        private sealed class FlagProbe : IProbe
        {
            public readonly HashSet<string> Flags = new HashSet<string>(StringComparer.Ordinal);
            public double Igt { get; set; }

            public bool Flag(string field) => Flags.Contains(field);
            public double Number(string field) => 0;
            public bool HasSkill(string skill) => false;
            public bool Collectible(string id) => false;
            public bool Activated(string id) => false;
            public int CountPrefix(string prefix) => 0;
            public string Scene => "";
            public bool Credits => false;
            public bool NewGame => false;
            public void ReportUnknown(string what) { }
        }
    }
}
