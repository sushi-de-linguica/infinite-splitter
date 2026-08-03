using System;
using InfiniteSplitter.Game;
using static InfiniteSplitter.Tools.Check;

namespace InfiniteSplitter.Tools
{
    /// <summary>
    /// New-game detection: the active scene leaving the menu without a save having
    /// been loaded during that visit. <see cref="SittingOnTheMenuDoesNotStart"/> and
    /// <see cref="LoadingFlagClearsBeforeTheSceneChange"/> are the two that matter —
    /// they are the bugs the previous detector shipped with. docs/testing.md.
    /// </summary>
    public static class NewGameTests
    {
        // The run counter is diagnostic only, so every case passes the same value
        // unless it is what is under test.
        private const int NoRuns = -1;

        public static int Run()
        {
            int bad = 0;
            Console.WriteLine("newgame");

            bad += SittingOnTheMenuDoesNotStart();
            bad += FirstNewGameOfTheSession();
            bad += LoadingASaveIsNotANewGame();
            bad += LoadingFlagClearsBeforeTheSceneChange();
            bad += BackToBackNewGames();
            bad += QuitToMenuAndBackIsNotANewGame();
            bad += AttachedMidGameplayDoesNotPulse();
            bad += PulseIsOneTickWide();
            bad += RunCounterIsDiagnosticOnly();

            Console.WriteLine(bad == 0 ? "  ok" : $"  {bad} failure(s)");
            Console.WriteLine();
            return bad;
        }

        /// <summary>
        /// The reported bug: launching the game and leaving it on the menu started the
        /// timer on its own. The old detector keyed on a slot clock reading zero, which
        /// is exactly what the menu shows before the game has read the saves off disk.
        /// </summary>
        private static int SittingOnTheMenuDoesNotStart()
        {
            var detector = new NewGameDetector();

            for (int tick = 0; tick < 200; tick++)
            {
                detector.Update(NoRuns, timePaused: true, loading: false, gameManagerAlive: true);
                if (detector.Pulse) return Fail($"pulsed on tick {tick} of doing nothing on the menu");
            }
            return 0;
        }

        private static int FirstNewGameOfTheSession()
        {
            var detector = new NewGameDetector();

            // The menu, with nothing loading: the player is picking a slot.
            for (int tick = 0; tick < 40; tick++)
            {
                detector.Update(NoRuns, true, false, true);
                if (detector.Pulse) return Fail("pulsed while still on the menu");
            }

            // The first gameplay scene comes up.
            detector.Update(1, timePaused: false, loading: false, gameManagerAlive: true);
            if (!detector.Pulse) return Fail("MISSED the first new game of the session");

            for (int tick = 0; tick < 40; tick++)
            {
                detector.Update(1, false, false, true);
                if (detector.Pulse) return Fail("kept pulsing into the run");
            }
            return 0;
        }

        private static int LoadingASaveIsNotANewGame()
        {
            var detector = new NewGameDetector();

            for (int tick = 0; tick < 10; tick++) detector.Update(NoRuns, true, false, true);

            // Loading a save raises the flag a full second before the scene changes.
            for (int tick = 0; tick < 60; tick++)
            {
                detector.Update(NoRuns, timePaused: true, loading: true, gameManagerAlive: true);
                if (detector.Pulse) return Fail("pulsed while a save was loading");
            }

            // It stays raised a frame into the destination, then clears.
            detector.Update(NoRuns, false, true, true);
            if (detector.Pulse) return Fail("loading a save was mistaken for a new game");

            for (int tick = 0; tick < 20; tick++)
            {
                detector.Update(NoRuns, false, false, true);
                if (detector.Pulse) return Fail("pulsed after loading a save");
            }
            return 0;
        }

        /// <summary>
        /// Why the flag is latched for the whole menu visit rather than sampled at the
        /// transition: a slow poll can land after the game has already cleared it, and
        /// a load would then read as a new game.
        /// </summary>
        private static int LoadingFlagClearsBeforeTheSceneChange()
        {
            var detector = new NewGameDetector();

            for (int tick = 0; tick < 5; tick++) detector.Update(NoRuns, true, false, true);
            detector.Update(NoRuns, timePaused: true, loading: true, gameManagerAlive: true);

            // Next poll: already in the destination, and the flag is down again.
            detector.Update(NoRuns, timePaused: false, loading: false, gameManagerAlive: true);
            return detector.Pulse ? Fail("a save load was read as a new game after the flag cleared") : 0;
        }

        /// <summary>Resetting early and going again: each new game gets its own pulse,
        /// so the latch has to clear on every fresh menu visit.</summary>
        private static int BackToBackNewGames()
        {
            var detector = new NewGameDetector();
            int pulses = 0;

            for (int round = 0; round < 3; round++)
            {
                for (int tick = 0; tick < 10; tick++)
                {
                    detector.Update(round, true, false, true);
                    if (detector.Pulse) pulses++;
                }
                for (int tick = 0; tick < 10; tick++)
                {
                    detector.Update(round + 1, false, false, true);
                    if (detector.Pulse) pulses++;
                }
            }

            return pulses == 3 ? 0 : Fail($"three new games in a row produced {pulses} pulse(s)");
        }

        /// <summary>
        /// The behaviour `reset = never` rests on: quitting to the menu mid-run and
        /// loading the save back must leave the run alone.
        /// </summary>
        private static int QuitToMenuAndBackIsNotANewGame()
        {
            var detector = new NewGameDetector();

            for (int tick = 0; tick < 10; tick++) detector.Update(1, true, false, true);
            for (int tick = 0; tick < 10; tick++) detector.Update(1, false, false, true);   // the run

            for (int tick = 0; tick < 20; tick++)                                            // quit to menu
            {
                detector.Update(1, true, false, true);
                if (detector.Pulse) return Fail("pulsed on merely reaching the menu");
            }
            for (int tick = 0; tick < 30; tick++) detector.Update(1, true, true, true);      // load the save
            for (int tick = 0; tick < 10; tick++)
            {
                detector.Update(1, false, tick == 0, true);
                if (detector.Pulse) return Fail("a save / quit / load cycle restarted the run");
            }
            return 0;
        }

        private static int AttachedMidGameplayDoesNotPulse()
        {
            var detector = new NewGameDetector();

            for (int tick = 0; tick < 50; tick++)
            {
                detector.Update(NoRuns, timePaused: false, loading: false, gameManagerAlive: true);
                if (detector.Pulse) return Fail("pulsed after attaching to a run already in progress");
            }
            return 0;
        }

        private static int PulseIsOneTickWide()
        {
            var detector = new NewGameDetector();

            detector.Update(NoRuns, true, false, true);
            detector.Update(NoRuns, false, false, true);
            if (!detector.Pulse) return Fail("no pulse");

            detector.Update(NoRuns, false, false, true);
            return detector.Pulse ? Fail("the pulse lasted more than one tick") : 0;
        }

        /// <summary>The counter is shown in the Debug tab and nowhere else: a rise on
        /// its own must not start anything.</summary>
        private static int RunCounterIsDiagnosticOnly()
        {
            var detector = new NewGameDetector();

            for (int tick = 0; tick < 5; tick++) detector.Update(-1, false, false, true);
            for (int count = 1; count <= 5; count++)
            {
                detector.Update(count, timePaused: false, loading: false, gameManagerAlive: true);
                if (detector.Pulse) return Fail("the run counter rising started a run on its own");
            }

            if (!detector.RunLoggerAvailable) return Fail("the counter should read as available");
            return detector.RunCount == 5 ? 0 : Fail($"run count reported {detector.RunCount}, expected 5");
        }
    }
}
