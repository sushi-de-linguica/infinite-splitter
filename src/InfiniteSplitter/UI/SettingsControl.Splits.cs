using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using InfiniteSplitter.Settings;

namespace InfiniteSplitter.UI
{
    /// <summary>The split list: what the Add / Update / Remove / Up / Down / Duplicate buttons do.
    /// Each one edits the model and raises, so the component rebuilds its Timeline.</summary>
    public partial class SettingsControl
    {
        private void RefreshSplitList()
        {
            int selected = _splits.SelectedIndices.Count > 0 ? _splits.SelectedIndices[0] : -1;

            _splits.BeginUpdate();
            _splits.Items.Clear();
            for (int i = 0; i < _model.Splits.Count; i++)
            {
                var split = _model.Splits[i];
                var errors = new List<string>();
                bool ok = Core.Trigger.Parse(split.Trigger, errors) != null && errors.Count == 0;

                var item = new ListViewItem(new[] { (i + 1).ToString(), split.Name, split.Trigger, ok ? "✓" : "✗" });
                if (!ok) item.ForeColor = Color.FromArgb(176, 0, 0);
                _splits.Items.Add(item);
            }
            _splits.EndUpdate();

            if (selected >= 0 && selected < _splits.Items.Count)
                _splits.Items[selected].Selected = true;

            _countWarning.Text = LiveSplitSegmentCount > 0 && LiveSplitSegmentCount != _model.Splits.Count
                ? $"your splits file has {LiveSplitSegmentCount} segment(s), this list has {_model.Splits.Count}"
                : "";
        }

        private void OnSplitSelected(object sender, EventArgs e)
        {
            if (_loading || _splits.SelectedIndices.Count == 0) return;
            var split = _model.Splits[_splits.SelectedIndices[0]];

            _loading = true;
            try { _splitName.Text = split.Name; _splitTrigger.Value = split.Trigger; }
            finally { _loading = false; }
        }

        private void AddSplit()
        {
            if (_splitName.Text.Trim().Length == 0) return;
            _model.Splits.Add(new SplitEntry(_splitName.Text.Trim(), _splitTrigger.Value));
            RefreshSplitList();
            Raise();
        }

        private void UpdateSplit()
        {
            if (_splits.SelectedIndices.Count == 0) return;
            var split = _model.Splits[_splits.SelectedIndices[0]];
            split.Name = _splitName.Text.Trim();
            split.Trigger = _splitTrigger.Value;
            RefreshSplitList();
            Raise();
        }

        private void RemoveSplit()
        {
            if (_splits.SelectedIndices.Count == 0) return;
            _model.Splits.RemoveAt(_splits.SelectedIndices[0]);
            RefreshSplitList();
            Raise();
        }

        private void MoveSplit(int delta)
        {
            if (_splits.SelectedIndices.Count == 0) return;
            int from = _splits.SelectedIndices[0];
            int to = from + delta;
            if (to < 0 || to >= _model.Splits.Count) return;

            var split = _model.Splits[from];
            _model.Splits.RemoveAt(from);
            _model.Splits.Insert(to, split);
            RefreshSplitList();
            if (to < _splits.Items.Count) _splits.Items[to].Selected = true;
            Raise();
        }

        private void DuplicateSplit()
        {
            if (_splits.SelectedIndices.Count == 0) return;
            int at = _splits.SelectedIndices[0];
            _model.Splits.Insert(at + 1, _model.Splits[at].Copy());
            RefreshSplitList();
            Raise();
        }

        private void RestoreDefaults()
        {
            _model.Splits = SettingsViewModel.DefaultSplits();
            RefreshSplitList();
            Raise();
        }
    }
}
