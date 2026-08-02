using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace InfiniteSplitter.Settings
{
    public sealed class SplitEntry
    {
        [XmlAttribute("name")] public string Name = "";
        [XmlAttribute("trigger")] public string Trigger = "";

        public SplitEntry() { }
        public SplitEntry(string name, string trigger) { Name = name; Trigger = trigger; }

        public SplitEntry Copy() => new SplitEntry(Name, Trigger);

        public override string ToString() => Name + " = " + Trigger;
    }

    /// <summary>
    /// Everything the user can configure, and what LiveSplit persists inside the
    /// layout file. It lives in the memory assembly rather than next to the UI so the
    /// offline harness can round-trip it with no WinForms and no LiveSplit — hence
    /// plain public fields and no UI types.
    ///
    /// Per-split conditions stay expression strings rather than a structured model:
    /// every condition is a predicate over named GameManager state, so a type would
    /// buy nothing and give up composition (`flag:melogBestiary &amp; !scene:menu`).
    /// The UI covers the common case with presets and validates as you type.
    /// </summary>
    [XmlRoot("InfiniteSplitter")]
    public sealed class SettingsViewModel
    {
        /// <summary>Bumped when the shape changes; read by <see cref="Migrator"/>
        /// *before* deserialization.</summary>
        public const string CurrentVersion = "1";

        [XmlAttribute("version")] public string Version = CurrentVersion;

        // [timer]
        public bool SendIgt = true;
        public bool ZeroAtStart;
        public bool SwitchToGameTime = true;

        // [run]
        public string Start = "newgame";
        public string Reset = "never";
        public bool AutoStart = true;
        public bool AutoSplit = true;
        public bool AutoReset = true;
        public bool RestartOnNewGame = true;

        /// <summary>
        /// Starts empty, not with the default route: XmlSerializer appends to a
        /// pre-populated collection rather than replacing it, so defaults here would be
        /// grafted onto every document loaded. Use <see cref="CreateDefault"/>.
        /// </summary>
        [XmlArray("Splits"), XmlArrayItem("Split")]
        public List<SplitEntry> Splits = new List<SplitEntry>();

        /// <summary>Runtime only — never persisted.</summary>
        [XmlIgnore] public readonly List<string> LoadProblems = new List<string>();

        /// <summary>A brand-new configuration, with the default route.</summary>
        public static SettingsViewModel CreateDefault() =>
            new SettingsViewModel { Splits = DefaultSplits() };

        /// <summary>The seven bosses in route order, each the gate for the ability that
        /// opens the next stretch, closed by the ending the player chose.
        ///
        /// The last row is deliberately not the `credits` latch: that latch rises on the
        /// elder's death, in the same tick as the Elder row above it, which would end the
        /// run while it is still being played. It stops at the ending rather than at the
        /// credits so the timer does not run through a cutscene nobody is playing.</summary>
        public static List<SplitEntry> DefaultSplits() => new List<SplitEntry>
        {
            new SplitEntry("Sword", "flag:sword"),
            new SplitEntry("Tenroh", "flag:tenrohBestiary"),
            new SplitEntry("Queen Wasps", "flag:queensWaspsBestiary"),
            new SplitEntry("Galegrim", "flag:galegrimBestiary"),
            new SplitEntry("Melog", "flag:melogBestiary"),
            new SplitEntry("Kairuz", "flag:kairuzBestiary"),
            new SplitEntry("Etrom", "flag:etromBestiary"),
            new SplitEntry("Elder", "flag:elderBestiary"),
            new SplitEntry("End", "ending:any"),
        };

        public SettingsViewModel Copy()
        {
            var copy = (SettingsViewModel)MemberwiseClone();
            copy.Splits = new List<SplitEntry>();
            foreach (var split in Splits) copy.Splits.Add(split.Copy());
            return copy;
        }

        public string Serialize()
        {
            var serializer = new XmlSerializer(typeof(SettingsViewModel));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, this);
                return writer.ToString();
            }
        }

        /// <summary>
        /// Never throws. A corrupt document yields fresh defaults with an explanation
        /// in <see cref="LoadProblems"/>: unreadable settings must leave the component
        /// working and saying so, not dead.
        /// </summary>
        public static SettingsViewModel Deserialize(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return CreateDefault();

            try
            {
                var migrated = Migrator.Migrate(xml, out var notes);
                var serializer = new XmlSerializer(typeof(SettingsViewModel));
                using (var reader = new StringReader(migrated))
                {
                    var model = (SettingsViewModel)serializer.Deserialize(reader);
                    if (model.Splits == null) model.Splits = new List<SplitEntry>();
                    model.LoadProblems.AddRange(notes);
                    model.Version = CurrentVersion;
                    return model;
                }
            }
            catch (Exception e)
            {
                var fallback = CreateDefault();
                fallback.LoadProblems.Add("settings could not be read (" + e.Message + "); defaults restored");
                return fallback;
            }
        }

        /// <summary>Reads the &lt;InfiniteSplitter&gt; element out of whatever wrapper
        /// LiveSplit hands us.</summary>
        public static SettingsViewModel FromXmlNode(XmlNode node)
        {
            if (node == null) return CreateDefault();

            var element = node.Name == "InfiniteSplitter"
                ? node
                : node.SelectSingleNode("InfiniteSplitter");

            return element == null
                ? CreateDefault()
                : Deserialize(element.OuterXml);
        }
    }
}
