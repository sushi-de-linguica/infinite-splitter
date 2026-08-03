using System;
using InfiniteSplitter.Core;
using LiveSplit.Model;

namespace InfiniteSplitter
{
    /// <summary>
    /// Drives LiveSplit's timer from <see cref="Timeline"/>, whose ordering rules
    /// describe TimerModel exactly: reset, set the game time, then start — and
    /// TimerModel.Start() is a no-op unless the timer is stopped, so a finished or
    /// abandoned attempt genuinely has to be cleared first.
    ///
    /// Reentrancy: LiveSplitState.OnStart and OnReset fire both when the user acts and
    /// when we call TimerModel, so without a guard DoStart -> Start() -> OnStart ->
    /// DoStart recurses. <see cref="Suppressed"/> is raised around every call the
    /// timeline makes, and the component's handlers return early while it is set.
    /// </summary>
    public sealed class TimerModelLink : ILink
    {
        private readonly TimerModel _model;
        private readonly LiveSplitState _state;
        private readonly Action<string> _note;

        /// <summary>True while a timer change originated from us rather than the user.</summary>
        public bool Suppressed { get; private set; }

        public TimerModelLink(TimerModel model, LiveSplitState state, Action<string> note)
        {
            _model = model;
            _state = state;
            _note = note;
        }

        public void StartTimer() => Guarded(() => _model.Start());

        public void Split() => Guarded(() => _model.Split());

        public void Undo() => Guarded(() => _model.UndoSplit());

        public void ResetRun() => Guarded(() => _model.Reset());

        /// <summary>
        /// Pushes the game's clock into LiveSplit's Game Time.
        ///
        /// <c>IsGameTimePaused</c> is re-asserted on every write and that is load
        /// bearing, not defensive. <c>TimerModel.Reset</c> clears it — the chain is
        /// Reset() -> Reset(true) -> ResetState(), which does
        /// <c>IsGameTimePaused = false</c> — and <see cref="Timeline.DoStart"/> resets
        /// before every start, so setting it once at construction only holds until the
        /// first run begins.
        ///
        /// With the flag clear, LiveSplitState.SetGameTime stops storing
        /// GameTimePauseTime and Game Time free-runs on real time between our writes,
        /// each write yanking it back. While playing, ours advances at about 1x too and
        /// the drift is invisible; the moment the clock is held — the pause menu with
        /// "exclude pause time" on, or a loading screen — ours is frozen while
        /// LiveSplit's creeps forward, and the display flickers between the two.
        /// </summary>
        public void SetGameTime(double seconds) => Guarded(() =>
        {
            _state.IsGameTimePaused = true;
            _state.SetGameTime(TimeSpan.FromSeconds(Math.Max(0, seconds)));
        });

        public void Note(string message) => _note?.Invoke(message);

        /// <summary>Runs an action with the reentrancy guard raised. Exceptions are
        /// swallowed on purpose: another plugin throwing inside its own OnStart handler
        /// must not take this timeline down with it.</summary>
        public void Guarded(Action action)
        {
            bool outer = Suppressed;
            Suppressed = true;
            try { action(); }
            catch (Exception e) { _note?.Invoke("LiveSplit rejected a timer command: " + e.Message); }
            finally { Suppressed = outer; }
        }
    }
}
