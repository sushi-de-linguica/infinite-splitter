using System;
using InfiniteSplitter.Memory;

namespace InfiniteSplitter.Diagnostics
{
    /// <summary>
    /// Owns the attach/detach lifecycle, so a caller only has to call
    /// <see cref="TryRefresh"/> every tick:
    ///
    ///   not attached  -> attach, then raise Hooked (the one-time resolve)
    ///   attached      -> cheap liveness check only
    ///   process gone  -> raise Exited, drop everything, back to not attached
    ///
    /// Hooked returning an error leaves the hook un-attached so the next tick retries.
    /// mono-2.0-bdwgc.dll is not loaded during the game's first moments, and "try
    /// again shortly" is the right answer there, not a hard failure.
    /// </summary>
    public sealed class ProcessHook : IMemory, IDisposable
    {
        private readonly string _processName;
        private readonly IProcessWrapper _process;

        public ProcessHook(string processName, IProcessWrapper process = null)
        {
            _processName = processName;
            _process = process ?? new ProcessWrapper();
        }

        /// <summary>Raised once after the process is found. Return an error to refuse
        /// the attach and be asked again next tick.</summary>
        public event Func<Result> Hooked;

        /// <summary>Raised when the process we were attached to goes away.</summary>
        public event Action Exited;

        public bool IsHooked { get; private set; }
        public IProcessWrapper Process => _process;
        public int ProcessId => _process.Id;

        public Result TryRefresh()
        {
            if (IsHooked)
            {
                if (!_process.HasExited) return Result.Ok();

                IsHooked = false;
                _process.Detach();
                try { Exited?.Invoke(); } catch { /* a listener must not break the hook */ }
                return Result.Err(RefreshErrorReason.ProcessExited, _processName);
            }

            var attach = _process.Attach(_processName);
            if (attach.IsErr) return attach;

            Result hooked;
            try
            {
                hooked = Hooked?.Invoke() ?? Result.Ok();
            }
            catch (Exception e)
            {
                hooked = Result.Err(RefreshErrorReason.UnknownException, "during attach", e);
            }

            if (hooked.IsErr)
            {
                _process.Detach();
                return hooked;
            }

            IsHooked = true;
            return Result.Ok();
        }

        public byte[] ReadBytes(long address, int length) => _process.ReadBytes(address, length);

        public bool TryReadInto(long address, byte[] buffer, int length) =>
            _process.TryReadInto(address, buffer, length);

        public void WriteBytes(long address, byte[] bytes) => _process.WriteBytes(address, bytes);

        public void Dispose()
        {
            IsHooked = false;
            _process.Dispose();
        }
    }
}
