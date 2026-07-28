using System;
using InfiniteSplitter.Core;

namespace InfiniteSplitter.Game
{
    /// <summary>
    /// The game's own clock: one counter per save slot, mirrored rather than
    /// re-integrated from our own delta, which is what keeps LiveSplit and the
    /// in-game timer showing the same number down to the rounding.
    ///
    /// The raw counter keeps running through the pause menu, loading screens and
    /// cutscenes, so anything the player wants excluded is carved out here.
    /// </summary>
    public sealed class IgtSource
    {
        private double _lastRaw;
        private double _excluded;
        private double _baseline;
        private bool _primed;

        /// <summary>"auto" or "1".."5".</summary>
        public string SlotSetting = "auto";

        public bool PauseOnLoad;
        public bool PauseOnPauseMenu;

        /// <summary>Which slot the last read came from; 0 before the game is up.</summary>
        public int ActiveSlot { get; private set; }

        /// <summary>The game's counter, before any exclusions.</summary>
        public double Raw { get; private set; }

        /// <summary>What LiveSplit gets.</summary>
        public double Value { get; private set; }

        /// <summary>True while the current tick is being carved out of the total.</summary>
        public bool Paused { get; private set; }

        public void Rebase(bool zeroAtStart, GameSnapshot snapshot, GameFields fields)
        {
            Raw = ReadRaw(snapshot, fields);
            _lastRaw = Raw;
            _excluded = 0;
            _baseline = zeroAtStart ? Raw : 0;
            _primed = true;
            Value = Math.Max(0, Raw - _baseline);
        }

        public void Update(GameSnapshot snapshot, GameFields fields)
        {
            double raw = ReadRaw(snapshot, fields);
            if (!_primed) { Rebase(false, snapshot, fields); return; }

            double delta = raw - _lastRaw;
            _lastRaw = raw;

            Paused = IsPaused(snapshot, fields);

            // A backwards jump means a save was loaded or a new game zeroed the
            // counters; it must never feed the exclusion accumulator.
            if (delta > 0 && Paused) _excluded += delta;

            Raw = raw;
            Value = Math.Max(0, raw - _excluded - _baseline);
        }

        private bool IsPaused(GameSnapshot snapshot, GameFields fields)
        {
            if (PauseOnLoad && fields.Loading != null && snapshot.Bool(fields.Loading)) return true;
            if (PauseOnPauseMenu && fields.UiPaused != null && snapshot.UIManagerAlive &&
                snapshot.BoolUI(fields.UiPaused)) return true;
            return false;
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
            if (SlotSetting != "auto" && int.TryParse(SlotSetting, out int forced) && forced >= 1 && forced <= 5)
                return forced;

            for (int slot = 1; slot <= 5; slot++)
                if (fields.CanSave[slot] != null && snapshot.Bool(fields.CanSave[slot]))
                    return slot;

            return ActiveSlot != 0 ? ActiveSlot : 1;
        }

        public string Format() => Trigger.FormatTime(Value);
    }
}
