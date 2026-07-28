using System;

namespace InfiniteSplitter.Tools
{
    /// <summary>
    ///   splitcheck --selftest                    offline suite; no game, no LiveSplit
    ///   splitcheck --attach --dump               resolve Mono and print everything
    ///   splitcheck --attach --watch              live table at 4 Hz
    ///   splitcheck --attach --ids                every collectible and activation id
    ///   splitcheck --attach --eval "&lt;trigger&gt;"    evaluate a trigger against the game
    ///   splitcheck --attach --find-scene forest  derive the native scene pointer chain
    ///
    /// docs/splitcheck.md covers every mode; docs/testing.md covers the suite.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (Args.Has(args, "--selftest")) return SelfTest();

            if (Args.Has(args, "--attach")) return Attach.Run(args);

            Console.WriteLine("usage: splitcheck --selftest | " +
                              "--attach [--dump|--watch|--ids|--eval <trigger>|--find-scene <name>]");
            return 2;
        }

        private static int SelfTest()
        {
            int failures = 0;

            failures += MonoTests.Run();
            failures += IgtTests.Run();
            failures += NewGameTests.Run();
            failures += SettingsTests.Run();
            failures += ScanTests.Run();

            Console.WriteLine(failures == 0 ? "OK — no problems found." : $"{failures} problem(s) found.");
            return failures == 0 ? 0 : 1;
        }
    }
}
