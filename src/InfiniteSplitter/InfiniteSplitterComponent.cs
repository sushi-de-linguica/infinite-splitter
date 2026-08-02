using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;
using InfiniteSplitter.Core;
using InfiniteSplitter.Game;
using InfiniteSplitter.Settings;
using InfiniteSplitter.UI;
using LiveSplit.Model;
using LiveSplit.UI;
using LiveSplit.UI.Components;

namespace InfiniteSplitter
{
    /// <summary>
    /// The LiveSplit component. ComponentCategory.Control, so it draws nothing into
    /// LiveSplit's window: every Draw method is empty and every size is zero, and all
    /// the work happens in Update, which LiveSplit calls once per frame.
    /// </summary>
    public class InfiniteSplitterComponent : IComponent
    {
        public const string Name = InfiniteSplitterFactory.CanonicalName;

        /// <summary>How long to wait before looking for the game again. Without it, a
        /// LiveSplit left open all day scans the process list 60 times a second.</summary>
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

        /// <summary>
        /// How many ticks in a row have to fault before <see cref="RetryDelay"/> is
        /// applied to a fault raised *inside* the tick rather than by the attach.
        ///
        /// Backing off on the first one costs five seconds of not reading the clock and
        /// not testing a single split condition — worse than the glitch it hides, and
        /// invisible, since the run carries on. Only a tick that is broken every frame
        /// is worth throttling, and by then it is a standing fault, not a glitch.
        /// </summary>
        private const int FaultsBeforeBackOff = 30;

        /// <summary>
        /// Timeline notes and tick faults, newest last — the component's only log, shown
        /// in the Debug tab. Every note used to be written to a field nothing read, so a
        /// split, a reset or a five-second hole in the clock left no trace at all.
        /// </summary>
        private readonly List<string> _log = new List<string>();
        private const int LogLines = 12;

        private int _consecutiveFaults;

        private readonly LiveSplitState _state;
        private readonly TimerModel _timerModel;
        private readonly TimerModelLink _link;
        private readonly Infinitevania _game;
        private readonly MemoryProbe _probe;
        private readonly IgtSource _igt = new IgtSource();
        private readonly SettingsControl _settingsControl;

        private SettingsViewModel _settings;
        private Cfg _cfg;
        private Timeline _timeline;
        private bool _rebuildPending;
        private DateTime _lastFailedRefresh = DateTime.MinValue;

        public InfiniteSplitterComponent(LiveSplitState state)
        {
            _state = state;

            // LiveSplit must never advance game time on its own — it is written from
            // the game's own clock on every tick. This only covers the window before
            // the first run: TimerModel.Reset clears the flag, so TimerModelLink
            // re-asserts it on every write.
            _state.IsGameTimePaused = true;

            _timerModel = new TimerModel { CurrentState = state };
            _link = new TimerModelLink(_timerModel, _state, Note);

            _game = new Infinitevania();
            _probe = new MemoryProbe(_game) { Log = message => Note("config: " + message) };

            _settings = SettingsViewModel.CreateDefault();
            _cfg = CfgBridge.ToCfg(_settings, out _);
            _timeline = new Timeline(_cfg, _link);

            _settingsControl = new SettingsControl { Model = _settings };
            _settingsControl.SettingsChanged += (sender, e) => _rebuildPending = true;

            _state.OnStart += OnLiveSplitStart;
            _state.OnReset += OnLiveSplitReset;
        }

        public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
        {
            try
            {
                if (_rebuildPending) Rebuild();
                if (DateTime.Now < _lastFailedRefresh + RetryDelay) return;

                var refresh = _game.TryRefresh();
                if (refresh.IsErr)
                {
                    if (ShouldBackOff(refresh.Error.Reason)) _lastFailedRefresh = DateTime.Now;
                    SetStatus(refresh.Error.Describe());
                    return;
                }

                _game.TakeSnapshot();
                var snapshot = _game.Snapshot;
                var fields = _game.Fields;

                _igt.Update(snapshot, fields);

                bool timePaused = fields.TimePaused != null && snapshot.Bool(fields.TimePaused);
                _probe.NewGameDetector.Update(snapshot.RunCount, _igt.Raw, timePaused, snapshot.GameManagerAlive);
                _probe.Bind(snapshot, fields, _igt.Value);

                var before = _timeline.Phase;
                _link.Guarded(() => _timeline.Tick(_probe));

                if (_timeline.Phase == Phase.Running && before != Phase.Running)
                {
                    _igt.Rebase(_cfg.ZeroAtStart, snapshot, fields);
                    _probe.OnRunStarted();
                }

                // Written on every tick even once the run is over, because that is what
                // holds the claim on Game Time: stop writing and LiveSplit free-runs it
                // on real time between our writes. What a finished run changes is the
                // value, not the cadence — it holds the clock at its last split instead
                // of following the game's, which keeps counting through the ending.
                if (_cfg.SendIgt && _timeline.Phase != Phase.Idle)
                    _link.SetGameTime(_timeline.Phase == Phase.Ended ? _timeline.FinalIgt : _igt.Value);

                _probe.ClearPulses();
                _consecutiveFaults = 0;
                SetStatus($"{_igt.Format()}  slot {_igt.ActiveSlot}  {_timeline.Phase.ToString().ToLowerInvariant()}" +
                          (_timeline.Phase == Phase.Running
                              ? $"  split {_timeline.Index + 1}/{_timeline.Count} -> {_timeline.Pending}"
                              : ""));
            }
            catch (Exception e)
            {
                // Nothing may escape into LiveSplit's render loop: an exception here
                // takes down the whole timer, not just this component.
                RecordFault(e);
            }
        }

