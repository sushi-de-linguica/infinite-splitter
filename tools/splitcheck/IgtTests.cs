using System;
using InfiniteSplitter.Game;
using InfiniteSplitter.Mono;
using static InfiniteSplitter.Tools.Check;

namespace InfiniteSplitter.Tools
{
    /// <summary>
    /// The clock, offline. Every case is a behaviour observed on a real run, so the
    /// reader has to reproduce all of them. See docs/testing.md.
    /// </summary>
    public static class IgtTests
    {
        // A synthetic layout: the point is IgtSource's arithmetic, not the
        // real offsets, which MonoTests covers.
        private const int HourBase = 0x10;     // hour, minutes, seconds packed per slot
        private const int SlotStride = 0x10;
        private const int CanSaveBase = 0x80;
        private const int LoadingOffset = 0x90;
        private const int TimePausedOffset = 0x91;
        private const int UiPausedOffset = 0x08;

        public static int Run()
        {
            int bad = 0;
            Console.WriteLine("igt");

            bad += MonotonicAdvance();
            bad += SaveLoadJumpsBackwards();
            bad += LoadingIsRemovedAndNothingElseIs();
            bad += SceneTransitionsAreRemoved();
            bad += TheMenuIsNotASceneTransition();
            bad += SlotSwapJumpIsNotRemoved();
            bad += RemoveLoadsOffKeepsEverything();
            bad += ZeroAtStartRebases();
            bad += SlotResolution();
            bad += RemovedTimeDoesNotSurviveIntoTheNextAttempt();

            Console.WriteLine(bad == 0 ? "  ok" : $"  {bad} failure(s)");
            Console.WriteLine();
            return bad;
        }

        private static int MonotonicAdvance()
        {
            var f = Fixture();
            f.SetClock(1, 0, 1, 30.5f);
            f.Igt.Update(f.Snapshot, f.Fields);
            int bad = Near(f.Igt.Value, 90.5, "initial value");

            f.SetClock(1, 0, 2, 0.25f);
            f.Igt.Update(f.Snapshot, f.Fields);
            bad += Near(f.Igt.Value, 120.25, "after advancing");

            f.SetClock(1, 1, 0, 0f);
            f.Igt.Update(f.Snapshot, f.Fields);
            bad += Near(f.Igt.Value, 3600, "across the hour");
            return bad;
        }

        /// <summary>
        /// Loading a save rewinds the game's counter, and the clock follows it down
        /// rather than holding a high-water mark — which is what lets a run survive a
        /// save / quit to menu / load cycle showing the right time.
        /// </summary>
        private static int SaveLoadJumpsBackwards()
        {
            var f = Fixture();
            f.SetClock(1, 0, 5, 0f);
            f.Igt.Update(f.Snapshot, f.Fields);
            int bad = Near(f.Igt.Value, 300, "before loading");

            f.SetClock(1, 0, 2, 0f);            // an older save
            f.Igt.Update(f.Snapshot, f.Fields);
            bad += Near(f.Igt.Value, 120, "after loading an older save");

            f.SetClock(1, 0, 2, 10f);
            f.Igt.Update(f.Snapshot, f.Fields);
            bad += Near(f.Igt.Value, 130, "and counts on from where the save left off");
            return bad;
        }

        /// <summary>
        /// Loading is the one thing taken back out. The pause menu is not: the game
        /// counts it, the player can see it on the same screen they paused from, and a
        /// number they cannot check is worse than a slightly longer one.
        /// </summary>
        private static int LoadingIsRemovedAndNothingElseIs()
        {
            var f = Fixture();
            f.SetClock(1, 0, 1, 0f);
            f.Igt.Update(f.Snapshot, f.Fields);

            // Ten seconds under a loading screen.
            f.SetBool(LoadingOffset, true);
            f.Advance(10);
            int bad = Expect(f.Igt.Excluding, "a loading screen must read as removing");
            bad += Near(f.Igt.Value, 60, "loading time is removed");

            // Five more with the pause menu up.
            f.SetBool(LoadingOffset, false);
            f.SetUiBool(UiPausedOffset, true);
            f.Advance(5);
            bad += Expect(!f.Igt.Excluding, "the pause menu must not read as removing");
            bad += Near(f.Igt.Value, 65, "pause-menu time counts");
            return bad + Near(f.Igt.Raw, 75, "the raw clock still follows the game");
        }

