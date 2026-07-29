# infinite-splitter — agent orientation

A **LiveSplit component** that reads Infinitevania's memory from outside the
process. The game runs unmodified: no injection, no launch flag, nothing
written.

Detail lives in `docs/` — read the one that matches the task rather than the
whole set.

| Task | Read |
|---|---|
| write or debug a split condition | [docs/splits.md](docs/splits.md) |
| build, test, add a file | [docs/development.md](docs/development.md) |
| touch the offline suite | [docs/testing.md](docs/testing.md) |
| change how a tick works | [docs/architecture.md](docs/architecture.md) |
| cut a release, or change how one is built | [docs/releasing.md](docs/releasing.md) |
| debug against the running game | [docs/splitcheck.md](docs/splitcheck.md) |

## Commands

```bash
make            # list targets
make test       # offline suite; runs on Linux, no game, no LiveSplit
make all        # the full gate: test + build + Components/ + zip
```

Always finish with `make test` at minimum, and `make all` before claiming a build
works. `./build.sh` is an alias for `make all`.

## Invariants — breaking one of these is a bug even if it compiles

1. **`src/InfiniteMemory/` may not reference LiveSplit, WinForms or Unity.** That
   constraint is what lets the whole stack be tested on Linux. Logic that drifts
   up into `src/InfiniteSplitter/` stops being reachable from `make test`.
2. **Nothing throws across a boundary.** Return `Result` / `Result<T>` with a
   `RefreshError`. An exception reaching LiveSplit's render loop takes the whole
   timer down. `IMemory.ReadBytes` returns zeros for an unreadable address, and
   `TryReadInto` **zeroes the caller's buffer** before reporting failure — a null
   pointer is a state, not a fault, and a reused buffer must never serve last
   tick's bytes as if they were current.
3. **Never write to the game.** The handle is opened read-only and
   `NativeMethods` deliberately has no write-side P/Invoke. Do not add one.
4. **Fail loudly, never plausibly.** Every metadata walk validates what it read
   and reports `LayoutMismatch` rather than returning a wrong-but-shaped answer.
   A splitter that silently reads the wrong field is worse than one that refuses
   to start.
5. **Runtime layout constants carry their provenance.** A new constant in
   `MonoOffsets.cs` needs a comment naming the exported function whose bytes it
   was read from. If it cannot be measured, mark it `(assumed)` and guard it with
   a search at attach.
6. **The version is declared once**, in `InfiniteSplitter.csproj`.
   `Components/Updates.xml` must be bumped to match or `make release` fails.
   `Components/*.dll` is committed on purpose: LiveSplit's updater fetches the
   built artefact from the repository, not from a GitHub release.
7. **The component ships as one DLL.** `InfiniteMemory` is merged in on Release
   by `src/InfiniteSplitter/ILRepack.targets`. Nothing may assume a second file
   sits next to the plugin.
8. **Game Time is ours, and the claim expires.** `TimerModel.Reset` sets
   `IsGameTimePaused = false`, and every run start resets first — so
   `TimerModelLink.SetGameTime` re-asserts it on each write. Without that,
   LiveSplit free-runs Game Time on real time between writes and the display
   flickers wherever our clock is deliberately held (pause, loading).

## Conventions

- Comments: keep provenance, contracts and the reason an obvious alternative is
  wrong. Drop restatement, decorative banners and discovery narrative. Longer
  explanations go in `docs/` with a one-line pointer from the code. Everything in
  **English**.
- **Documentation and comments describe the splitter, never the game's
  internals.** Name a field only where the code has to read it; do not explain
  how the game implements it, where the name was found, or what else lives near
  it.
- Tests: no framework, by design. A case returns a failure count and prints
  `ERROR <what>`; register it in its group's `Run()`.
- UI: every control is built in code. No `.resx`, no designer files — they would
  break the WSL build.
- Prefer adding a fixture to the offline suite over another round of live
  `--watch` when pinning a bug down.

## Things that look like bugs and are not

- `GameManager.instance == 0` on the title screen: the singleton is not up yet.
- `runs = -1` until the first new game of a session — the counter's storage does
  not exist before then.
- A scene reading `(approx)`: the live read did not resolve and the fallback is
  in use. Splits will land late, visibly — except on scenes the game never saves
  in (the two endings and the credits), which the fallback cannot report at all.
- The clock losing a frame per minute — that is the game's own rounding, and
  mirroring it is what keeps LiveSplit agreeing with the in-game timer.
