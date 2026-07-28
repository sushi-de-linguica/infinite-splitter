using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using InfiniteSplitter.Core;

namespace InfiniteSplitter.UI
{
    /// <summary>
    /// A text box for a trigger expression that validates as you type. The whole cost
    /// of choosing an expression language over a structured editor is the risk of a
    /// typo, and running the parser on every keystroke removes it — a bad expression
    /// cannot reach a run.
    /// </summary>
    public sealed class TriggerBox : TableLayoutPanel
    {
        private readonly TextBox _text;
        private readonly Label _feedback;

        public event EventHandler Changed;

        public TriggerBox(string initial = "")
        {
            ColumnCount = 1;
            RowCount = 2;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = new Padding(0);
            Dock = DockStyle.Top;
            // A percent column plus Dock=Fill makes the field follow the dialog's width
            // instead of forcing the dialog to grow to fit it.
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _text = new TextBox { Dock = DockStyle.Fill, Text = initial ?? "" };
            _feedback = new Label { AutoSize = true, Margin = new Padding(3, 0, 3, 4) };

            _text.TextChanged += (s, e) => { RefreshFeedback(); Changed?.Invoke(this, EventArgs.Empty); };

            Controls.Add(_text);
            Controls.Add(_feedback);
            RefreshFeedback();
        }

        public string Value
        {
            get { return _text.Text.Trim(); }
            set { if (_text.Text != value) _text.Text = value ?? ""; }
        }

        public bool IsValid { get; private set; }

        /// <summary>Not named Validate: that is a real method on Control, and hiding it
        /// with a private one of our own is a trap rather than an override.</summary>
        private void RefreshFeedback()
        {
            string text = _text.Text.Trim();
            if (text.Length == 0)
            {
                IsValid = false;
                _feedback.ForeColor = SystemColors.GrayText;
                _feedback.Text = "(empty)";
                return;
            }

            var errors = new List<string>();
            var trigger = Trigger.Parse(text, errors);

            IsValid = trigger != null && errors.Count == 0;
            _feedback.ForeColor = IsValid ? Color.FromArgb(0, 128, 0) : Color.FromArgb(176, 0, 0);
            _feedback.Text = IsValid ? "✓ ok" : errors.Count > 0 ? errors[0] : "invalid";
        }
    }
}
