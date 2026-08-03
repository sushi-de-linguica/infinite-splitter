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
| `packages/` | `LiveSplit.Core.dll`, `UpdateManager.dll` — compile-time only. `SHA256SUMS` pins their bytes; `PROVENANCE.md` records where they came from |
| `Components/` | build output; the single DLL a user drops into LiveSplit, committed so the updater can fetch it |
| `docs/` | this folder |

[architecture.md](architecture.md) explains why the split between the three
projects is load-bearing rather than tidiness.

## Continuous integration

CI covers **security only** — it does not build the component and does not run
the offline suite. `make all` before pushing is still the gate that says the
change compiles; nothing on GitHub will say it for you.

| Workflow | What it does |
|---|---|
| `codeql.yml` | Static analysis of our C#. `build-mode: none`, so it never compiles — the trade is that types from the vendored assemblies go unresolved and `src/InfiniteSplitter/` is analysed more shallowly than `src/InfiniteMemory/` |
| `supply-chain.yml` | Verifies `packages/SHA256SUMS`, refuses an unpinned DLL beside the pinned ones, restores in locked mode, fails on a vulnerable NuGet package, and scans for leaked secrets |
| `scorecard.yml` | Grades the repository's own posture and publishes to the Security tab |
| `dependabot.yml` | Weekly NuGet and GitHub-Actions updates. Runs on GitHub's infrastructure, so it costs no Actions minutes |

Every third-party action is pinned to a **full commit SHA**, with the tag beside
it in a comment. Dependabot is what moves those pins — replacing one with a
floating tag silently opts out of the whole arrangement.

Two dependency pins are load-bearing and are checked on every pull request:

- `src/InfiniteSplitter/packages.lock.json` — committed, and restored with
  `--locked-mode`. After changing a `PackageReference`, regenerate it with
  `dotnet restore src/InfiniteSplitter --force-evaluate` and commit the result,
  or CI will reject the build.
- `packages/SHA256SUMS` — regenerate only when a vendored DLL is updated on
  purpose. The command and the reasoning are in
  [`packages/PROVENANCE.md`](../packages/PROVENANCE.md).

What CI does **not** cover: nothing proves that the committed
`Components/InfiniteSplitter.dll` — the file LiveSplit's updater hands to users —
was built from the source in this tree. That check needs a build, and would have
to compare hashes rather than run `git status`, because `make components`
rewrites the tracked file by design.

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
