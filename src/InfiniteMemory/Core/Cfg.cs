using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace InfiniteSplitter.Core
{
    /// <summary>One line of the [splits] section: a display name and its trigger.</summary>
    public sealed class SplitDef
    {
        public string Name;
        public string Raw;
        public Trigger Trigger;
        public int Line;

        public override string ToString() => $"{Name} = {Raw}";
    }

    /// <summary>
    /// The .cfg file, an INI dialect.
    ///
    /// Deliberately Unity-free and dependency-free so tools/cfgcheck can compile
    /// these very sources and validate a config without the game installed. Every
    /// problem is collected into <see cref="Errors"/>/<see cref="Warnings"/>
    /// instead of thrown — a bad line must never take the game down mid-run.
    /// </summary>
    public sealed class Cfg
    {
        // [server]
        public string Host = "127.0.0.1";
        public int Port = 16834;
        public double ReconnectSeconds = 3.0;

        // [timer]
        public bool SendIgt = true;
        public bool ZeroAtStart;                // subtract the IGT captured when the run starts
        public bool SwitchToGameTime = true;    // make LiveSplit's main timer show game time

        // [run]
        public Trigger Start;
        public Trigger Reset;
        public bool AutoStart = true;
        public bool AutoSplit = true;
        public bool AutoReset = true;

        /// <summary>
        /// Starting a new game restarts the LiveSplit run even if one is already
        /// in progress. This is what makes `reset = never` usable: the run then
        /// survives a save/quit/load cycle but a genuinely new run still resets.
        /// </summary>
        public bool RestartOnNewGame = true;

        // [hud]
        public bool Hud = true;

        // [hotkeys] — KeyCode names, "none" disables
        public string KeyReload = "F10";
        public string KeySplit = "F11";
        public string KeyUndo = "F12";
        public string KeyReset = "none";

        public readonly List<SplitDef> Splits = new List<SplitDef>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public bool Ok => Errors.Count == 0;

        public static Cfg Parse(string text)
        {
            var cfg = new Cfg();
            string section = "";
            int lineNo = 0;

            foreach (var rawLine in SplitLines(text))
            {
                lineNo++;
                string line = StripComment(rawLine).Trim();
                if (line.Length == 0) continue;

                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    if (close < 0) { cfg.Errors.Add($"line {lineNo}: unterminated section header '{line}'"); continue; }
                    section = line.Substring(1, close - 1).Trim().ToLowerInvariant();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq < 0) { cfg.Errors.Add($"line {lineNo}: expected 'key = value', got '{line}'"); continue; }

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key.Length == 0) { cfg.Errors.Add($"line {lineNo}: empty key"); continue; }

                if (section == "splits")
                {
                    var errors = new List<string>();
                    var trigger = Trigger.Parse(value, errors);
                    foreach (var e in errors) cfg.Errors.Add($"line {lineNo}: split '{key}': {e}");
                    if (trigger != null)
                        cfg.Splits.Add(new SplitDef { Name = key, Raw = value, Trigger = trigger, Line = lineNo });
                    continue;
                }

                cfg.Assign(section, key.ToLowerInvariant(), value, lineNo);
            }

            if (cfg.Splits.Count == 0)
                cfg.Warnings.Add("no [splits] defined — only IGT will be sent, nothing will split");
            if (cfg.AutoStart && cfg.Start == null)
                cfg.Warnings.Add("[run] autostart is on but no 'start' trigger is set — start LiveSplit by hand");

            return cfg;
        }

        public static Cfg Load(string path, out string source)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                source = path;
                return Parse(File.ReadAllText(path));
            }
            source = "<embedded default>";
            return Parse(Default);
        }

        private void Assign(string section, string key, string value, int lineNo)
        {
            switch (section + "." + key)
            {
                case "server.host": Host = value; return;
                case "server.port": Port = (int)Num(value, Port, lineNo, "server.port"); return;
                case "server.reconnect_seconds": ReconnectSeconds = Num(value, ReconnectSeconds, lineNo, "server.reconnect_seconds"); return;

                case "timer.igt": SendIgt = Bool(value, SendIgt, lineNo, "timer.igt"); return;
                case "timer.zero_at_start": ZeroAtStart = Bool(value, ZeroAtStart, lineNo, "timer.zero_at_start"); return;
                case "timer.switch_to_gametime": SwitchToGameTime = Bool(value, SwitchToGameTime, lineNo, "timer.switch_to_gametime"); return;

                case "run.start": Start = ParseTrigger(value, lineNo, "run.start"); return;
                case "run.reset": Reset = ParseTrigger(value, lineNo, "run.reset"); return;
                case "run.autostart": AutoStart = Bool(value, AutoStart, lineNo, "run.autostart"); return;
                case "run.autosplit": AutoSplit = Bool(value, AutoSplit, lineNo, "run.autosplit"); return;
                case "run.autoreset": AutoReset = Bool(value, AutoReset, lineNo, "run.autoreset"); return;
                case "run.restart_on_new_game": RestartOnNewGame = Bool(value, RestartOnNewGame, lineNo, "run.restart_on_new_game"); return;

                case "hud.enabled": Hud = Bool(value, Hud, lineNo, "hud.enabled"); return;

                case "hotkeys.reload": KeyReload = value; return;
                case "hotkeys.split": KeySplit = value; return;
                case "hotkeys.undo": KeyUndo = value; return;
                case "hotkeys.reset": KeyReset = value; return;

                // Retired. Named rather than left to fall through to "unknown setting",
                // so an older config is told what happened to them: the clock is a
                // straight mirror of the game's now, which leaves no slot to choose and
                // nothing to carve out.
                case "timer.slot":
                case "timer.pause_on_load":
                case "timer.pause_on_pause_menu":
                    Warnings.Add($"line {lineNo}: '[timer] {key}' no longer exists — the clock always mirrors " +
                                 "the game's, following the slot in play; ignored");
                    return;
            }

            if (section.Length == 0) Errors.Add($"line {lineNo}: '{key}' appears before any [section]");
            else Warnings.Add($"line {lineNo}: unknown setting '[{section}] {key}' — ignored");
        }

        private Trigger ParseTrigger(string value, int lineNo, string what)
        {
            var errors = new List<string>();
            var result = Trigger.Parse(value, errors);
            foreach (var e in errors) Errors.Add($"line {lineNo}: {what}: {e}");
            return errors.Count == 0 ? result : null;
        }

        private bool Bool(string value, bool fallback, int lineNo, string what)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "on": case "true": case "yes": case "1": return true;
                case "off": case "false": case "no": case "0": return false;
            }
            Errors.Add($"line {lineNo}: {what} expects on/off, got '{value}'");
            return fallback;
        }

        private double Num(string value, double fallback, int lineNo, string what)
        {
            if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                return n;
            Errors.Add($"line {lineNo}: {what} expects a number, got '{value}'");
            return fallback;
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        /// <summary>
        /// Strips a trailing comment. Only '#' or ';' preceded by whitespace (or at
        /// the very start) counts, so an id containing one of them survives.
        /// </summary>
        private static string StripComment(string line)
        {
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c != '#' && c != ';') continue;
                if (i == 0 || char.IsWhiteSpace(line[i - 1])) return line.Substring(0, i);
            }
            return line;
        }

        /// <summary>
        /// Shipped fallback, used when no .cfg sits next to the DLL — the
        /// last-resort default before the user has configured anything.
        /// </summary>
        public const string Default = @"
[server]
host = 127.0.0.1
port = 16834

[timer]
igt = on

[run]
start = newgame
reset = never
restart_on_new_game = on

[splits]
Sword        = flag:sword
Tenroh       = flag:tenrohBestiary
Queen Wasps  = flag:queensWaspsBestiary
Galegrim     = flag:galegrimBestiary
Melog        = flag:melogBestiary
Kairuz       = flag:kairuzBestiary
Etrom        = flag:etromBestiary
Elder        = flag:elderBestiary
End          = ending:any
";
    }
}