        /// <summary>
        /// The loading flag only covers save loads. Walking through a portal is a scene
        /// transition, which the game reports separately — and the clock runs through
        /// both.
        /// </summary>
        private static int SceneTransitionsAreRemoved()
        {
            var f = Fixture();
            f.SetClock(1, 0, 1, 0f);
            f.Igt.Update(f.Snapshot, f.Fields);

            f.Snapshot.SceneLoadInFlight = true;
            f.Advance(4);
            int bad = Expect(f.Igt.Excluding, "a scene transition must read as removing");
            bad += Near(f.Igt.Value, 60, "the transition is removed");

            f.Snapshot.SceneLoadInFlight = false;
            f.Advance(5);
            bad += Near(f.Igt.Value, 65, "play resumes on the other side");
            return bad + Near(f.Igt.Excluded, 4, "the removed total");
        }

        /// <summary>
        /// The menu has no UIManager of its own, so the transition signal reads the
        /// previous scene's and stays set the whole time the menu is up. The in-menu
        /// test has to veto it, or every menu visit would report as a load.
        /// </summary>
        private static int TheMenuIsNotASceneTransition()
        {
            var f = Fixture();
            f.SetClock(1, 0, 1, 0f);
            f.Igt.Update(f.Snapshot, f.Fields);

            f.Snapshot.SceneLoadInFlight = true;
            f.SetBool(TimePausedOffset, true);
            f.Advance(5);

            int bad = Expect(!f.Igt.Excluding, "the menu must not read as a scene transition");
            return bad + Near(f.Igt.Value, 65, "nothing may be removed on the menu");
        }

        /// <summary>
        /// Reading a different slot's counter moves the number by minutes in one tick.
        /// That is not time that passed, and it must never reach the accumulator — the
        /// backwards guard does not catch it, because the jump is forwards.
        /// </summary>
        private static int SlotSwapJumpIsNotRemoved()
        {
            var f = Fixture();
            f.SetClock(1, 0, 1, 0f);
            f.Igt.Update(f.Snapshot, f.Fields);

            f.SetBool(LoadingOffset, true);
            f.SetClock(1, 2, 0, 0f);            // the counter jumps to a two-hour save
            f.Igt.Update(f.Snapshot, f.Fields);

            int bad = Near(f.Igt.Excluded, 0, "a slot-sized jump must not be removed");
            return bad + Near(f.Igt.Value, 7200, "the clock still follows the counter");
        }

        /// <summary>With the option off the clock is the mirror it used to be, which is
        /// what a player who wants to match the in-game number is asking for.</summary>
        private static int RemoveLoadsOffKeepsEverything()
        {
            var f = Fixture();
            f.Igt.RemoveLoads = false;
            f.SetClock(1, 0, 1, 0f);
            f.Igt.Update(f.Snapshot, f.Fields);

            f.SetBool(LoadingOffset, true);
            f.Advance(10);
            f.SetBool(LoadingOffset, false);
            f.Snapshot.SceneLoadInFlight = true;
            f.Advance(4);

            int bad = Near(f.Igt.Value, 74, "with remove_loads off, everything counts");
            return bad + Near(f.Igt.Excluded, 0, "and nothing accumulates");
        }

        private static int ZeroAtStartRebases()
        {
            var f = Fixture();
            f.SetClock(1, 2, 30, 0f);
            f.Igt.Update(f.Snapshot, f.Fields);
            int bad = Near(f.Igt.Value, 9000, "absolute value before rebasing");

            f.Igt.Rebase(true, f.Snapshot, f.Fields);
            bad += Near(f.Igt.Value, 0, "zero_at_start starts from zero");

            f.SetClock(1, 2, 31, 0f);
            f.Igt.Update(f.Snapshot, f.Fields);
            bad += Near(f.Igt.Value, 60, "and counts up from there");

            // Rebasing with zeroAtStart off keeps the game's absolute number.
            f.Igt.Rebase(false, f.Snapshot, f.Fields);
            bad += Near(f.Igt.Value, 9060, "rebase(false) keeps the absolute value");
            return bad;
        }

        /// <summary>
        /// canSaveN picks the slot, and the game clears canSave1..4 while the menu's
        /// slot list is up — so the resolver keeps the last known slot rather than
        /// snapping back to 1 and reporting some other save's time.
        /// </summary>
        private static int SlotResolution()
        {
            var f = Fixture();
            f.SetClock(1, 1, 0, 0f);
            f.SetClock(3, 9, 0, 0f);

            f.SetBool(CanSaveBase + 2, true);           // canSave3
            f.Igt.Update(f.Snapshot, f.Fields);
            int bad = Expect(f.Igt.ActiveSlot == 3, "auto picked slot " + f.Igt.ActiveSlot);
            bad += Near(f.Igt.Value, 32400, "slot 3's clock");

            f.SetBool(CanSaveBase + 2, false);          // the menu clears them all
            f.Igt.Update(f.Snapshot, f.Fields);
            bad += Expect(f.Igt.ActiveSlot == 3, "kept the last slot, got " + f.Igt.ActiveSlot);
            bad += Near(f.Igt.Value, 32400, "and kept reading its clock");
            return bad;
        }

