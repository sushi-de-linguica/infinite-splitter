using System;
using System.Collections.Generic;
using System.Globalization;
using InfiniteSplitter.Core;
using InfiniteSplitter.Memory;
using InfiniteSplitter.Mono;

namespace InfiniteSplitter.Game
{
    /// <summary>
    /// Answers the trigger language's questions from a snapshot.
    ///
    /// A call per atom looks chatty for out-of-process work, but nothing here touches
    /// the process: <see cref="Infinitevania.TakeSnapshot"/> already copied the watched
    /// objects whole, and each answer is a byte index into that array.
    /// </summary>
    public sealed class MemoryProbe : IProbe
    {
        private readonly Infinitevania _game;
        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, MonoFieldRef> _fields =
            new Dictionary<string, MonoFieldRef>(StringComparer.Ordinal);
        private readonly Dictionary<long, MonoReader.ListLayout> _listLayouts =
            new Dictionary<long, MonoReader.ListLayout>();
        private readonly List<int> _skills = new List<int>();

        private MonoStringSet _collected;
        private MonoStringSet _activated;

        private GameSnapshot _snapshot;
        private GameFields _gameFields;
        private bool _creditsSeen;
        private bool _elderWasDead;

        public Action<string> Log;

        public MemoryProbe(Infinitevania game) { _game = game; }

        public NewGameDetector NewGameDetector { get; } = new NewGameDetector();

        public double Igt { get; set; }

        /// <summary>Called once per tick, before the timeline runs.</summary>
        public void Bind(GameSnapshot snapshot, GameFields fields, double igt)
        {
            _snapshot = snapshot;
            _gameFields = fields;
            Igt = igt;

            RefreshSets();

            // `credits` latches on the elder's death, which is earlier than the credits
            // themselves: the ending is still to be chosen and its cutscene still to
            // play. It is kept because it is the one end-of-run signal that survives the
            // native scene read going stale — see IProbe.Credits. `scene:credits` is the
            // split that lands on the credits proper.
            bool elderDead = Flag("elderBestiary");
            if (elderDead && !_elderWasDead) _creditsSeen = true;
            _elderWasDead = elderDead;
        }

        /// <summary>Called at the end of every tick. Nothing to do here: the new-game
        /// pulse is already one tick wide, cleared by the detector itself.</summary>
        public void ClearPulses() { }

        public void OnRunStarted() { _creditsSeen = false; }

        public void Detached()
        {
            _fields.Clear();
            _listLayouts.Clear();
            _collected = null;
            _activated = null;
            _reported.Clear();
            _creditsSeen = false;
            _elderWasDead = false;
            NewGameDetector.Reset();
        }

        public bool Flag(string field)
        {
            var reference = Resolve(field);
            if (reference == null) return false;

            if (reference.Kind != MonoTypeKind.Boolean)
            {
                ReportUnknown($"GameManager.{reference.Name} is {reference.Kind.Display()}, not bool — " +
                              $"use int:{reference.Name}");
                return false;
            }
            return _snapshot != null && _snapshot.Bool(reference);
        }

        public double Number(string field)
        {
            var reference = Resolve(field);
            if (reference == null || _snapshot == null) return 0;

            if (!_snapshot.TryNumber(reference, out double value))
            {
                ReportUnknown($"GameManager.{reference.Name} is {reference.Kind.Display()}, which is not a number");
                return 0;
            }
            return value;
        }

        public bool HasSkill(string skill)
        {
            if (!TryParseSkill(skill, out int ordinal)) return false;
            if (_snapshot == null || !_snapshot.GameManagerAlive) return false;

            var field = Resolve("skills");
            if (field == null) return false;

            long list = _snapshot.Pointer(field);
            if (list == 0) return false;

            if (!MonoReader.TryReadInt32List(_game.Runtime, _game.Memory, list, _listLayouts, _skills))
                return false;

            return _skills.Contains(ordinal);
        }

        /// <summary>
        /// The active scene. Three sources, in descending order of truth:
        ///
        /// 1. Unity's own scene state, via <see cref="NativeScene"/> — the live active
        ///    scene, which changes at the door.
        /// 2. timePaused, exact frame for frame but only able to answer `scene:menu`.
        /// 3. GameManager.currentScene, written only on save and load, so a split
        ///    keyed on entering an area lands at the next checkpoint. Kept so a Unity
        ///    update degrades visibly rather than lying.
        /// </summary>
        public string Scene => ResolveScene(_game.Scene.Read(_game.Memory), Alive, InMenu, SceneChanging, LastSaved);

        /// <summary>
        /// The ladder's decision, pure so the offline suite can pin it.
        ///
        /// The fourth rung is the one that matters. Mid-transition the native read goes
        /// dark for a window, and <paramref name="lastSaved"/> still names wherever the
        /// game last wrote a save — an area the run may never have entered. Answering
        /// with it fires that area's split on the way out of an unrelated scene, so a
        /// transition answers nothing at all instead.
        /// </summary>
        public static string ResolveScene(string native, bool gameManagerAlive, bool inMenu,
                                          bool sceneChanging, string lastSaved)
        {
            if (!string.IsNullOrEmpty(native)) return native;
            if (!gameManagerAlive) return "";
            if (inMenu) return "menu";
            if (sceneChanging) return "";
            return lastSaved ?? "";
        }

