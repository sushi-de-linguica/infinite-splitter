using System;
using InfiniteSplitter.Game;
using static InfiniteSplitter.Tools.Check;

namespace InfiniteSplitter.Tools
{
    /// <summary>
    /// New-game detection. <see cref="FirstNewGameOfTheSession"/> is the case that
    /// matters most — the obvious implementation silently misses it. docs/testing.md.
    /// </summary>
    public static class NewGameTests
    {
        public static int Run()
        {
            int bad = 0;
            Console.WriteLine("newgame");

            bad += FirstNewGameOfTheSession();
            bad += SubsequentNewGames();
            bad += PulseIsOneTickWide();
            bad += LoadingASaveIsNotANewGame();

            Console.WriteLine(bad == 0 ? "  ok" : $"  {bad} failure(s)");
            Console.WriteLine();
            return bad;
        }

        /// <summary>
        /// RunLogger's static constructor only runs when Menu.PlayGame first touches
        /// it, so its counter reads -1 until then and jumps straight to 1 — no baseline
        /// to have risen from. A detector watching only for a rise misses it entirely.
        /// </summary>
        private static int FirstNewGameOfTheSession()
        {
            var detector = new NewGameDetector();

            // Playing from a loaded save: counter unavailable, clock well past zero.
            for (int tick = 0; tick < 10; tick++)
            {
                detector.Update(runCount: -1, rawIgt: 3600 + tick, timePaused: false, gameManagerAlive: true);
                if (detector.Pulse) return Fail("pulsed while simply playing a loaded save");
            }

            // Quit to the menu. The clock is still the save's, so still no pulse.
            for (int tick = 0; tick < 10; tick++)
            {
                detector.Update(-1, 3610, timePaused: true, gameManagerAlive: true);
                if (detector.Pulse) return Fail("pulsed on merely reaching the menu");
            }

            // New game: PlayGame zeroes the clocks while still on the menu scene, and
            // RunLogger's constructor runs, so its counter appears at 1.
            detector.Update(runCount: 1, rawIgt: 0.0, timePaused: true, gameManagerAlive: true);
            if (!detector.Pulse) return Fail("MISSED the first new game of the session");

            // It must not keep firing across the 0.3 s wait and the scene load.
            for (int tick = 0; tick < 30; tick++)
            {
                detector.Update(1, 0.1 * tick, timePaused: true, gameManagerAlive: true);
                if (detector.Pulse) return Fail("pulsed repeatedly during the load");
            }
            return 0;
        }

        private static int SubsequentNewGames()
        {
            var detector = new NewGameDetector();

            detector.Update(1, 0.0, true, true);          // first new game
            for (int tick = 0; tick < 5; tick++) detector.Update(1, 10 + tick, false, true);

            // A second new game, started from well into a run. The clock signal fires
            // first; the counter rise must not then fire a second, duplicate pulse.
            detector.Update(2, 0.0, true, true);
            if (!detector.Pulse) return Fail("missed the second new game");

            int extra = 0;
            for (int tick = 0; tick < 20; tick++)
            {
                detector.Update(2, 0.2 * tick, tick < 10, true);
                if (detector.Pulse) extra++;
            }
            if (extra != 0) return Fail($"the second new game produced {extra} extra pulses");
            return 0;
        }

        private static int PulseIsOneTickWide()
        {
            var detector = new NewGameDetector();
            detector.Update(1, 0.0, true, true);
            if (!detector.Pulse) return Fail("no pulse");

            detector.Update(1, 0.0, true, true);
            return detector.Pulse ? Fail("the pulse lasted more than one tick") : 0;
        }

        /// <summary>A loaded save always carries its own elapsed time, so a clock at
        /// zero on the menu is a state only a fresh run produces.</summary>
        private static int LoadingASaveIsNotANewGame()
        {
            var detector = new NewGameDetector();
            detector.Update(3, 0.0, true, true);              // a new game, to set a baseline
            for (int tick = 0; tick < 5; tick++) detector.Update(3, 100 + tick, false, true);

            // Quit to menu and load a save with 2 hours on it.
            for (int tick = 0; tick < 10; tick++)
            {
                detector.Update(3, 7200, timePaused: true, gameManagerAlive: true);
                if (detector.Pulse) return Fail("loading a save was mistaken for a new game");
            }
            for (int tick = 0; tick < 10; tick++)
            {
                detector.Update(3, 7200 + tick, timePaused: false, gameManagerAlive: true);
                if (detector.Pulse) return Fail("pulsed after loading a save");
            }
            return 0;
        }
    }
}