        /// <summary>
        /// The removed-load total belongs to one attempt. Quitting to the menu removes
        /// the transition out of the run, and carrying that into the next attempt held
        /// its clock at zero — Math.Max(0, raw - removed) — for as long as the total,
        /// which is what a run looked like that had been restarted a few times.
        /// </summary>
        private static int RemovedTimeDoesNotSurviveIntoTheNextAttempt()
        {
            var f = Fixture();
            f.SetClock(1, 0, 0, 0f);
            f.Igt.Rebase(false, f.Snapshot, f.Fields);

            // Play a little, then quit to the menu: the transition out is removed.
            f.Advance(20);
            f.Snapshot.SceneLoadInFlight = true;
            f.Advance(4);
            f.Snapshot.SceneLoadInFlight = false;
            int bad = Near(f.Igt.Excluded, 4, "the transition was removed");

            // A new game zeroes the counter, and the next attempt starts over.
            f.SetClock(1, 0, 0, 0f);
            f.Igt.Rebase(false, f.Snapshot, f.Fields);
            bad += Near(f.Igt.Excluded, 0, "the removed total starts over");
            bad += Near(f.Igt.Value, 0, "and so does the clock");

            // The first seconds of the new attempt must count, not be swallowed.
            f.Advance(3);
            return bad + Near(f.Igt.Value, 3, "the new attempt's clock runs from the first second");
        }

        private sealed class Harness
        {
            public readonly GameSnapshot Snapshot = new GameSnapshot();
            public readonly GameFields Fields = new GameFields();
            public readonly IgtSource Igt = new IgtSource();
            public readonly byte[] GameManager = new byte[0x100];
            public readonly byte[] UIManager = new byte[0x40];

            private double _slot1;

            public void SetClock(int slot, int hours, int minutes, float seconds)
            {
                int at = HourBase + (slot - 1) * SlotStride;
                BitConverter.GetBytes(hours).CopyTo(GameManager, at);
                BitConverter.GetBytes(minutes).CopyTo(GameManager, at + 4);
                BitConverter.GetBytes(seconds).CopyTo(GameManager, at + 8);
                if (slot == 1) _slot1 = hours * 3600.0 + minutes * 60.0 + seconds;
            }

            /// <summary>
            /// Runs slot 1's clock forward the way the game does — a frame at a time,
            /// updating as it goes. Anything reading a delta has to see one of a
            /// plausible size; a single jump of the same total is a different event.
            /// </summary>
            public void Advance(double seconds)
            {
                const double Step = 1.0 / 60;
                for (double left = seconds; left > 1e-9; left -= Step)
                {
                    double total = _slot1 + Math.Min(Step, left);
                    int hours = (int)(total / 3600);
                    int minutes = (int)((total - hours * 3600) / 60);
                    SetClock(1, hours, minutes, (float)(total - hours * 3600 - minutes * 60));
                    Igt.Update(Snapshot, Fields);
                }
            }

            public void SetBool(int offset, bool value) => GameManager[offset] = (byte)(value ? 1 : 0);

            public void SetUiBool(int offset, bool value) => UIManager[offset] = (byte)(value ? 1 : 0);
        }

        private static Harness Fixture()
        {
            var h = new Harness();

            for (int slot = 1; slot <= 5; slot++)
            {
                int at = HourBase + (slot - 1) * SlotStride;
                h.Fields.Hour[slot] = Field("hourCount" + slot, at, MonoTypeKind.Int32);
                h.Fields.Minutes[slot] = Field("minutesCount" + slot, at + 4, MonoTypeKind.Int32);
                h.Fields.Seconds[slot] = Field("secondsCount" + slot, at + 8, MonoTypeKind.Single);
                h.Fields.CanSave[slot] = Field("canSave" + slot, CanSaveBase + slot - 1, MonoTypeKind.Boolean);
            }
            h.Fields.Loading = Field("loading", LoadingOffset, MonoTypeKind.Boolean);
            h.Fields.TimePaused = Field("timePaused", TimePausedOffset, MonoTypeKind.Boolean);
            h.Fields.UiPaused = Field("paused", UiPausedOffset, MonoTypeKind.Boolean);

            h.Snapshot.SetGameManager(0x1000, h.GameManager);
            h.Snapshot.SetUIManager(0x2000, h.UIManager);
            return h;
        }

        private static MonoFieldRef Field(string name, int offset, MonoTypeKind kind) =>
            new MonoFieldRef { Name = name, Offset = offset, Kind = kind };
    }
}
