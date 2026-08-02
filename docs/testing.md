# Testing

```bash
make test          # the whole offline suite
make all           # tests + build + package
```

`make test` runs `splitcheck --selftest`. It needs no game, no LiveSplit and no
Windows: every fixture is a byte array, so the suite runs on Linux in about a
second and the release is gated on it.

Everything under test lives in `src/InfiniteMemory/`, which references neither
LiveSplit nor WinForms nor Unity. That constraint is what makes an offline suite
possible at all, and it is worth keeping.

## What the suite covers

`tools/splitcheck/Program.cs` runs five groups. Each returns a failure count and
prints `ok` or the failing assertions; the process exit code is non-zero if any
group failed.

| Group | File | Pins |
|---|---|---|
| `mono` | `MonoTests.cs` | the accessor decoder, the PE export parser, and a full resolve against a synthetic runtime |
| `igt` | `IgtTests.cs` | the in-game clock: advance, save-load rewind, mirroring, rebasing, slot resolution |
| `newgame` | `NewGameTests.cs` | the two new-game signals and the one-tick pulse |
| `timeline` | `TimelineTests.cs` | where the run's clock comes from, and that a finished run holds its last split |
| `settings` | `SettingsTests.cs` | XML and INI round trips, corrupt-document fallback, migration, the Status report |
| `scan` | `ScanTests.cs` | the pointer scanner, `PointerChain` and the read-into contract, against a synthetic address space |

### mono — the synthetic heap

`MonoHeapBuilder` writes a complete fake Mono process into a `ByteArrayMemory`:
a PE module with a real export directory and real instruction bytes, a domain, an
assembly list, an image, a **chained** class cache, field arrays, a vtable and
static storage.

It is the highest-value fixture in the project, because it can be corrupted on
purpose. Three cases matter more than the happy path:

- **`RelocatedClassCacheIsFound`** — move the class-cache pointer and the
  resolver must still find every class. This is the offline guard on the search
  the resolver falls back to when the compiled-in offset does not fit the
  runtime it attached to.
- **`MissingClassCacheFailsLoudly`** — with no class cache anywhere, attaching
  must fail with `LayoutMismatch`. A splitter that silently reads the wrong
  field is worse than one that refuses to start, and this is the test that says
  so in code.
- **`NameOffsetIsRecovered`** — move every class-name pointer to another offset
  and the candidate search must recover it. `0x30` is used rather than an offset
  the probe already owns: clobbering `ClassImage` would break the walk itself
  instead of testing the name search.

`OffsetProbeDecoding` also asserts the negative case — a real function prologue
must **not** decode as a one-instruction accessor, or a large function would
hand back a garbage offset.

### igt — behaviours of the game, not of our code

Every case is a behaviour observed on a real run, so the reader has to reproduce
all of them:

- Loading a save rewinds the game's counter, and the clock follows it down rather
  than holding a high-water mark — which is what lets a run survive a save / quit
  to menu / load cycle showing the right time.
- Nothing is carved out of the total: loading screens and the pause menu count,
  because the game counts them. A number the player cannot check against the one
  the game shows them is the failure this pins down.
- The slot the player is on is picked from the game's own state, and that state
  goes briefly ambiguous while the menu's slot list is up — so the resolver keeps
  the last known slot rather than snapping back to 1 and reporting some other
  save's time.

The field offsets in the fixture are synthetic on purpose: the point is
`IgtSource`'s arithmetic, not the real layout, which the `mono` group covers.

### newgame — the first run of a session

The run counter's storage does not exist until the first new game of a session,
so its value goes from unavailable straight to 1 with no baseline to have risen
from. A detector watching only for a rise misses it entirely — which is the gap
`FirstNewGameOfTheSession` exists to catch, and it caught it during bring-up.

The second signal is a slot clock at zero while the game reports itself paused on
the menu, a state only a fresh run produces. `LoadingASaveIsNotANewGame` pins the
other side of that: a loaded save always carries its own elapsed time.

### scan — the address-space fixture

Same bargain as the Mono heap: a fake address space shaped like the real thing —
a static in a module's image pointing at a heap object whose field points at a
string.

- `WalksBackToTheModuleStatic` is the point of the exercise. The static holds
  the object's *head*, not the field, so the second level only connects if the
  scan tolerates an offset; with `maxOffset` at 0 this test fails.
- `SpansChunkBoundaries` places a needle exactly across the 1 MB read boundary,
  the one case a naive chunked scan drops.
- `ChainSurvivesARebasedModule` rebuilds the fixture at a different module base,
  which is the whole reason chains are stored module-relative.
- `SceneNamesComeOutOfAssetPaths` and `GarbageIsNotMistakenForAScene` pin
  `NativeScene.NameFrom`, including a scene name containing a space — so the
  check cannot be a plain identifier test.
- `AFailedReadZeroesTheBuffer` pins the `IMemory.TryReadInto` contract. The
  snapshot reuses its buffers between ticks, so a read that fails has to leave
  zeros rather than the previous tick's bytes: stale state read as current is how
  a split fires on nothing.

`NameFrom` is internal; splitcheck reaches it through the `InternalsVisibleTo`
in `src/InfiniteMemory/AssemblyInfo.cs`, the same way it reaches
`GameSnapshot`'s setters.

## Live validation

The offline suite proves the code is self-consistent. It cannot prove the reader
agrees with the running game, and `--watch` is the check that does:

```bash
make splitcheck                 # publish for win-x64
splitcheck.exe --attach --watch
```

It prints the IGT, the measured clock rate (1.000 while playing), the scene and
the run count. Full reading of that output, and the other live modes, in
[splitcheck.md](splitcheck.md).

## Adding a test

Add the case to the group it belongs to and call it from that group's `Run()`.
The convention throughout is deliberately plain — no test framework, because a
framework would be a NuGet dependency and the suite has to build anywhere the
component does. A case returns the number of failures and prints `ERROR <what>`
for each one.
