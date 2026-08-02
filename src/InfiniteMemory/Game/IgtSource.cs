using System;
using InfiniteSplitter.Core;

namespace InfiniteSplitter.Game
{
    /// <summary>
    /// The game's own clock: one counter per save slot, mirrored rather than
    /// re-integrated from our own delta, which is what keeps LiveSplit and the
    /// in-game timer showing the same number down to the rounding.
    ///
    /// Mirroring is the whole contract — nothing is carved out of the total, so a
    /// backwards jump (a save loaded, a new game zeroing the counters) is simply
    /// followed. The one adjustment is <see cref="Rebase"/>'s baseline.
    /// </summary>
    public sealed class IgtSource
    {
        private double _baseline;

        /// <summary>Which slot the last read came from; 0 before the game is up.</summary>
        public int ActiveSlot { get; private set; }

        /// <summary>The game's counter, as read.</summary>
        public double Raw { get; private set; }

        /// <summary>What LiveSplit gets: <see cref="Raw"/> less the baseline.</summary>
        public double Value { get; private set; }

        public void Rebase(bool zeroAtStart, GameSnapshot snapshot, GameFields fields)
        {
            Raw = ReadRaw(snapshot, fields);
            _baseline = zeroAtStart ? Raw : 0;
            Value = Math.Max(0, Raw - _baseline);
        }

        public void Update(GameSnapshot snapshot, GameFields fields)
        {
            Raw = ReadRaw(snapshot, fields);

            // Clamped because a save loaded after the baseline was taken rewinds the
            // counter below it; the run's own clock cannot go negative.
            Value = Math.Max(0, Raw - _baseline);
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
