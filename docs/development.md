# Development

## Entry points

Everything goes through the Makefile; `make` alone lists the targets.

| | |
|---|---|
| `make test` | the offline suite — no game, no LiveSplit, runs on Linux. See [testing.md](testing.md) |
| `make build` | compile the component |
| `make components` | compile and refresh `Components/`, which is what a user installs |
| `make release` | `components` + version check + `dist/InfiniteSplitter-<version>.zip`. See [releasing.md](releasing.md) |
| `make all` | test + release, the full gate |
| `make publish` | build, tag, push, open a draft GitHub release. See [releasing.md](releasing.md) |
| `make publish-dry` | the same, stopping before anything is pushed |
| `make deploy` | copy `Components/` into LiveSplit (`LIVESPLIT_DIR=…`) |
| `make splitcheck` | publish the live attach tool for win-x64 (`PUBLISH_DIR=…`). See [splitcheck.md](splitcheck.md) |
| `make clean` | remove `bin/`, `obj/` and `dist/` |

`build.sh` is kept as the documented entry point and just calls `make all`.
`deploy.sh` and `test.sh` are git-ignored: they hardcode machine-specific paths.

## Toolchain

Building needs nothing but the .NET SDK — no game install, no Windows, no
LiveSplit. `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` is set by the Makefile because
there is no libicu in WSL and `dotnet` crashes outright without it.

The component project uses the **plain** `Microsoft.NET.Sdk`, never
`Microsoft.NET.Sdk.WindowsDesktop`, and never `<UseWindowsForms>` / `<UseWPF>`:
those pull in Windows-only MSBuild targets that cannot run on Linux. Referencing
`System.Windows.Forms` by name and letting
`Microsoft.NETFramework.ReferenceAssemblies` resolve it builds fine from WSL.

The consequence is deliberate: no `.resx`, no `.Designer.cs`, no
`ApplicationIcon`. **Every control is constructed in code.**

`net481` rather than `net48` because the LiveSplit 1.8.37 assemblies in
`packages/` are themselves net481, and referencing them from net48 fails to
resolve. They are compile-time only (`<Private>false</Private>`) — at runtime
they come from LiveSplit itself.

## Layout

| Path | |
|---|---|
| `src/InfiniteMemory/` | netstandard2.0. No LiveSplit, no WinForms, no Unity — which is what lets it be tested on Linux |
| `src/InfiniteSplitter/` | net481. The LiveSplit component and its WinForms settings UI |
| `tools/splitcheck/` | net8.0. The offline suite and the live attach modes |
| `tools/package.py` | builds the release zip from `Components/` |
| `tools/publish.sh` | the release flow: verify, build, tag, push, draft release |
| `tools/dist-readme.txt` | source of `Components/readme.txt`; the Makefile converts it to CRLF |
| `packages/` | `LiveSplit.Core.dll`, `UpdateManager.dll` — compile-time only |
| `Components/` | build output; the single DLL a user drops into LiveSplit, committed so the updater can fetch it |
| `docs/` | this folder |

[architecture.md](architecture.md) explains why the split between the three
projects is load-bearing rather than tidiness.

## Comments

The rule this codebase follows:

- **Keep** what the code cannot say: where a magic number came from (every
  constant in `MonoOffsets.cs` names the runtime function its bytes were read out
  of), why an obvious alternative is wrong, and contracts a caller has to honour
  (`IMemory.ReadBytes` never throws; a static field's `Offset` means something
  different from an instance field's).
- **Keep** short XML docs on public types and non-obvious members.
- **Drop** anything that restates the code, decorative section banners, and
  narrative about how something was discovered.
- **Describe the splitter, not the game.** Name a field where the code reads it;
  do not document how the game implements it, where the name came from, or what
  else lives near it. That belongs to the game's authors, not to this repository.
- **Move** longer explanations into `docs/` and point at them from a one-line
  comment.

Documentation is in English, including in repositories whose commit messages are
not.

## Working on the memory layer

Nothing in `src/InfiniteMemory/` may reference LiveSplit, WinForms or Unity. If a
rule needs testing, it belongs there; if it ends up in the WinForms assembly it
stops being reachable from `make test`.

`ByteArrayMemory` stands in for a live process, which is how the whole runtime
resolver is developed without the game running. When a live bug needs pinning
down, the shape to reach for is a fixture in the suite rather than another round
of `--watch`.
