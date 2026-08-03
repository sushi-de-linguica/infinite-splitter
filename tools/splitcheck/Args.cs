using System;

namespace InfiniteSplitter.Tools
{
    /// <summary>
    /// Command-line flags, at the level this tool needs: presence, and the value that
    /// follows. No parser library and no grammar — every mode is one flag, and a flag
    /// with nothing after it reads as absent rather than as an error.
    /// </summary>
    internal static class Args
    {
        public static bool Has(string[] args, string flag) => Array.IndexOf(args, flag) >= 0;

        /// <summary>The argument after <paramref name="flag"/>, or null.</summary>
        public static string Value(string[] args, string flag)
        {
            int at = Array.IndexOf(args, flag);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }
    }
}
