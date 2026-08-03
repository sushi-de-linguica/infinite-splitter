using System.Collections.Generic;
using InfiniteSplitter.Mono;

namespace InfiniteSplitter.Game
{
    /// <summary>
    /// The handful of fields read on every tick, resolved once at attach.
    ///
    /// Naming them here rather than looking them up per frame means a missing one is
    /// reported at attach, in the Debug tab, instead of as a silently false trigger
    /// halfway through someone's run.
    /// </summary>
    public sealed class GameFields
    {
        /// <summary>Index 1..5; index 0 is unused so the code reads like the game's names.</summary>
        public readonly MonoFieldRef[] Hour = new MonoFieldRef[6];
        public readonly MonoFieldRef[] Minutes = new MonoFieldRef[6];
        public readonly MonoFieldRef[] Seconds = new MonoFieldRef[6];
        public readonly MonoFieldRef[] CanSave = new MonoFieldRef[6];

        /// <summary>GameManager.Update sets this every frame to
        /// `SceneManager.GetActiveScene().name == "menu"`, so it answers `scene:menu`
        /// exactly — the one scene test the default config makes.</summary>
        public MonoFieldRef TimePaused;

        public MonoFieldRef Loading;
        public MonoFieldRef CurrentScene;

        /// <summary>On UIManager, not GameManager.</summary>
        public MonoFieldRef UiPaused;

        /// <summary>On UIManager. Non-empty exactly while a scene transition is in
        /// flight — see docs/architecture.md.</summary>
        public MonoFieldRef UiCurrentScene;

        public int GameManagerExtent;
        public int UIManagerExtent;

        /// <summary>Names the game did not have. Non-fatal; surfaced in the Debug tab.</summary>
        public readonly List<string> Missing = new List<string>();

        public bool ClockAvailable => Hour[1] != null && Minutes[1] != null && Seconds[1] != null;

        public static GameFields Resolve(MonoClassRef gameManager, MonoClassRef uiManager)
        {
            var fields = new GameFields();
            if (gameManager == null) { fields.Missing.Add("GameManager"); return fields; }

            for (int slot = 1; slot <= 5; slot++)
            {
                fields.Hour[slot] = fields.Require(gameManager, "hourCount" + slot);
                fields.Minutes[slot] = fields.Require(gameManager, "minutesCount" + slot);
                fields.Seconds[slot] = fields.Require(gameManager, "secondsCount" + slot);
                fields.CanSave[slot] = fields.Require(gameManager, "canSave" + slot);
            }

            fields.TimePaused = fields.Require(gameManager, "timePaused");
            fields.Loading = fields.Require(gameManager, "loading");
            fields.CurrentScene = fields.Require(gameManager, "currentScene");
            fields.GameManagerExtent = gameManager.InstanceExtent();

            if (uiManager != null)
            {
                fields.UiPaused = uiManager.Find("paused");
                if (fields.UiPaused == null) fields.Missing.Add("UIManager.paused");

                fields.UiCurrentScene = uiManager.Find("currentScene");
                if (fields.UiCurrentScene == null) fields.Missing.Add("UIManager.currentScene");

                fields.UIManagerExtent = uiManager.InstanceExtent();
            }

            return fields;
        }

        private MonoFieldRef Require(MonoClassRef klass, string name)
        {
            var field = klass.Find(name);
            if (field == null) Missing.Add("GameManager." + name);
            return field;
        }
    }
}
