using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using InfiniteSplitter.Core;

namespace InfiniteSplitter.Settings
{
    /// <summary>
    /// Converts between the settings model, the runtime <see cref="Cfg"/>, and the
    /// INI text the Advanced tab shows. Sections the component does not use —
    /// [server], [hud], [hotkeys] — are ignored rather than rejected, so a config
    /// carrying them still imports.
    /// </summary>
    public static class CfgBridge
    {
        public static Cfg ToCfg(SettingsViewModel model, out List<string> problems)
        {
            problems = new List<string>();

            var cfg = new Cfg
            {
                SendIgt = model.SendIgt,
                RemoveLoads = model.RemoveLoads,
                ZeroAtStart = model.ZeroAtStart,
                SwitchToGameTime = model.SwitchToGameTime,
                AutoStart = model.AutoStart,
                AutoSplit = model.AutoSplit,
                AutoReset = model.AutoReset,
                RestartOnNewGame = model.RestartOnNewGame,
            };

            cfg.Start = ParseTrigger(model.Start, "start", problems);
            cfg.Reset = ParseTrigger(model.Reset, "reset", problems);

            for (int i = 0; i < model.Splits.Count; i++)
            {
                var entry = model.Splits[i];
                var errors = new List<string>();
                var trigger = Trigger.Parse(entry.Trigger, errors);

                if (trigger == null || errors.Count > 0)
                {
                    problems.Add($"split {i + 1} '{entry.Name}': {string.Join("; ", errors.ToArray())}");
                    continue;
                }

                cfg.Splits.Add(new SplitDef
                {
                    Name = entry.Name,
                    Raw = entry.Trigger,
                    Trigger = trigger,
                    Line = i + 1,
                });
            }

            return cfg;
        }

        private static Trigger ParseTrigger(string text, string what, List<string> problems)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var errors = new List<string>();
            var trigger = Trigger.Parse(text, errors);
            if (errors.Count == 0) return trigger;

            problems.Add(what + ": " + string.Join("; ", errors.ToArray()));
            return null;
        }

        public static string ToIni(SettingsViewModel model)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# infinite-splitter configuration");
            sb.AppendLine("# The [server], [hud] and [hotkeys] sections are accepted on import");
            sb.AppendLine("# and ignored: this component has no server, HUD or hotkeys.");
            sb.AppendLine();
            sb.AppendLine("[timer]");
            sb.AppendLine("igt = " + OnOff(model.SendIgt));
            sb.AppendLine("remove_loads = " + OnOff(model.RemoveLoads));
            sb.AppendLine("zero_at_start = " + OnOff(model.ZeroAtStart));
            sb.AppendLine("switch_to_gametime = " + OnOff(model.SwitchToGameTime));
            sb.AppendLine();
            sb.AppendLine("[run]");
            sb.AppendLine("start = " + Blank(model.Start));
            sb.AppendLine("reset = " + Blank(model.Reset));
            sb.AppendLine("autostart = " + OnOff(model.AutoStart));
            sb.AppendLine("autosplit = " + OnOff(model.AutoSplit));
            sb.AppendLine("autoreset = " + OnOff(model.AutoReset));
            sb.AppendLine("restart_on_new_game = " + OnOff(model.RestartOnNewGame));
            sb.AppendLine();
            sb.AppendLine("[splits]");

            int width = 0;
            foreach (var split in model.Splits) width = Math.Max(width, split.Name.Length);
            foreach (var split in model.Splits)
                sb.AppendLine(split.Name.PadRight(width) + " = " + split.Trigger);

            return sb.ToString();
        }

        /// <summary>Parses INI text into a model. Never throws; the parser's own
        /// complaints come back verbatim so the user sees line numbers.</summary>
        public static SettingsViewModel FromIni(string text, out List<string> problems)
        {
            var cfg = Cfg.Parse(text);
            problems = new List<string>();
            problems.AddRange(cfg.Errors);
            foreach (var warning in cfg.Warnings) problems.Add("warning: " + warning);

            var model = new SettingsViewModel
            {
                SendIgt = cfg.SendIgt,
                RemoveLoads = cfg.RemoveLoads,
                ZeroAtStart = cfg.ZeroAtStart,
                SwitchToGameTime = cfg.SwitchToGameTime,
                Start = cfg.Start?.Source ?? "",
                Reset = cfg.Reset?.Source ?? "",
                AutoStart = cfg.AutoStart,
                AutoSplit = cfg.AutoSplit,
                AutoReset = cfg.AutoReset,
                RestartOnNewGame = cfg.RestartOnNewGame,
                Splits = new List<SplitEntry>(),
            };

            foreach (var split in cfg.Splits)
                model.Splits.Add(new SplitEntry(split.Name, split.Raw));

            return model;
        }

        private static string OnOff(bool value) => value ? "on" : "off";

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? "never" : value;
    }
}