        private void RecordFault(Exception e)
        {
            _consecutiveFaults++;

            string what = e.GetType().Name + ": " + e.Message;
            Note("fault: " + what);

            if (_consecutiveFaults >= FaultsBeforeBackOff) _lastFailedRefresh = DateTime.Now;
            SetStatus($"error (x{_consecutiveFaults}): {what}");
        }

        /// <summary>
        /// Rebuilds the runtime configuration after the user edits something. The new
        /// Timeline is deliberate: phase and split index describe a run under the old
        /// configuration, and carrying them over could leave the run pointing at a
        /// split that no longer exists.
        /// </summary>
        private void Rebuild()
        {
            _rebuildPending = false;
            _settings = _settingsControl.Model;
            _cfg = CfgBridge.ToCfg(_settings, out var problems);
            _timeline = new Timeline(_cfg, _link);

            if (_settings.SwitchToGameTime)
                _state.CurrentTimingMethod = TimingMethod.GameTime;

            foreach (var problem in problems) Note("config: " + problem);
        }

        private static bool ShouldBackOff(RefreshErrorReason reason) => reason switch
        {
            RefreshErrorReason.ProcessNotRunning or RefreshErrorReason.ProcessExited or
            RefreshErrorReason.AccessDenied or RefreshErrorReason.HostNot64Bit or
            RefreshErrorReason.TargetNot64Bit or RefreshErrorReason.MonoModuleNotFound or
            RefreshErrorReason.ClassNotFound or RefreshErrorReason.LayoutMismatch => true,

            // RootDomainNull and AssemblyNotFound mean the game is still starting:
            // retry promptly rather than making the player wait.
            _ => false,
        };

        private void OnLiveSplitStart(object sender, EventArgs e)
        {
            if (_link.Suppressed) return;      // this was us; see TimerModelLink
            _timeline.DoStart("started by hand in LiveSplit");
        }

        private void OnLiveSplitReset(object sender, TimerPhase phase)
        {
            if (_link.Suppressed) return;
            _timeline.DoReset("reset by hand in LiveSplit");
        }

        /// <summary>Appends to the Debug tab's log. Consecutive repeats collapse: a fault
        /// raised on every tick would otherwise flush the whole ring in a fifth of a
        /// second and hide what led up to it.</summary>
        private void Note(string message)
        {
            if (_log.Count > 0 && _log[_log.Count - 1] == message) return;
            if (_log.Count >= LogLines) _log.RemoveAt(0);
            _log.Add(message);
        }

        private void SetStatus(string message) => _settingsControl?.SetStatus(message, _game, _probe, _log);

        public Control GetSettingsControl(LayoutMode mode) => _settingsControl;

        public XmlNode GetSettings(XmlDocument document)
        {
            var root = document.CreateElement("Settings");
            try
            {
                var fragment = document.CreateDocumentFragment();
                fragment.InnerXml = _settingsControl.Model.Serialize();

                // Drop the XML declaration the serializer emits; it cannot appear
                // inside LiveSplit's own document.
                foreach (XmlNode child in fragment.ChildNodes)
                    if (child.NodeType == XmlNodeType.Element)
                    {
                        root.AppendChild(child.CloneNode(true));
                        break;
                    }
            }
            catch (Exception e)
            {
                Note("could not save settings: " + e.Message);
            }
            return root;
        }

        public void SetSettings(XmlNode settings)
        {
            var model = SettingsViewModel.FromXmlNode(settings);
            foreach (var problem in model.LoadProblems) Note("settings: " + problem);

            _settingsControl.Model = model;
            _settings = model;
            _rebuildPending = true;
        }

        public string ComponentName => Name;

        public IDictionary<string, Action> ContextMenuControls => new Dictionary<string, Action>();

        public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
        {
            // Control components do not draw.
        }

        public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
        {
            // Control components do not draw.
        }

        public float HorizontalWidth => 0;
        public float VerticalHeight => 0;
        public float MinimumWidth => 0;
        public float MinimumHeight => 0;
        public float PaddingTop => 0;
        public float PaddingBottom => 0;
        public float PaddingLeft => 0;
        public float PaddingRight => 0;

        public void Dispose()
        {
            _state.OnStart -= OnLiveSplitStart;
            _state.OnReset -= OnLiveSplitReset;
            _game?.Dispose();
            _settingsControl?.Dispose();
        }
    }
}
