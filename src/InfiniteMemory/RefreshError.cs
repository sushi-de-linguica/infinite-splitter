using System;

namespace InfiniteSplitter
{
    /// <summary>
    /// Every way attaching to the game can fail. A closed set on purpose: the
    /// component decides whether to back off from the reason alone, and the settings
    /// UI explains each one in the player's terms.
    /// </summary>
    public enum RefreshErrorReason
    {
        None = 0,

        // process
        ProcessNotRunning,
        ProcessExited,
        AccessDenied,
        MainModuleNull,
        HostNot64Bit,
        TargetNot64Bit,

        // mono
        MonoModuleNotFound,
        MonoExportMissing,
        RootDomainNull,
        AssemblyNotFound,
        ClassNotFound,
        FieldNotFound,
        StaticDataNull,

        /// <summary>A structure walk failed its own sanity checks — almost always
        /// a stale offset after a game or Unity update.</summary>
        LayoutMismatch,

        ReadFailed,
        UnknownException,
    }

    public sealed class RefreshError
    {
        public RefreshErrorReason Reason { get; }
        public string Message { get; }
        public Exception Exception { get; }

        public RefreshError(RefreshErrorReason reason, string message = null, Exception exception = null)
        {
            Reason = reason;
            Message = message;
            Exception = exception;
        }

        /// <summary>What the Debug tab shows. Written for a player, not a developer.</summary>
        public string Describe()
        {
            string head = Reason switch
            {
                RefreshErrorReason.ProcessNotRunning => "Infinitevania is not running.",
                RefreshErrorReason.ProcessExited => "Infinitevania closed.",
                RefreshErrorReason.AccessDenied => "Cannot read the game's memory. Try running LiveSplit as administrator.",
                RefreshErrorReason.HostNot64Bit => "LiveSplit is running as a 32-bit process; a 64-bit host is required.",
                RefreshErrorReason.TargetNot64Bit => "The game process is not 64-bit.",
                RefreshErrorReason.MonoModuleNotFound => "mono-2.0-bdwgc.dll is not loaded yet — give the game a moment to start.",
                RefreshErrorReason.MonoExportMissing => "The Mono runtime is missing an export we need.",
                RefreshErrorReason.RootDomainNull => "The Mono root domain is not up yet.",
                RefreshErrorReason.AssemblyNotFound => "The game assembly is not loaded yet.",
                RefreshErrorReason.ClassNotFound => "A game class could not be found. The game may have updated — see the README.",
                RefreshErrorReason.FieldNotFound => "A game field could not be found. The game may have updated — see the README.",
                RefreshErrorReason.StaticDataNull => "A class's static storage is not initialised yet.",
                RefreshErrorReason.LayoutMismatch => "The Mono runtime does not have the layout we expect. The game or Unity version may have changed.",
                _ => Reason.ToString(),
            };
            return string.IsNullOrEmpty(Message) ? head : head + " (" + Message + ")";
        }

        public override string ToString() =>
            Exception == null ? Reason + ": " + Message : Reason + ": " + Message + " -> " + Exception;
    }
}
