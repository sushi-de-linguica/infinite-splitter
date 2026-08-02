# infinite-splitter

A **LiveSplit component** for Infinitevania: in-game time into LiveSplit's Game
Time, plus auto-splitting. It reads the game's memory from outside the process —
nothing is installed into the game folder, nothing is injected, nothing is
written.

<img width="984" height="668" alt="image" src="https://github.com/user-attachments/assets/43cf6b5b-b313-49f7-95cc-6e83a6cf7f39" />

## Install

1. Close LiveSplit — it locks component DLLs while running.
2. Unzip `InfiniteSplitter-<version>.zip` and drop `InfiniteSplitter.dll` into
   LiveSplit's `Components` folder, next to `LiveSplit.exe`. That is the whole
   install: one file, no dependencies to copy alongside it.
3. Start LiveSplit, then Edit Layout > + > Control > InfiniteSplitter.
4. Edit Layout > Layout Settings > InfiniteSplitter to configure.

Steam must be running for the game to resolve its save folder.

Later versions install the same way, or arrive through LiveSplit's own updater
under Help > Check for Updates.

## Out of the box

The timer starts when you start a new game and not a moment before — the menu
and continuing a save leave it alone. Quitting to the menu mid-run does not throw
the run away; only a new game does, and that one opens a fresh attempt.

Loading screens and the transitions between areas are removed from Game Time, so
it reads behind the number the game's own pause menu shows. The Debug tab prints
both and how much was removed; the Timer tab turns it off if you would rather
they matched exactly.

## Using it

The settings dialog has five tabs:

| Tab | |
|---|---|
| Timer | whether to drive Game Time from the game's clock, what to remove from it, and where it starts |
| Run | when a run starts, splits and resets |
| Splits | the split list, with presets for the common conditions |
| Advanced | the whole configuration as editable text |
| Debug | whether the game was found, and what the component is reading right now |

Splits are conditions over the game's own state, written as expressions —
`flag:melogBestiary`, `int:heartFragments>=4`, `scene:forest`, `ending:any`.
The presets cover the usual route; [docs/splits.md](docs/splits.md) covers the
rest of the language.

If something does not fire, read the **Debug** tab first: it names an unknown
flag, an unresolved read, or a scene reading `(approx)` — each of which explains
a split that lands late or never.

## Build

```bash
make            # list the targets
make test       # offline suite — no game, no LiveSplit, runs on Linux
make all        # test + compile + Components/ + release zip
make deploy     # into LiveSplit's Components folder
make publish    # tag, push, and open a draft GitHub release
```

Building requires nothing but the .NET SDK. `./build.sh` is kept as an alias for
`make all`.

## Layout

| Path | |
|---|---|
| `src/InfiniteMemory/` | netstandard2.0. No LiveSplit, no WinForms, no Unity — which is what lets it be tested on Linux |
| `src/InfiniteSplitter/` | net481. The LiveSplit component and its WinForms settings UI |
| `tools/splitcheck/` | net8.0. Offline suite and the live attach modes |
| `docs/` | everything below |
| `packages/` | `LiveSplit.Core.dll`, `UpdateManager.dll` — compile-time only |
| `Components/` | build output; the single DLL a user drops into LiveSplit |

## Documentation

| | |
|---|---|
| [docs/splits.md](docs/splits.md) | the split expression language, and how to find the names it takes |
| [docs/development.md](docs/development.md) | build and test targets, toolchain, layout, comment policy |
| [docs/testing.md](docs/testing.md) | what the offline suite pins |
| [docs/architecture.md](docs/architecture.md) | the three assemblies, the tick, the error model, the trigger language |
| [docs/splitcheck.md](docs/splitcheck.md) | the live attach tool: watching and evaluating triggers |
| [docs/releasing.md](docs/releasing.md) | versioning, the update manifest, incomplete installs |
| [CLAUDE.md](CLAUDE.md) | orientation and invariants, for AI agents working in this repo |

## Credits

The architecture — a LiveSplit `IComponent` with `ComponentCategory.Control`, a
memory layer with no LiveSplit reference, `Result`/`RefreshError` instead of
exceptions, and a process hook with `Hooked`/`Exited` — follows
[SoulSplitter](https://github.com/FrankvdStam/SoulSplitter) by FrankvdStam. The
code here is our own; the design lessons are theirs.

And Thanks for [Nyk_Style](https://www.speedrun.com/users/Nyk_Style) for testing!

`packages/` holds two assemblies from [LiveSplit](https://github.com/LiveSplit/LiveSplit)
(MIT), unmodified and compile-time only. [packages/PROVENANCE.md](packages/PROVENANCE.md)
records which release they came from.

## License

GPL-3.0-or-later. See [LICENSE](LICENSE).
