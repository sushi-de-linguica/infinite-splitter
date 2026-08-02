using System;
using System.Collections.Generic;

namespace InfiniteSplitter.Core
{
    /// <summary>What the timeline asks of LiveSplit. Implemented by the TCP link
    /// in the game and by a recorder in tools/cfgcheck.</summary>
    public interface ILink
    {
        void StartTimer();
        void Split();
        void Undo();
        void ResetRun();
        void SetGameTime(double seconds);
        void Note(string message);
    }

    public enum Phase { Idle, Running, Ended }

    /// <summary>
    /// Walks the [splits] list in order and decides when to talk to LiveSplit.
    ///
    /// At most one split per tick on purpose: loading a save with half the game's
    /// flags already set would otherwise burn through the whole split list in a
    /// single frame, and one-per-frame at least leaves a readable trail in the log.
    /// </summary>
    public sealed class Timeline
    {
        private readonly Cfg _cfg;
        private readonly ILink _link;

        public Phase Phase { get; private set; } = Phase.Idle;
        public int Index { get; private set; }
        public int Count => _cfg.Splits.Count;

        /// <summary>
        /// The clock at the final split, to be held for as long as the run is
        /// <see cref="Phase.Ended"/>. The game's own clock does not stop there: it only
        /// freezes on the main menu, so it carries on through the ending cutscene, the
        /// credits and the results screen. A finished run has to stop following it, or
        /// the time on the board keeps climbing after the last split landed.
        /// </summary>
        public double FinalIgt { get; private set; }

        /// <summary>
        /// False while the reset condition is still true from before the run
        /// started. See the guard in <see cref="Tick"/>.
        /// </summary>
        private bool _resetArmed;

        /// <summary>Name of the split the run is currently working towards.</summary>
        public string Pending => Index >= 0 && Index < _cfg.Splits.Count ? _cfg.Splits[Index].Name : "-";

        public Timeline(Cfg cfg, ILink link)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _link = link ?? throw new ArgumentNullException(nameof(link));
        }

        public void Tick(IProbe probe)
        {
            // A new game always outranks everything else: it is an unambiguous
            // edge (a one-frame pulse straight off Menu.PlayGame), so it can
            // safely restart a run that is already going. That is what lets a
            // config say `reset = never` — the run then survives a save / quit
            // to menu / load cycle, and only a genuinely new run clears it.
            if (Phase == Phase.Running && _cfg.RestartOnNewGame && probe.NewGame)
            {
                DoStart("new game started while a run was in progress");
                return;
            }

            // Reset next: quitting to the menu on the same frame a split would
            // fire should abandon the run, not record it.
            //
            // But the reset condition has to go false once before it can fire.
            // A run starts while the reset condition is still true: the menu's
            // PlayGame hands us the start pulse, and Menu.LoadScene then waits
            // 0.3 s of real time before it even calls LoadSceneAsync, so the
            // active scene is still "menu" for dozens of frames. Without this
            // arming step the default 'reset = scene:menu' kills the run one
            // frame after starting it.
            if (Phase == Phase.Running && _cfg.AutoReset && _cfg.Reset != null)
            {
                if (!_cfg.Reset.Eval(probe))
                {
                    _resetArmed = true;
                }
                else if (_resetArmed)
                {
                    DoReset("reset trigger: " + _cfg.Reset.Source);
                    return;
                }
            }

            if (Phase != Phase.Running && _cfg.AutoStart && _cfg.Start != null && _cfg.Start.Eval(probe))
            {
                DoStart("start trigger: " + _cfg.Start.Source);
                return;
            }

            if (Phase != Phase.Running || !_cfg.AutoSplit) return;

            if (Index < _cfg.Splits.Count)
            {
                var split = _cfg.Splits[Index];
                if (split.Trigger.Eval(probe))
                    DoSplit(probe.Igt, "trigger: " + split.Raw);
            }
        }

        public void DoStart(string why)
        {
            // LiveSplit's Model.Start() is a no-op unless the timer is not running,
            // so a finished or half-finished previous attempt has to be cleared.
            _link.ResetRun();
            _link.SetGameTime(0);
            _link.StartTimer();
            Phase = Phase.Running;
            Index = 0;
            _resetArmed = false;
            FinalIgt = 0;
            _link.Note($"start ({why})");
        }

        public void DoSplit(double igt, string why)
        {
            string name = Pending;
            _link.SetGameTime(igt);
            _link.Split();
            Index++;
            _link.Note($"split {Index}/{Count} '{name}' at {Trigger.FormatTime(igt)} ({why})");
            if (Index >= Count && Count > 0)
            {
                Phase = Phase.Ended;
                FinalIgt = igt;
                _link.Note("run finished — autoreset is off until the next start");
            }
        }

        public void DoUndo(string why)
        {
            if (Index <= 0) return;
            _link.Undo();
            Index--;
            if (Phase == Phase.Ended) { Phase = Phase.Running; FinalIgt = 0; }
            _link.Note($"undo -> {Index}/{Count} ({why})");
        }

        public void DoReset(string why)
        {
            _link.ResetRun();
            Phase = Phase.Idle;
            Index = 0;
            _resetArmed = false;
            FinalIgt = 0;
            _link.Note($"reset ({why})");
        }
    }
}
