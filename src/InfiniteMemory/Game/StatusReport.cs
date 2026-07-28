using System.Text;

namespace InfiniteSplitter.Game
{
    /// <summary>
    /// What the attach actually resolved, as text: addresses, the offsets that had to
    /// be searched for rather than trusted, and everything non-fatal that was noted on
    /// the way. It is what the Debug tab shows and the first thing to read
    /// when a split fires late or not at all.
    ///
    /// Lives here rather than next to the UI because it is pure formatting over the
    /// memory layer's own types — no WinForms, and reachable from the offline suite.
    /// </summary>
    public static class StatusReport
    {
        /// <summary>
        /// Never throws and never returns null: a caller in the middle of a failed
        /// attach still gets a line it can display. Lines are separated by '\n'; a
        /// WinForms caller has to widen those to "\r\n" itself.
        /// </summary>
        public static string Describe(Infinitevania game, MemoryProbe probe)
        {
            var sb = new StringBuilder();
            if (game == null || !game.IsHooked || game.Runtime == null)
                return "not attached to Infinitevania\n";

            var runtime = game.Runtime;
            sb.AppendLine($"pid                {game.ProcessId}");
            sb.AppendLine($"mono module        0x{game.MonoModuleBase:X}");
            sb.AppendLine($"root domain        0x{runtime.RootDomain:X}  (domain {runtime.DomainId})");
            sb.AppendLine($"game assembly      0x{runtime.Image:X}  ({runtime.ClassCount} classes)");
            sb.AppendLine($"class name offset  0x{runtime.ResolvedClassNameOffset:X}");
            sb.AppendLine($"class cache offset 0x{runtime.ResolvedClassCacheOffset:X}");
            sb.AppendLine($"GameManager        0x{game.GameManager?.Address ?? 0:X}");
            sb.AppendLine($"  instance         0x{game.ReadGameManagerInstance():X}");
            sb.AppendLine($"id sets            {probe?.CollectedCount ?? -1} collectibles, " +
                          $"{probe?.ActivatedCount ?? -1} activations");

            // Spelled out rather than implied: on "native" a scene: split fires at the
            // door, on "approx" it fires whenever the game next saves — the difference
            // between a correct split and a late one.
            if (probe != null)
            {
                sb.AppendLine($"scene              {probe.Scene}  ({probe.SceneSource})" +
                              (probe.SceneSource == "native"
                                  ? ""
                                  : "  — scene: splits land at the next save, not at the door"));
                if (game.Scene.Winner != null)
                    sb.AppendLine($"  chain            {game.Scene.Winner}");
            }
            sb.AppendLine($"new game source    {probe?.NewGameDetector.Source ?? "-"}" +
                          (probe != null && !probe.NewGameDetector.RunLoggerAvailable
                              ? "  (run counter not available yet this session)"
                              : ""));

            if (runtime.Probe.Overrides.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Mono offsets corrected at runtime:");
                foreach (var over in runtime.Probe.Overrides) sb.AppendLine("  " + over);
            }

            if (game.Notes.Count > 0)
            {
                sb.AppendLine();
                foreach (var note in game.Notes) sb.AppendLine("note: " + note);
            }

            if (probe != null)
            {
                bool any = false;
                foreach (var problem in probe.Problems)
                {
                    if (!any) { sb.AppendLine(); sb.AppendLine("config problems:"); any = true; }
                    sb.AppendLine("  " + problem);
                }
            }

            return sb.ToString();
        }
    }
}
