using System;
using LiveSplit.Model;
using LiveSplit.UI.Components;

[assembly: ComponentFactory(typeof(InfiniteSplitter.InfiniteSplitterFactory))]

namespace InfiniteSplitter
{
    public class InfiniteSplitterFactory : IComponentFactory
    {
        private const string Branch = "main";
        private const string Repository = "sushi-de-linguica/infinite-splitter";

        internal const string CanonicalName = "InfiniteSplitter";

        public string ComponentName => CanonicalName;
        public string Description => "Infinitevania auto splitter and in-game time, read from the game's memory";

        public ComponentCategory Category => ComponentCategory.Control;
        public string UpdateName => ComponentName;
        public string UpdateURL => $"https://raw.githubusercontent.com/{Repository}/{Branch}/";
        public string XMLURL => UpdateURL + "Components/Updates.xml";
        public Version Version => VersionHelper.Version;
        public IComponent Create(LiveSplitState state) => new InfiniteSplitterComponent(state);
    }
}
