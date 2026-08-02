using System;
using InfiniteSplitter.Core;

namespace InfiniteSplitter.Game
{
    /// <summary>
    /// The game's own clock: one counter per save slot, mirrored rather than
    /// re-integrated from our own delta, which is what keeps LiveSplit and the
    /// in-game timer agreeing down to the rounding.
    ///
    /// Mirroring is the contract, with one deliberate departure: the game's clock
    /// runs through loading, so with <see cref="RemoveLoads"/> on that time is carved
    /// back out. See docs/architecture.md. Everything else is followed as-is,
    /// including a backwards jump when a save is loaded.
    /// </summary>
    public sealed class IgtSource
    {
        /// <summary>Generous enough to survive a stalled poll during a heavy load, far
        /// below the minutes a slot swap moves the counter by.</summary>
        private const double MaxCredibleTick = 2.0;

        private double _baseline;
        private double _lastRaw;
        private double _excluded;
        private bool _primed;

        /// <summary>Carve loading out of the total. See <see cref="IsLoading"/>.</summary>
        public bool RemoveLoads = true;

        /// <summary>Which slot the last read came from; 0 before the game is up.</summary>
        public int ActiveSlot { get; private set; }

        /// <summary>The game's counter, as read — what the game's own pause menu shows.</summary>
        public double Raw { get; private set; }

        /// <summary>What LiveSplit gets: <see cref="Raw"/> less the loads and the baseline.</summary>
        public double Value { get; private set; }

        /// <summary>How much has been carved out since the run started.</summary>
        public double Excluded => _excluded;

        /// <summary>True while this tick is being carved out.</summary>
        public bool Excluding { get; private set; }

        public void Rebase(bool zeroAtStart, GameSnapshot snapshot, GameFields fields)
        {
            Raw = ReadRaw(snapshot, fields);
            _lastRaw = Raw;
            _excluded = 0;
            _primed = true;
            _baseline = zeroAtStart ? Raw : 0;
            Value = Math.Max(0, Raw - _baseline);
        }

        public void Update(GameSnapshot snapshot, GameFields fields)
        {
            double raw = ReadRaw(snapshot, fields);
            if (!_primed) { Rebase(false, snapshot, fields); return; }

            double delta = raw - _lastRaw;
            _lastRaw = raw;
            Excluding = IsLoading(snapshot, fields);

            if (Excluding && delta > 0 && delta < MaxCredibleTick) _excluded += delta;

            Raw = raw;

            Value = Math.Max(0, Raw - _excluded - _baseline);
        }

        /// <summary>
        /// The game reports its two kinds of load separately, and only calls one of
        /// them loading: a save being read (from the menu or after a death), and a
        /// scene transition (a portal, cross-scene fast travel, quitting to the menu).
        ///
        /// The transition signal has to be discarded while the menu is up, where it
        /// reads the previous scene's and stays set. The clock is frozen there anyway,
        /// so nothing is miscounted either way, but <see cref="Excluding"/> would
        /// otherwise misreport what it is doing.
        /// </summary>
        private bool IsLoading(GameSnapshot snapshot, GameFields fields)
        {
            if (!RemoveLoads || snapshot == null || fields == null) return false;

            if (fields.Loading != null && snapshot.Bool(fields.Loading)) return true;

            bool inMenu = fields.TimePaused != null && snapshot.Bool(fields.TimePaused);
            return !inMenu && snapshot.SceneLoadInFlight;
        }

        private double ReadRaw(GameSnapshot snapshot, GameFields fields)
        {
            if (snapshot == null || !snapshot.GameManagerAlive || fields == null) return Raw;

            int slot = ResolveSlot(snapshot, fields);
            ActiveSlot = slot;

            var hour = fields.Hour[slot];
            var minutes = fields.Minutes[slot];
            var seconds = fields.Seconds[slot];
            if (hour == null || minutes == null || seconds == null) return Raw;

            // The seconds counter already carries the milliseconds.
            return snapshot.Int32(hour) * 3600.0
                 + snapshot.Int32(minutes) * 60.0
                 + snapshot.Single(seconds);
        }

        /// <summary>
        /// Picks the slot the player is on, using the same state the in-game timer
        /// reads. That state goes briefly ambiguous while the menu's slot list is up,
        /// hence the fall back to the last known slot rather than to 1 — snapping back
        /// would report some other save's time.
        /// </summary>
        private int ResolveSlot(GameSnapshot snapshot, GameFields fields)
        {
            for (int slot = 1; slot <= 5; slot++)
                if (fields.CanSave[slot] != null && snapshot.Bool(fields.CanSave[slot]))
                    return slot;

            return ActiveSlot != 0 ? ActiveSlot : 1;
        }

        public string Format() => Trigger.FormatTime(Value);
    }
}
