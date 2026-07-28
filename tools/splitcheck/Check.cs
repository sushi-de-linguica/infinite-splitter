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
    }
}
