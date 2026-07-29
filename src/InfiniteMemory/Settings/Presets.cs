using System.Collections.Generic;

namespace InfiniteSplitter.Settings
{
    public sealed class Preset
    {
        public readonly string Group;
        public readonly string Name;
        public readonly string Trigger;

        public Preset(string group, string name, string trigger)
        {
            Group = group;
            Name = name;
            Trigger = trigger;
        }

        public override string ToString() => Group + " — " + Name;
    }

    /// <summary>
    /// The common splits, so the expression language is there when you outgrow the
    /// list rather than being the price of entry. Bosses are in route order.
    ///
    /// Lives here rather than beside the dropdown that shows it so the offline suite
    /// can parse every trigger: a mistyped scene name still compiles and still parses,
    /// and shows up only as a split that never fires.
    /// </summary>
    public static class Presets
    {
        public static readonly List<Preset> All = new List<Preset>
        {
            new Preset("Boss", "Tenroh", "flag:tenrohBestiary"),
            new Preset("Boss", "Queen Wasps", "flag:queensWaspsBestiary"),
            new Preset("Boss", "Galegrim", "flag:galegrimBestiary"),
            new Preset("Boss", "Melog", "flag:melogBestiary"),
            new Preset("Boss", "Kairuz", "flag:kairuzBestiary"),
            new Preset("Boss", "Etrom", "flag:etromBestiary"),
            new Preset("Boss", "Elder", "flag:elderBestiary"),

            new Preset("Creature", "Scythehead", "flag:scytheheadBestiary"),
            new Preset("Creature", "Sorcerer", "flag:SorcererBestiary"),

            new Preset("Item", "Sword", "flag:sword"),

            new Preset("Skill", "Charged Attack", "skill:ChargedAttack"),
            new Preset("Skill", "Dash", "skill:Dash"),
            new Preset("Skill", "Double Jump", "skill:DoubleJump"),
            new Preset("Skill", "Flames", "skill:flames"),
            new Preset("Skill", "Water Upgrade", "skill:waterUpgrade"),
            new Preset("Skill", "Ghost", "skill:Ghost"),

            new Preset("Area", "Enter Lobby", "scene:lobby"),
            new Preset("Area", "Enter Forest", "scene:forest"),
            new Preset("Area", "Enter Volcano", "scene:volcano"),
            new Preset("Area", "Enter Castle", "scene:castle"),

            new Preset("Milestone", "4 Heart Fragments", "int:heartFragments>=4"),
            new Preset("Milestone", "All Diaries", "flag:allDiariesCollected"),
            new Preset("Milestone", "Merchant unlocked", "flag:merchantOn"),
            new Preset("Milestone", "Teleport unlocked", "flag:teleportationOn"),

            // Route order: the kill, the ending you pick, the credits.
            new Preset("End", "Elder's death", "credits"),
            new Preset("End", "Any ending", "ending:any"),
            new Preset("End", "Ending 1", "ending:1"),
            new Preset("End", "Ending 2", "ending:2"),
            new Preset("End", "Credits roll", "scene:credits"),
            new Preset("End", "Post-credits (100 % only)", "scene:post credits cutscene"),
        };
    }
}
