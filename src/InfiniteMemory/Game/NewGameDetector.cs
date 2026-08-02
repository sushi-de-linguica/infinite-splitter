namespace InfiniteSplitter.Game
{
    /// <summary>
    /// Turns "the player just started a new game" into a one-tick pulse.
    ///
    /// The signal is the active scene leaving the menu without a save having been
    /// loaded during that visit. Leaving the menu is the only way into a run, and it
    /// is also the frame the game's own clock starts on, so a run started here reads
    /// 0:00.000 rather than picking up a fraction of the fade.
    ///
    /// A save load is told apart by <c>loading</c>, which the game raises a full
    /// second before it starts the scene load and lowers a frame into the destination.
    /// Latching it for the whole menu visit rather than sampling it at the transition
    /// is what makes the distinction independent of the poll rate.
    ///
    /// Two signals this deliberately does not use: a slot clock reading zero (a new
    /// game does not zero every slot, so it both misses and false-positives — and on
    /// the menu, before the game has read the saves off disk, every clock reads zero),
    /// and the run counter rising (unreadable until the first new game of a session,
    /// and it would fire a third of a second earlier than the scene edge — two
    /// different start points across attempts is worse than one late one).
    /// </summary>
    public sealed class NewGameDetector
    {
        private int _lastRunCount = -1;
        private bool _wasTimePaused;
        private bool _loadSeenThisMenuVisit;

        /// <summary>True for exactly one tick after a new game began.</summary>
        public bool Pulse { get; private set; }

        /// <summary>What the last menu exit was, for the Debug tab.</summary>
        public string Source { get; private set; } = "none";

        /// <summary>Whether a save load has been seen since the menu came up. Debug tab.</summary>
        public bool LoadSeenThisMenuVisit => _loadSeenThisMenuVisit;

        /// <summary>Whether the run counter is readable at all this session. Diagnostic
        /// only — it does not feed the pulse.</summary>
        public bool RunLoggerAvailable => _lastRunCount >= 0;

        /// <summary>The run counter as last seen, or -1. Diagnostic only.</summary>
        public int RunCount => _lastRunCount;

        public void Reset()
        {
            _lastRunCount = -1;
            _wasTimePaused = false;
            _loadSeenThisMenuVisit = false;
            Pulse = false;
            Source = "none";
        }

        /// <summary>Called once per tick, before the timeline runs.</summary>
        public void Update(int runCount, bool timePaused, bool loading, bool gameManagerAlive)
        {
            Pulse = false;
            if (runCount >= 0) _lastRunCount = runCount;

            if (!gameManagerAlive)
            {
                _wasTimePaused = false;
                _loadSeenThisMenuVisit = false;
                return;
            }

            if (timePaused)
            {
                if (!_wasTimePaused) _loadSeenThisMenuVisit = false;
                if (loading) _loadSeenThisMenuVisit = true;
            }
            else if (_wasTimePaused)
            {
                if (!_loadSeenThisMenuVisit && !loading)
                {
                    Pulse = true;
                    Source = "new game";
                }
                else
                {
                    Source = "loaded a save";
                }
            }

            _wasTimePaused = timePaused;
        }
    }
}
