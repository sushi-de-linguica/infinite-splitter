namespace InfiniteSplitter.Game
{
    /// <summary>
    /// Turns "the player just started a new game" into a one-tick pulse. Two signals,
    /// because neither is sufficient alone:
    ///
    /// 1. The run list gets one longer. It covers every way a run can be started, and
    ///    being a level rather than an edge, a slow poll cannot miss it.
    /// 2. A slot clock reading ~0 while the game reports itself paused on the menu.
    ///    Needed because the first new game of a session is invisible to signal 1: the
    ///    counter goes from unavailable straight to 1, with no baseline to have risen
    ///    from. A loaded save always carries its own elapsed time, so this state is
    ///    unambiguous.
    /// </summary>
    public sealed class NewGameDetector
    {
        private int _lastRunCount = -1;
        private bool _clockArmed = true;

        /// <summary>True for exactly one tick after a new game began.</summary>
        public bool Pulse { get; private set; }

        /// <summary>Which signal produced the last pulse, for the Debug tab.</summary>
        public string Source { get; private set; } = "none";

        /// <summary>Whether the run counter is readable at all this session.</summary>
        public bool RunLoggerAvailable => _lastRunCount >= 0;

        public void Reset()
        {
            _lastRunCount = -1;
            _clockArmed = true;
            Pulse = false;
            Source = "none";
        }

        /// <summary>Called once per tick, before the timeline runs.</summary>
        public void Update(int runCount, double rawIgt, bool timePaused, bool gameManagerAlive)
        {
            Pulse = false;
            if (!gameManagerAlive) return;

            // Armed/disarmed rather than edge-triggered: the fresh-clock state persists
            // for dozens of frames before the first gameplay scene comes up, and would
            // otherwise pulse on every one.
            bool freshClock = timePaused && rawIgt < 1.0;
            if (!freshClock)
            {
                _clockArmed = true;
            }
            else if (_clockArmed)
            {
                _clockArmed = false;
                _lastRunCount = runCount;      // adopt whatever the counter says now
                Pulse = true;
                Source = "clock reset on the menu";
                return;
            }

            // The RunLogger signal, exact once the class has been touched.
            if (runCount < 0) return;

            if (_lastRunCount < 0)
            {
                _lastRunCount = runCount;   // first readable value: no baseline, no pulse
                return;
            }

            if (runCount > _lastRunCount)
            {
                _lastRunCount = runCount;
                Pulse = true;
                Source = "RunLogger run count";
            }
            else if (runCount < _lastRunCount)
            {
                _lastRunCount = runCount;      // the file was cleared out from under us
            }
        }
    }
}