        private bool Alive => _snapshot != null && _snapshot.GameManagerAlive && _gameFields != null;

        private bool InMenu =>
            Alive && _gameFields.TimePaused != null && _snapshot.Bool(_gameFields.TimePaused);

        /// <summary>Either kind of load in flight — the same pair the clock carves out.</summary>
        private bool SceneChanging =>
            Alive && ((_gameFields.Loading != null && _snapshot.Bool(_gameFields.Loading))
                      || _snapshot.SceneLoadInFlight);

        private string LastSaved =>
            !Alive || _gameFields.CurrentScene == null
                ? ""
                : MonoReader.ReadString(_game.Memory, _snapshot.Pointer(_gameFields.CurrentScene));

        /// <summary>Where <see cref="Scene"/> is coming from, for the Debug tab.
        /// "native" is exact; "changing" means a transition is in flight and nothing is
        /// being answered; "approx" means splits land late.</summary>
        public string SceneSource =>
            _game.Scene.Resolved ? "native" : !InMenu && SceneChanging ? "changing" : "approx";

        public bool Credits => _creditsSeen;

        public bool NewGame => NewGameDetector.Pulse;

        public bool Collectible(string id) => Set(_collected, "collectible")?.Contains(id) ?? false;

        public bool Activated(string id) => Set(_activated, "activation")?.Contains(id) ?? false;

        public int CountPrefix(string prefix) => Set(_collected, "count")?.CountWithPrefix(prefix) ?? 0;

        /// <summary>How many ids each set holds, for the Debug tab.</summary>
        public int CollectedCount => _collected?.Entries.Count ?? -1;
        public int ActivatedCount => _activated?.Entries.Count ?? -1;

        /// <summary>Diagnostics for both id sets, for splitcheck --ids.</summary>
        public string DescribeSets()
        {
            if (_snapshot == null || !_snapshot.GameManagerAlive) return "GameManager is not up";

            var collected = Resolve("collectedCollectibles");
            var activated = Resolve("persistentActivations");
            return "collected:  " + (collected == null || _collected == null
                       ? "field not found"
                       : _collected.Describe(_snapshot.Pointer(collected))) +
                   "\nactivated:  " + (activated == null || _activated == null
                       ? "field not found"
                       : _activated.Describe(_snapshot.Pointer(activated)));
        }

        public IEnumerable<string> CollectedIds =>
            (IEnumerable<string>)_collected?.Entries ?? new string[0];
        public IEnumerable<string> ActivatedIds =>
            (IEnumerable<string>)_activated?.Entries ?? new string[0];

        private MonoStringSet Set(MonoStringSet set, string what)
        {
            if (set != null && set.Resolved) return set;

            ReportUnknown(set?.Problem != null
                ? $"{what}: {set.Problem}"
                : $"{what}: the game's id set is not readable yet — it fills in once you are in a level");
            return null;
        }

        /// <summary>The two private HashSet&lt;string&gt; fields behind the game's
        /// IsCollectibleCollected / IsPersistentActivated. Called once per tick; each
        /// set only re-reads its contents when its size changes.</summary>
        private void RefreshSets()
        {
            if (_snapshot == null || !_snapshot.GameManagerAlive || _game.Runtime == null) return;

            if (_collected == null) _collected = new MonoStringSet(_game.Runtime, _game.Memory);
            if (_activated == null) _activated = new MonoStringSet(_game.Runtime, _game.Memory);

            var collected = Resolve("collectedCollectibles");
            if (collected != null) _collected.Refresh(_snapshot.Pointer(collected));

            var activated = Resolve("persistentActivations");
            if (activated != null) _activated.Refresh(_snapshot.Pointer(activated));
        }

        public void ReportUnknown(string what)
        {
            if (!_reported.Add(what)) return;
            Log?.Invoke(what);
        }

        /// <summary>Everything reported so far, for the Debug tab.</summary>
        public IEnumerable<string> Problems => _reported;

        private MonoFieldRef Resolve(string name)
        {
            if (_fields.TryGetValue(name, out var cached)) return cached;

            MonoFieldRef found = _game.GameManager?.Find(name);
            if (found == null) ReportUnknown($"GameManager has no field '{name}'");
            else if (found.IsStatic)
            {
                ReportUnknown($"GameManager.{found.Name} is a static field; triggers read instance state");
                found = null;
            }

            _fields[name] = found;
            return found;
        }

        private bool TryParseSkill(string skill, out int ordinal)
        {
            if (int.TryParse(skill, NumberStyles.Integer, CultureInfo.InvariantCulture, out ordinal))
            {
                if (ordinal >= 0 && ordinal < SkillNames.Length) return true;
                ReportUnknown($"skill:{skill} — no such Skills value");
                return false;
            }

            for (int i = 0; i < SkillNames.Length; i++)
            {
                if (!string.Equals(SkillNames[i], skill, StringComparison.OrdinalIgnoreCase)) continue;
                ordinal = i;
                return true;
            }

            ReportUnknown($"skill:{skill} — no such Skills member ({string.Join(", ", SkillNames)})");
            return false;
        }

        /// <summary>The Skills enum, in ordinal order, so a config may name a skill or
        /// number it.</summary>
        public static readonly string[] SkillNames =
        {
            "DoubleJump", "Melee", "flames", "Dash", "Ghost",
            "HealthFlask", "FlashLight", "waterUpgrade", "ChargedAttack",
        };
    }
}
