using System;
using System.Collections.Generic;
using InfiniteSplitter.Diagnostics;
using InfiniteSplitter.Memory;

namespace InfiniteSplitter.Game
{
    /// <summary>
    /// The active scene, read from the engine rather than from a managed field. The
    /// managed value is only refreshed when the game saves, so a route that splits on
    /// entering an area would fire at the next checkpoint instead of at the door.
    ///
    /// UnityPlayer.dll exposes no metadata to walk, so this is the one place in the
    /// project using a fixed pointer chain instead of a resolved one — and therefore
    /// the one place a Unity update can invalidate. That is why the caller falls back
    /// rather than trusting whatever it reads.
    /// </summary>
    public sealed class NativeScene
    {
        public const string ModuleName = "UnityPlayer.dll";

        /// <summary>
        /// Verified against Unity 2022.3.62f2, in the order they are tried. All three
        /// reach the same string; each is kept because a Unity update is more likely to
        /// break one route than all of them, and <see cref="Read"/> moves on to the
        /// next as soon as one stops resolving.
        /// </summary>
        private static readonly string[] Candidates =
        {
            "UnityPlayer.dll+0x1D0F2E0 -> +0x10 -> +0x10 -> +0x0",
            "UnityPlayer.dll+0x1CCB950 -> +0x20 -> +0x10 -> +0x0",
            "UnityPlayer.dll+0x1C41880 -> +0x40 -> +0x10 -> +0x0",
        };

        private readonly List<PointerChain> _chains = new List<PointerChain>();
        private PointerChain _winner;
        private long _moduleBase;

        public NativeScene()
        {
            foreach (var text in Candidates)
            {
                var chain = PointerChain.Parse(text);
                if (chain != null) _chains.Add(chain);
            }
        }

        /// <summary>Which chain is in use, for the Debug tab. Null when unresolved.</summary>
        public PointerChain Winner => _winner;

        /// <summary>True while the native read is working; false means the caller must
        /// fall back to the managed approximation.</summary>
        public bool Resolved => _winner != null && _moduleBase != 0;

        /// <summary>Set when the module is missing, so the UI can say why.</summary>
        public string Problem { get; private set; }

        public void Bind(IProcessWrapper process)
        {
            _winner = null;
            _moduleBase = 0;
            Problem = null;

            var module = process?.FindModule(ModuleName);
            if (module == null)
            {
                Problem = ModuleName + " is not loaded — the scene falls back to the approximation";
                return;
            }
            _moduleBase = module.BaseAddress;
        }

        public void Detached()
        {
            _winner = null;
            _moduleBase = 0;
            Problem = null;
        }

        /// <summary>
        /// The active scene's bare name ("forest"), or "" when the native read is
        /// unavailable — an ordinary state, since a load leaves a window where the
        /// pointer is null, and the caller falls back rather than reporting a wrong
        /// scene. The winning chain is re-validated every call, so one that goes stale
        /// hands off to the next candidate instead of returning a stale name.
        /// </summary>
        public string Read(IMemory memory)
        {
            if (_moduleBase == 0 || memory == null) return "";

            if (_winner != null)
            {
                string current = NameFrom(_winner.ResolveAscii(memory, _moduleBase));
                if (current.Length > 0) return current;
                _winner = null;                     // it stopped working; re-pick below
            }

            foreach (var chain in _chains)
            {
                string name = NameFrom(chain.ResolveAscii(memory, _moduleBase));
                if (name.Length == 0) continue;

                _winner = chain;
                return name;
            }
            return "";
        }

        /// <summary>
        /// "Assets/Scenes/forest.unity" -> "forest"; anything that is not an asset path
        /// -> "". That shape check is the whole validation, and it is enough: a wrong
        /// chain lands on arbitrary bytes, which will not look like this.
        /// </summary>
        internal static string NameFrom(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return "";
            if (!assetPath.EndsWith(".unity", StringComparison.Ordinal)) return "";
            if (assetPath.IndexOf("Assets/", StringComparison.Ordinal) < 0) return "";

            int slash = assetPath.LastIndexOf('/');
            string name = assetPath.Substring(slash + 1, assetPath.Length - slash - 1 - ".unity".Length);

            // Scene names reach the trigger language exactly as the game spells them,
            // spaces included, so this cannot be an identifier check.
            foreach (var c in name)
                if (c < 0x20 || c > 0x7E) return "";

            return name;
        }
    }
}
