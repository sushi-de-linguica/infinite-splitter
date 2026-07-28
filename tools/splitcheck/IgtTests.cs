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
        private const int UiPausedOffset = 0x08;

        public static int Run()
        {
            int bad = 0;
            Console.WriteLine("igt");

            bad += MonotonicAdvance();
            bad += SaveLoadJumpsBackwards();
            bad += PauseOnLoadExcludesTime();
            bad += ZeroAtStartRebases();
            bad += SlotResolution();

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
        /// Loading a save rewinds the game's counter. The clock must follow it, and the
        /// backwards delta must not reach the pause accumulator — that would corrupt
        /// every later reading.
        /// </summary>
        private static int SaveLoadJumpsBackwards()
        {
            var f = Fixture();
            f.Igt.PauseOnLoad = true;

            f.SetClock(1, 0, 5, 0f);
            f.Igt.Update(f.Snapshot, f.Fields);

            f.SetClock(1, 0, 2, 0f);            // an older save
            f.SetBool(LoadingOffset, true);     // and we are loading while it happens
            f.Igt.Update(f.Snapshot, f.Fields);
            int bad = Near(f.Igt.Value, 120, "after loading an older save");

            f.SetBool(LoadingOffset, false);
            f.SetClock(1, 0, 2, 10f);
            f.Igt.Update(f.Snapshot, f.Fields);
            bad += Near(f.Igt.Value, 130, "the backwards jump must not have been excluded");
            return bad;
        }

        private static int PauseOnLoadExcludesTime()
        {
            var f = Fixture();
            f.Igt.PauseOnLoad = true;

            f.SetClock(1, 0, 1, 0f);
            f.Igt.Update(f.Snapshot, f.Fields);

            // Ten seconds pass while a loading screen is up.
            f.SetBool(LoadingOffset, true);
            f.SetClock(1, 0, 1, 10f);
            f.Igt.Update(f.Snapshot, f.Fields);
            int bad = Near(f.Igt.Value, 60, "loading time must be carved out");

            // Five more seconds, playing.
            f.SetBool(LoadingOffset, false);
            f.SetClock(1, 0, 1, 15f);
            f.Igt.Update(f.Snapshot, f.Fields);
            bad += Near(f.Igt.Value, 65, "play time after a load resumes");

            // And with the option off the same sequence keeps everything.
            var g = Fixture();
            g.SetClock(1, 0, 1, 0f);
            g.Igt.Update(g.Snapshot, g.Fields);
            g.SetBool(LoadingOffset, true);
            g.SetClock(1, 0, 1, 10f);
            g.Igt.Update(g.Snapshot, g.Fields);
            bad += Near(g.Igt.Value, 70, "with pause_on_load off, loading counts");
            return bad;
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

            var g = Fixture();
            g.Igt.SlotSetting = "1";
            g.SetClock(1, 1, 0, 0f);
            g.SetClock(3, 9, 0, 0f);
            g.SetBool(CanSaveBase + 2, true);
            g.Igt.Update(g.Snapshot, g.Fields);
            bad += Expect(g.Igt.ActiveSlot == 1, "forced slot, got " + g.Igt.ActiveSlot);
            bad += Near(g.Igt.Value, 3600, "forced slot's clock");
            return bad;
        }

        private sealed class Harness
        {
            public readonly GameSnapshot Snapshot = new GameSnapshot();
            public readonly GameFields Fields = new GameFields();
            public readonly IgtSource Igt = new IgtSource();
            public readonly byte[] GameManager = new byte[0x100];
            public readonly byte[] UIManager = new byte[0x40];

            public void SetClock(int slot, int hours, int minutes, float seconds)
            {
                int at = HourBase + (slot - 1) * SlotStride;
                BitConverter.GetBytes(hours).CopyTo(GameManager, at);
                BitConverter.GetBytes(minutes).CopyTo(GameManager, at + 4);
                BitConverter.GetBytes(seconds).CopyTo(GameManager, at + 8);
            }

            public void SetBool(int offset, bool value) => GameManager[offset] = (byte)(value ? 1 : 0);
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
            h.Fields.UiPaused = Field("paused", UiPausedOffset, MonoTypeKind.Boolean);

            h.Snapshot.SetGameManager(0x1000, h.GameManager);
            h.Snapshot.SetUIManager(0x2000, h.UIManager);
            return h;
        }

        private static MonoFieldRef Field(string name, int offset, MonoTypeKind kind) =>
            new MonoFieldRef { Name = name, Offset = offset, Kind = kind };
    }
}
