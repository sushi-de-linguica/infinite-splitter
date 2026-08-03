using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using InfiniteSplitter.Game;
using InfiniteSplitter.Settings;

namespace InfiniteSplitter.UI
{
    /// <summary>
    /// The control LiveSplit hosts in its layout-settings dialog. Built entirely in
    /// code — no designer file, no .resx, no images — which is what keeps the project
    /// buildable from WSL (see the note in InfiniteSplitter.csproj).
    /// </summary>
    public partial class SettingsControl : UserControl
    {
        private SettingsViewModel _model = SettingsViewModel.CreateDefault();
        private bool _loading;

        /// <summary>
        /// Controls whose width has to follow the dialog: wrapping labels and button
        /// rows. WinForms will not wrap either without a concrete MaximumSize.Width,
        /// and a hardcoded one clips the text in a narrower dialog.
        /// </summary>
        private readonly List<Control> _wrapping = new List<Control>();

        /// <summary>Raised when the user changes something, so the component can rebuild
        /// its Cfg and Timeline.</summary>
        public event EventHandler SettingsChanged;

        // status
        private readonly Label _statusLine = new Label { AutoSize = true, Text = "starting…" };
        private readonly TextBox _diagnostics;

        // timer
        private readonly CheckBox _sendIgt = new CheckBox { AutoSize = true, Text = "Feed LiveSplit's Game Time from the game's clock" };
        private readonly CheckBox _removeLoads = new CheckBox { AutoSize = true, Text = "Remove loading time from the clock" };
        private readonly CheckBox _zeroAtStart = new CheckBox { AutoSize = true, Text = "Start the clock from zero, whatever the save held" };
        private readonly CheckBox _switchToGameTime = new CheckBox { AutoSize = true, Text = "Switch LiveSplit's timer to Game Time on attach" };

        // run
        private TriggerBox _start;
        private TriggerBox _reset;
        private readonly CheckBox _autoStart = new CheckBox { AutoSize = true, Text = "Start automatically" };
        private readonly CheckBox _autoSplit = new CheckBox { AutoSize = true, Text = "Split automatically" };
        private readonly CheckBox _autoReset = new CheckBox { AutoSize = true, Text = "Reset automatically" };
        private readonly CheckBox _restartOnNewGame = new CheckBox { AutoSize = true, Text = "A new game restarts a run in progress" };

        // splits
        private readonly ListView _splits = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            Dock = DockStyle.Fill,
        };
        private readonly TextBox _splitName = new TextBox { Width = 120 };
        private TriggerBox _splitTrigger;
        private readonly ComboBox _presets = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        private readonly Label _countWarning = new Label { AutoSize = true, ForeColor = Color.FromArgb(176, 96, 0) };

        // advanced
        private readonly TextBox _ini = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 8.25f),
        };
        private readonly Label _iniProblems = new Label { AutoSize = true, ForeColor = Color.FromArgb(176, 0, 0) };

        public SettingsControl()
        {
            Name = "InfiniteSplitterSettings";
            // LiveSplit hosts this in a fixed-size dialog: Dock=Fill makes the content
            // follow that width, where a hard Size puts a scrollbar under the whole
            // panel and clips the explanatory text.
            Dock = DockStyle.Fill;
            AutoScroll = false;
            Size = new Size(460, 400);
            Resize += (s, e) => ReflowWrappingControls();

            _diagnostics = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 8.25f),
            };

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildTimerPage());
            tabs.TabPages.Add(BuildRunPage());
            tabs.TabPages.Add(BuildSplitsPage());
            tabs.TabPages.Add(BuildAdvancedPage());
            tabs.TabPages.Add(BuildDebugPage());   // last: diagnostics, not configuration
            Controls.Add(tabs);

            LoadFromModel();
            ReflowWrappingControls();
        }

        public SettingsViewModel Model
        {
            get { return _model; }
            set
            {
                _model = value ?? SettingsViewModel.CreateDefault();
                LoadFromModel();
            }
        }

        /// <summary>Segment count of the loaded LiveSplit run, for the mismatch warning.
        /// Set by the component; 0 means unknown.</summary>
        public int LiveSplitSegmentCount { get; set; }

        private void LoadFromModel()
        {
            _loading = true;
            try
            {
                _sendIgt.Checked = _model.SendIgt;
                _removeLoads.Checked = _model.RemoveLoads;
                _zeroAtStart.Checked = _model.ZeroAtStart;
                _switchToGameTime.Checked = _model.SwitchToGameTime;

                if (_start != null) _start.Value = _model.Start;
                if (_reset != null) _reset.Value = _model.Reset;
                _autoStart.Checked = _model.AutoStart;
                _autoSplit.Checked = _model.AutoSplit;
                _autoReset.Checked = _model.AutoReset;
                _restartOnNewGame.Checked = _model.RestartOnNewGame;

                RefreshSplitList();
                _ini.Text = CfgBridge.ToIni(_model).Replace("\n", "\r\n");
                _iniProblems.Text = "";
            }
            finally { _loading = false; }
        }

        private void OnEdited(object sender, EventArgs e)
        {
            if (_loading) return;

            _model.SendIgt = _sendIgt.Checked;
            _model.RemoveLoads = _removeLoads.Checked;
            _model.ZeroAtStart = _zeroAtStart.Checked;
            _model.SwitchToGameTime = _switchToGameTime.Checked;

            _model.Start = _start?.Value ?? _model.Start;
            _model.Reset = _reset?.Value ?? _model.Reset;
            _model.AutoStart = _autoStart.Checked;
            _model.AutoSplit = _autoSplit.Checked;
            _model.AutoReset = _autoReset.Checked;
            _model.RestartOnNewGame = _restartOnNewGame.Checked;

            Raise();
        }

        private void Raise()
        {
            if (_loading) return;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyIni()
        {
            var imported = CfgBridge.FromIni(_ini.Text, out var problems);
            _iniProblems.Text = problems.Count == 0 ? "" : string.Join("   ", problems.ToArray());
            if (problems.Exists(p => !p.StartsWith("warning:"))) return;

            _model = imported;
            LoadFromModel();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>How often the Debug tab may be rebuilt. It is diagnostics a human
        /// reads, not a display anything depends on.</summary>
        private static readonly TimeSpan DiagnosticsInterval = TimeSpan.FromMilliseconds(250);

        private DateTime _lastDiagnostics = DateTime.MinValue;

        /// <summary>
        /// Called from LiveSplit's Update, so already on the UI thread — and therefore on
        /// its render loop, which is why so little happens here.
        ///
        /// The report used to be rebuilt and pushed into the TextBox on every frame, for
        /// as long as LiveSplit ran: a report that costs two memory reads to build, a
        /// kilobyte of string, and a full window-text replace, 60 times a second, to show
        /// a tab nobody had open. Now it is throttled, skipped while the page is off
        /// screen, and only assigned when the text actually changed.
        /// </summary>
        public void SetStatus(string status, Infinitevania game, MemoryProbe probe,
                              IgtSource igt = null, IEnumerable<string> log = null)
        {
            if (IsDisposed || !IsHandleCreated) return;

            string line = status ?? "";
            if (_statusLine.Text != line) _statusLine.Text = line;

            if (Parent == null || !Visible) return;

            var now = DateTime.UtcNow;
            if (now - _lastDiagnostics < DiagnosticsInterval) return;
            _lastDiagnostics = now;

            string report = StatusReport.Describe(game, probe, igt, log).Replace("\n", "\r\n");
            if (_diagnostics.Text != report) _diagnostics.Text = report;
        }
    }
}
