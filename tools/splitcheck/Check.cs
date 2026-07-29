using System;

namespace InfiniteSplitter.Tools
{
    /// <summary>
    /// The assertions every test group shares. Deliberately not a test framework: a
    /// framework would be a NuGet dependency, and the suite has to build anywhere the
    /// component does.
    ///
    /// A case returns the number of failures, so a group's Run() is a sum. Import with
    /// <c>using static InfiniteSplitter.Tools.Check;</c>.
    /// </summary>
    internal static class Check
    {
        /// <summary>0 when the condition holds, 1 and a printed line when it does not.</summary>
        public static int Expect(bool condition, string what)
        {
            if (condition) return 0;
            Console.WriteLine("  ERROR " + what);
            return 1;
        }

        /// <summary>Floating-point equality to within a millisecond, which is the
        /// precision the game's own clock carries.</summary>
        public static int Near(double actual, double expected, string what)
        {
            if (Math.Abs(actual - expected) < 0.001) return 0;
            Console.WriteLine($"  ERROR {what}: got {actual}, want {expected}");
            return 1;
        }

        /// <summary>An unconditional failure, for a case that has already decided.</summary>
        public static int Fail(string what)
        {
            Console.WriteLine("  ERROR " + what);
            return 1;
        }

        /// <summary>
        /// The game's build list, in build order. Shared because more than one group
        /// checks a shipped scene name against it, and a name the game does not have is
        /// the failure that no amount of parsing catches: it compiles, it validates, and
        /// it produces a split that never fires.
        ///
        /// Scene names reach the trigger language exactly as the game spells them, so
        /// the spaces and the trailing digits are the point.
        /// </summary>
        public static readonly string[] BuildListScenes =
        {
            "warnings", "menu", "initial cutscene", "lobby", "forest", "volcano",
            "castle", "castle initial", "final cutscene 1", "final cutscene 2",
            "credits", "post credits cutscene",
        };
    }
}
