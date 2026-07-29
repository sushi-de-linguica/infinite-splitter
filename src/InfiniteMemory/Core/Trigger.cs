using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace InfiniteSplitter.Core
{
    /// <summary>
    /// Everything a trigger can ask about the running game. The whole surface between
    /// the expression language and however the values are actually read, which is why
    /// nothing here mentions Unity or the process.
    /// </summary>
    public interface IProbe
    {
        /// <summary>A public bool field on GameManager (sword, kairuzBestiary, ...).</summary>
        bool Flag(string field);

        /// <summary>A public numeric field on GameManager (souls, heartFragments, ...).</summary>
        double Number(string field);

        /// <summary>Skills enum member, by name or by ordinal.</summary>
        bool HasSkill(string skill);

        bool Collectible(string id);
        bool Activated(string id);
        int CountPrefix(string prefix);

        /// <summary>Name of the active scene (menu, lobby, forest, volcano, castle, ...).</summary>
        string Scene { get; }

        /// <summary>Latched on the elder's death, not on the credits. Coarser than
        /// `scene:credits` and earlier than it, but read from a plain managed field, so
        /// it is the end-of-run signal that still works when <see cref="Scene"/> has
        /// fallen back to the approximation.</summary>
        bool Credits { get; }

        /// <summary>One-frame pulse: the player just started a new game from the menu.</summary>
        bool NewGame { get; }

        /// <summary>Seconds on the clock about to be handed to LiveSplit.</summary>
        double Igt { get; }

        /// <summary>Names a trigger referenced but the game does not have. Reported once, then treated as false.</summary>
        void ReportUnknown(string what);
    }

    public enum AtomKind
    {
        Flag, Number, Skill, Collectible, Activation, CountPrefix,
        Scene, Ending, Credits, NewGame, Igt, Always, Never
    }

    public enum Op { None, Ge, Gt, Le, Lt, Eq, Ne }

    public sealed class Atom
    {
        public bool Negate;
        public AtomKind Kind;
        public string Arg = "";
        public Op Op = Op.None;
        public double Value;
        public string Source = "";

        public bool Eval(IProbe p)
        {
            bool v;
            switch (Kind)
            {
                case AtomKind.Flag: v = p.Flag(Arg); break;
                case AtomKind.Number: v = Compare(p.Number(Arg), Value); break;
                case AtomKind.Skill: v = p.HasSkill(Arg); break;
                case AtomKind.Collectible: v = p.Collectible(Arg); break;
                case AtomKind.Activation: v = p.Activated(Arg); break;
                case AtomKind.CountPrefix: v = Compare(p.CountPrefix(Arg), Value); break;
                case AtomKind.Scene: v = string.Equals(p.Scene, Arg, StringComparison.OrdinalIgnoreCase); break;
                case AtomKind.Ending: v = MatchesEnding(p.Scene); break;
                case AtomKind.Credits: v = p.Credits; break;
                case AtomKind.NewGame: v = p.NewGame; break;
                case AtomKind.Igt: v = Compare(p.Igt, Value); break;
                case AtomKind.Always: v = true; break;
                default: v = false; break;
            }
            return Negate ? !v : v;
        }

        /// <summary>
        /// The endings are ordinary scenes, so `ending:` is shorthand rather than a new
        /// source of truth: `ending:2` is `scene:final cutscene 2`, and `ending:any` is
        /// the OR of both without the chance of misspelling either.
        /// </summary>
        public static readonly string[] EndingScenes = { "final cutscene 1", "final cutscene 2" };

        /// <summary><see cref="Value"/> carries the index the parser resolved, or -1 for
        /// "whichever ending was taken". Reads <see cref="IProbe.Scene"/> once: on the
        /// live probe that property is a memory read, not a field.</summary>
        private bool MatchesEnding(string scene)
        {
            int want = (int)Value;
            for (int i = 0; i < EndingScenes.Length; i++)
            {
                if (want >= 0 && want != i) continue;
                if (string.Equals(scene, EndingScenes[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private bool Compare(double left, double right)
        {
            switch (Op)
            {
                case Op.Ge: return left >= right;
                case Op.Gt: return left > right;
                case Op.Le: return left <= right;
                case Op.Lt: return left < right;
                case Op.Eq: return Math.Abs(left - right) < 1e-9;
                case Op.Ne: return Math.Abs(left - right) >= 1e-9;
                default: return left != 0;
            }
        }

        public override string ToString() => Source;
    }

    /// <summary>
    /// A boolean expression over <see cref="Atom"/>s: OR of ANDs.
    ///
    ///   flag:sword                          a single condition
    ///   flag:sword &amp; scene:forest            both
    ///   flag:melogBestiary | flag:etromBestiary  either
    ///   !flag:merchantOn                    negated
    ///
    /// '&amp;' binds tighter than '|'; there are no parentheses, on purpose — a split
    /// condition that needs them wants a different split.
    /// </summary>
    public sealed class Trigger
    {
        public readonly List<List<Atom>> Or = new List<List<Atom>>();
        public string Source = "";

        public bool Eval(IProbe p)
        {
            for (int i = 0; i < Or.Count; i++)
            {
                var and = Or[i];
                bool all = true;
                for (int j = 0; j < and.Count; j++)
                {
                    if (and[j].Eval(p)) continue;
                    all = false;
                    break;
                }
                if (all) return true;
            }
            return false;
        }

        public IEnumerable<Atom> Atoms
        {
            get
            {
                foreach (var and in Or)
                    foreach (var a in and)
                        yield return a;
            }
        }

        public static Trigger Parse(string text, List<string> errors)
        {
            var t = new Trigger { Source = (text ?? "").Trim() };
            if (t.Source.Length == 0) { errors.Add("empty trigger"); return null; }

            foreach (var orPart in t.Source.Split('|'))
            {
                var and = new List<Atom>();
                foreach (var andPart in orPart.Split('&'))
                {
                    var atom = ParseAtom(andPart.Trim(), errors);
                    if (atom != null) and.Add(atom);
                }
                if (and.Count > 0) t.Or.Add(and);
            }

            return t.Or.Count > 0 ? t : null;
        }

        private static Atom ParseAtom(string text, List<string> errors)
        {
            if (text.Length == 0) { errors.Add("empty term"); return null; }

            var atom = new Atom { Source = text };
            if (text[0] == '!')
            {
                atom.Negate = true;
                text = text.Substring(1).Trim();
                if (text.Length == 0) { errors.Add("'!' with nothing after it"); return null; }
            }

            int colon = text.IndexOf(':');
            string kind = (colon < 0 ? text : text.Substring(0, colon)).Trim().ToLowerInvariant();
            string rest = colon < 0 ? "" : text.Substring(colon + 1).Trim();

            switch (kind)
            {
                case "credits": case "end": atom.Kind = AtomKind.Credits; return NoArg(atom, rest, errors);
                case "newgame": atom.Kind = AtomKind.NewGame; return NoArg(atom, rest, errors);
                case "always": atom.Kind = AtomKind.Always; return NoArg(atom, rest, errors);
                case "never": case "none": case "off":
                    atom.Kind = AtomKind.Never; return NoArg(atom, rest, errors);

                case "flag": atom.Kind = AtomKind.Flag; break;
                case "skill": atom.Kind = AtomKind.Skill; break;
                case "collectible": case "item": atom.Kind = AtomKind.Collectible; break;
                case "activation": atom.Kind = AtomKind.Activation; break;
                case "scene": atom.Kind = AtomKind.Scene; break;
                case "ending": atom.Kind = AtomKind.Ending; break;
                case "int": case "num": atom.Kind = AtomKind.Number; break;
                case "count": atom.Kind = AtomKind.CountPrefix; break;
                case "igt": atom.Kind = AtomKind.Igt; break;
                default:
                    errors.Add($"unknown trigger kind '{kind}' in '{atom.Source}' " +
                               "(flag, int, count, skill, collectible, activation, scene, ending, " +
                               "igt, credits, newgame)");
                    return null;
            }

            if (rest.Length == 0 && colon < 0)
            {
                errors.Add($"'{kind}' needs an argument, as in '{kind}:something'");
                return null;
            }

            if (atom.Kind == AtomKind.Number || atom.Kind == AtomKind.CountPrefix || atom.Kind == AtomKind.Igt)
                return ParseComparison(atom, rest, errors);

            if (atom.Kind == AtomKind.Ending)
                return ParseEnding(atom, rest, errors);

            atom.Arg = rest;
            if (atom.Arg.Length == 0) { errors.Add($"'{kind}' needs an argument"); return null; }
            return atom;
        }

        /// <summary>
        /// `ending:1`, `ending:2` or `ending:any`, resolved here to the index the atom
        /// carries in <see cref="Atom.Value"/> — -1 meaning either ending.
        /// Anything else is rejected here rather than at evaluation: an ending that does
        /// not exist would otherwise be a split that quietly never fires, at the end of
        /// a run, which is the worst place to discover a typo.
        /// </summary>
        private static Atom ParseEnding(Atom atom, string rest, List<string> errors)
        {
            switch (rest.ToLowerInvariant())
            {
                case "any": atom.Value = -1; return atom;
                case "1": atom.Value = 0; return atom;
                case "2": atom.Value = 1; return atom;
                default:
                    errors.Add($"'{atom.Source}' — 'ending' takes 1, 2 or any");
                    return null;
            }
        }

        private static Atom NoArg(Atom atom, string rest, List<string> errors)
        {
            if (rest.Length > 0) errors.Add($"'{atom.Source}' takes no argument");
            return atom;
        }

        /// <summary>Parses "name &gt;= 4", "&gt;= 10:00" (igt), or a bare "name" meaning "!= 0".</summary>
        private static Atom ParseComparison(Atom atom, string rest, List<string> errors)
        {
            int at = -1;
            Op op = Op.None;
            for (int i = 0; i < rest.Length; i++)
            {
                char c = rest[i];
                if (c != '>' && c != '<' && c != '=' && c != '!') continue;
                bool twoChar = i + 1 < rest.Length && rest[i + 1] == '=';
                if (c == '>') op = twoChar ? Op.Ge : Op.Gt;
                else if (c == '<') op = twoChar ? Op.Le : Op.Lt;
                else if (c == '=') op = Op.Eq;
                else { if (!twoChar) { errors.Add($"'!' must be '!=' in '{atom.Source}'"); return null; } op = Op.Ne; }
                int opLen = twoChar ? 2 : 1;
                at = i;
                atom.Arg = rest.Substring(0, i).Trim();
                rest = rest.Substring(i + opLen).Trim();
                break;
            }

            if (at < 0)
            {
                if (atom.Kind == AtomKind.Igt) { errors.Add($"'igt' needs a comparison, as in 'igt:>=10:00'"); return null; }
                atom.Arg = rest.Trim();
                atom.Op = Op.Ne;
                atom.Value = 0;
                return atom;
            }

            atom.Op = op;

            if (atom.Kind == AtomKind.Igt)
            {
                if (!TryParseTime(rest, out double seconds)) { errors.Add($"'{rest}' is not a time (use 12.5, 1:30, or 1:02:03.4)"); return null; }
                atom.Value = seconds;
                atom.Arg = "igt";
                return atom;
            }

            if (atom.Arg.Length == 0) { errors.Add($"'{atom.Source}' is missing the field name before the operator"); return null; }
            if (!double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                errors.Add($"'{rest}' is not a number, in '{atom.Source}'");
                return null;
            }
            atom.Value = value;
            return atom;
        }

        /// <summary>"12.5" | "1:30" | "1:02:03.4" -&gt; seconds.</summary>
        public static bool TryParseTime(string text, out double seconds)
        {
            seconds = 0;
            var parts = (text ?? "").Trim().Split(':');
            if (parts.Length == 0 || parts.Length > 3) return false;
            double total = 0;
            foreach (var part in parts)
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) return false;
                total = total * 60.0 + n;
            }
            seconds = total;
            return true;
        }

        /// <summary>h:mm:ss.fff, the shape LiveSplit's TimeSpanParser expects.</summary>
        public static string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int totalMs = (int)Math.Round(seconds * 1000.0);
            int ms = totalMs % 1000;
            int totalSec = totalMs / 1000;
            int s = totalSec % 60;
            int m = (totalSec / 60) % 60;
            int h = totalSec / 3600;
            var sb = new StringBuilder(12);
            sb.Append(h.ToString(CultureInfo.InvariantCulture)).Append(':');
            sb.Append(m.ToString("00", CultureInfo.InvariantCulture)).Append(':');
            sb.Append(s.ToString("00", CultureInfo.InvariantCulture)).Append('.');
            sb.Append(ms.ToString("000", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public override string ToString() => Source;
    }
}
