using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using InfiniteSplitter.Settings;

namespace InfiniteSplitter.UI
{
    /// <summary>The five tab pages. Every control is constructed here rather than in a designer
    /// file — see the note in InfiniteSplitter.csproj.</summary>
    public partial class SettingsControl
    {
        private TabPage BuildTimerPage()
        {
            var page = new TabPage("Timer") { Padding = new Padding(8) };
            var layout = Rows(8);

            _slot.Items.AddRange(new object[] { "auto", "1", "2", "3", "4", "5" });

            layout.Controls.Add(_sendIgt);
            layout.Controls.Add(Labelled("Save slot to read:", _slot));
            layout.Controls.Add(Note("\"auto\" follows the slot you are playing — the same test the pause menu uses."));
            layout.Controls.Add(_pauseOnLoad);
            layout.Controls.Add(_pauseOnPauseMenu);
            layout.Controls.Add(Note("The game's own clock keeps running through loading screens and the pause menu; " +
                                     "it only freezes on the main menu. With either box ticked the number here " +
                                     "stops matching the one the game shows."));
            layout.Controls.Add(_zeroAtStart);
            layout.Controls.Add(_switchToGameTime);

            foreach (var box in new[] { _sendIgt, _pauseOnLoad, _pauseOnPauseMenu, _zeroAtStart, _switchToGameTime })
                box.CheckedChanged += OnEdited;
            _slot.SelectedIndexChanged += OnEdited;

            page.Controls.Add(layout);
            return page;
        }

        private TabPage BuildRunPage()
        {
            var page = new TabPage("Run") { Padding = new Padding(8) };
            var layout = Rows(8);

            _start = new TriggerBox();
            _reset = new TriggerBox();
            _start.Changed += OnEdited;
            _reset.Changed += OnEdited;

            layout.Controls.Add(new Label { AutoSize = true, Text = "Start the run when:" });
            layout.Controls.Add(_start);
            layout.Controls.Add(new Label { AutoSize = true, Text = "Throw the run away when:" });
            layout.Controls.Add(_reset);
            layout.Controls.Add(Note("\"never\" keeps a run alive across save / quit to menu / load — the game " +
                                     "freezes its clock on the menu and re-reads every slot from disk, so the timer " +
                                     "picks up exactly where it was. A new game still restarts it.\n" +
                                     "A reset condition must read false once before it can fire: a run begins while " +
                                     "the game is still on the menu scene."));
            layout.Controls.Add(_autoStart);
            layout.Controls.Add(_autoSplit);
            layout.Controls.Add(Flow(_autoReset, _restartOnNewGame));

            foreach (var box in new[] { _autoStart, _autoSplit, _autoReset, _restartOnNewGame })
                box.CheckedChanged += OnEdited;

            page.Controls.Add(layout);
            return page;
        }

        private TabPage BuildSplitsPage()
        {
            var page = new TabPage("Splits") { Padding = new Padding(8) };
            var layout = Rows(4);
            layout.RowStyles[0] = new RowStyle(SizeType.Percent, 100);

            _splits.Columns.Add("#", 28);
            _splits.Columns.Add("Name", 120);
            _splits.Columns.Add("Trigger", 240);
            _splits.Columns.Add("", 26);
            _splits.SelectedIndexChanged += OnSplitSelected;
            layout.Controls.Add(_splits);

            _splitTrigger = new TriggerBox();
            foreach (var preset in Presets.All) _presets.Items.Add(preset);
            _presets.SelectedIndexChanged += (s, e) =>
            {
                if (_loading || !(_presets.SelectedItem is Preset preset)) return;
                _splitName.Text = preset.Name;
                _splitTrigger.Value = preset.Trigger;
            };

            layout.Controls.Add(Flow(new Label { AutoSize = true, Text = "Name:", Padding = new Padding(0, 6, 0, 0) },
                                     _splitName,
                                     new Label { AutoSize = true, Text = "Preset:", Padding = new Padding(12, 6, 0, 0) },
                                     _presets));
            layout.Controls.Add(_splitTrigger);
            layout.Controls.Add(Flow(
                Button("Add", (s, e) => AddSplit()),
                Button("Update", (s, e) => UpdateSplit()),
                Button("Remove", (s, e) => RemoveSplit()),
                Button("Up", (s, e) => MoveSplit(-1)),
                Button("Down", (s, e) => MoveSplit(1)),
                Button("Duplicate", (s, e) => DuplicateSplit()),
                Button("Restore defaults", (s, e) => RestoreDefaults()),
                _countWarning));

            page.Controls.Add(layout);
            return page;
        }

        private TabPage BuildAdvancedPage()
        {
            var page = new TabPage("Advanced") { Padding = new Padding(8) };
            var layout = Rows(4);
            layout.RowStyles[1] = new RowStyle(SizeType.Percent, 100);

            layout.Controls.Add(Note("The whole configuration as text. Paste a .cfg here to import it; " +
                                     "[server], [hud] and [hotkeys] sections are accepted and ignored."));
            layout.Controls.Add(_ini);
            layout.Controls.Add(_iniProblems);
            layout.Controls.Add(Flow(
                Button("Apply", (s, e) => ApplyIni()),
                Button("Revert", (s, e) => LoadFromModel())));

            page.Controls.Add(layout);
            return page;
        }

        /// <summary>Diagnostics rather than configuration, so it sits last.</summary>
        private TabPage BuildDebugPage()
        {
            var page = new TabPage("Debug") { Padding = new Padding(8) };
            var layout = Rows(3);
            layout.Controls.Add(_statusLine);
            layout.Controls.Add(Note("The game runs completely unmodified — nothing is installed into " +
                                     "its folder and no launch flag is needed. This reads its memory " +
                                     "from outside the process."));
            layout.Controls.Add(_diagnostics);
            layout.RowStyles[2] = new RowStyle(SizeType.Percent, 100);
            page.Controls.Add(layout);
            return page;
        }
    }
}
