using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using InfiniteSplitter.Settings;

namespace InfiniteSplitter.UI
{
    /// <summary>The small WinForms builders the pages are assembled from, and the reflow that
    /// keeps wrapping controls inside the dialog's width.</summary>
    public partial class SettingsControl
    {
        private void ReflowWrappingControls()
        {
            int width = Math.Max(120, ClientSize.Width - 36);
            foreach (var control in _wrapping)
                control.MaximumSize = new Size(width, 0);
        }

        private static TableLayoutPanel Rows(int count)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = count };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < count; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return layout;
        }

        private FlowLayoutPanel Flow(params Control[] controls)
        {
            var flow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0),
            };
            flow.Controls.AddRange(controls);
            _wrapping.Add(flow);
            return flow;
        }

        private Label Note(string text)
        {
            var label = new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Text = text,
                Margin = new Padding(3, 2, 3, 8),
            };
            _wrapping.Add(label);
            return label;
        }

        private static Button Button(string text, EventHandler onClick)
        {
            var button = new Button { AutoSize = true, Text = text };
            button.Click += onClick;
            return button;
        }
    }
}
