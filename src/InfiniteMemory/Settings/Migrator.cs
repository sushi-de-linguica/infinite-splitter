using System;
using System.Collections.Generic;
using System.Xml;

namespace InfiniteSplitter.Settings
{
    /// <summary>
    /// Rewrites a stored settings document to the current shape before it is
    /// deserialized.
    ///
    /// Present from the start on purpose: once a split's trigger names a game field,
    /// someone's saved layout depends on that name, and renaming it later without a
    /// migration silently drops their splits. The empty chain costs one lookup.
    /// </summary>
    public static class Migrator
    {
        /// <summary>
        /// A rename table applied to trigger expressions, keyed by the version the
        /// document was written with. Empty today; an entry looks like:
        ///   ["1"] = { ["flag:scytheHeadBestiary"] = "flag:scytheheadBestiary" }
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, string>> TriggerRenames =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        public static string Migrate(string xml, out List<string> notes)
        {
            notes = new List<string>();
            if (string.IsNullOrWhiteSpace(xml)) return xml;

            var document = new XmlDocument();
            document.LoadXml(xml);

            var root = document.DocumentElement;
            if (root == null) return xml;

            string version = root.GetAttribute("version");
            if (string.IsNullOrEmpty(version))
            {
                // Written before versioning existed, or by hand: treat it as the
                // oldest shape and let the chain below bring it forward.
                version = "0";
                notes.Add("settings had no version attribute; assumed the oldest shape");
            }

            if (version == SettingsViewModel.CurrentVersion) return xml;

            if (TriggerRenames.TryGetValue(version, out var renames))
            {
                int changed = 0;
                foreach (XmlNode split in document.SelectNodes("//Split"))
                {
                    var attribute = split.Attributes?["trigger"];
                    if (attribute == null) continue;
                    if (!renames.TryGetValue(attribute.Value, out string replacement)) continue;

                    attribute.Value = replacement;
                    changed++;
                }
                if (changed > 0) notes.Add($"migrated {changed} trigger(s) from settings version {version}");
            }

            root.SetAttribute("version", SettingsViewModel.CurrentVersion);
            return document.OuterXml;
        }
    }
}
