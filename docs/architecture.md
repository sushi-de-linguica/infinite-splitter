# Architecture

Three assemblies, and the split between them is what makes the project testable
on Linux with no game and no LiveSplit.

| Project | Target | Knows about |
|---|---|---|
| `src/InfiniteMemory/` | netstandard2.0 | nothing but Win32 P/Invoke. No LiveSplit, no WinForms, no Unity |
| `src/InfiniteSplitter/` | net481 | LiveSplit and WinForms; almost no logic |
| `tools/splitcheck/` | net8.0 | the memory layer, for the offline suite and live attach |

`InfiniteMemory` holds everything worth testing: the runtime resolver, the
snapshot, the IGT clock, the trigger language, the timeline and the settings
model. `InfiniteSplitter` adapts it to LiveSplit's `IComponent` and builds the
settings UI. Keep it that way — the moment a rule moves up into the WinForms
assembly it stops being reachable from `make test`.

## Resolving state by name

The game is a Unity title on the Mono backend, so the values the splitter needs
are managed fields with no fixed address. `src/InfiniteMemory/Mono/` walks the
Mono runtime's own metadata — domain, assembly, image, class, field table,
vtable — to turn a name into an address. Because this Mono uses a non-moving
collector, that resolution happens **once** at attach and stays valid for the
life of the process; per tick only the instance pointer is re-read.

Nothing in that walk is trusted blind. A class is accepted only if it belongs to
the image the walk started from, a field only if it points back at its class, a
vtable only if it points back at the class — and every count and chain length is
bounded, so a wrong offset cannot hang. Where a layout constant cannot be
measured from the runtime's own exported accessors, it is searched for at attach
and the result reported in the Debug tab.

That is the whole reason the error model below exists: a splitter that silently
reads the wrong field is worse than one that refuses to start.

## The tick

`InfiniteSplitterComponent.Update` runs once per LiveSplit frame:

```
TryRefresh          attach if needed; cheap liveness check if already attached
TakeSnapshot        two bulk reads of the objects we watch, plus the run count
IgtSource.Update    the game's five slot clocks -> one number, less loading
NewGameDetector     leaving the menu without a save load -> a one-tick pulse
MemoryProbe.Bind    point the trigger language at this tick's snapshot
Timeline.Tick       new game > reset > start > split, at most one action
TimerModelLink      drive LiveSplit, with the reentrancy guard raised
```

Each watched object is copied whole once per tick, so a trigger asking twenty
questions costs no more syscalls than one asking two. That is also what makes a
recorded snapshot a complete, replayable fixture.

## Errors are values

Nothing in the memory layer throws across a boundary. An exception escaping into
LiveSplit's render loop takes the whole timer down, and "the game is not running"
is an ordinary state that should not cost a stack trace.

- `Result` / `Result<T>` carry a `RefreshError` with a closed set of reasons, so
  the component can decide to back off from the reason alone and the settings UI
  can explain each one in the player's own terms.
- `IMemory.ReadBytes` never throws and never returns null: an unreadable address
  yields zeros. A null link in a pointer chain is a state, not a fault.
- `IMemory.TryReadInto` is the same read into a caller-owned buffer, so the
  per-tick path does not allocate. A failed read **zeroes the buffer** before
  returning false: the snapshot reuses its buffers between ticks, and leaving the
  previous contents would serve stale state as current — a dead flag reading as a
  live one is how a split fires on nothing.
- Callers therefore guard by **validating what they read**, never by catching.

## Nothing is written

The process handle is opened with `QueryInformation | VmRead` only. There is no
`VirtualAllocEx`, no `CreateRemoteThread`, no `WriteProcessMemory` anywhere in
`NativeMethods` — `ProcessWrapper.WriteBytes` throws by construction. The game
runs unmodified, with nothing installed into its folder and no launch flag.

## We own Game Time, and have to keep saying so

`LiveSplitState.IsGameTimePaused` does not mean "the run is paused". It means
LiveSplit will not advance Game Time on its own, and will show whatever
`SetGameTime` last stored — which is the whole basis of this component, since the
number comes from the game's clock, not from a stopwatch.

The trap is that **`TimerModel.Reset` clears it**. The chain, read out of
LiveSplit.Core's IL:

```
Reset()  ->  Reset(true)  ->  ResetState()  ->  IsGameTimePaused = false
```

and `Timeline.DoStart` resets before every start, so setting the flag once at
construction only holds until the first run begins. `LiveSplitState.SetGameTime`
stores `GameTimePauseTime` **only while the flag is set**; with it clear, Game
Time falls back to real time minus `LoadingTimes` and free-runs between our
writes, each write yanking it back.

While playing, our clock also advances at about 1×, so the drift inside one frame
is invisible. The moment the clock is deliberately held — a finished run, which
sits on the time of its last split — ours freezes while LiveSplit's creeps
forward, and the display flickers between the two.

That is also why a finished run keeps writing rather than stopping: the value it
writes stops changing, the writing does not. Stop writing and Game Time resumes on
real time, which is the failure this flag exists to prevent.

So `TimerModelLink.SetGameTime` re-asserts the flag on every write. That method is
the single funnel for both the per-tick push and the writes `Timeline` makes
around a split, which is why the invariant is enforced there rather than in the
component's `Update`.

## When a run starts

The `newgame` pulse fires when the active scene leaves the menu without a save
having been loaded during that visit. Both halves are load-bearing:

- **Leaving the menu** is the only way into a run — nothing in the game restarts
  one from inside — and it is also the frame the game's own clock starts
  counting on, so a run that starts here reads 0:00.000 rather than picking up
  part of the fade.
- **A save load raises `GameManager.loading`** a full second before the scene
  load begins and lowers it a frame into the destination. `NewGameDetector`
  latches that for the whole menu visit instead of sampling it at the
  transition, which is what makes the distinction independent of the poll rate:
  a slow tick can easily land after the flag has cleared, and the load would
  then read as a new game.

Two signals that look usable and are not. A slot clock reading zero: a new game
does not zero every slot, so it both misses and false-positives — and on the
menu, before the game has read the saves off disk, every clock reads zero, which
is why an earlier version of this started the timer the moment the game was
launched. The run counter rising: its storage does not exist until the first new
game of a session, and it would fire a third of a second ahead of the scene
edge, giving two different start points across attempts. It is still read, but
only to be shown in the Debug tab.

## Load removal

`remove_loads` carves loading back out of the mirrored clock. It has to be us
that does it, because the game's own clock does not: it runs on unscaled time and
only freezes on the menu, so every load is in the in-game total.

Two signals, because the game has two kinds of load and only reports one of them
as such:

- `GameManager.loading` — loading a save, from the menu or after a death,
  including the second the game waits before starting the scene load.
- `UIManager.currentScene` non-empty — portals between areas, cross-scene fast
  travel, quitting to the menu, cutscene loads. The field is empty in every
  scene as shipped and is written only by the transition entry points, before
  the fade; the destination scene brings up its own `UIManager` with the field
  empty again, so non-empty means exactly "a transition was started and has not
  completed". Only its length is read.

The second has to be discarded while the menu is up: the menu has no `UIManager`
of its own, so the read is the previous scene's and stays set the whole time. The
clock is frozen there anyway, so nothing is miscounted either way, but
`IgtSource.Excluding` would otherwise misreport what it is doing.

Only a plausible tick's worth of time may feed the accumulator. A negative delta
is a save being loaded; a large positive one is the counter being read having
been swapped for another slot's. Neither is time that passed.

The total belongs to one attempt, so the component rebases the clock on
`Timeline.StartCount` changing rather than on the phase reaching Running. A new
game restarting a run in progress — the default, and what quitting to the menu
and starting again does — goes Running to Running, so the phase edge never
fires. Missing it left each attempt carrying every previous attempt's removed
load, and `Value` is clamped at zero, so the new run's clock sat at 0:00 until
the game's counter passed that total.

The pause menu is deliberately not removed: the game counts it, the player can
see it on the screen they paused from, and a number they cannot check against the
game's own is worse than a slightly longer one. Loading is the exception because
there is no screen to check it against.

Deliberately not used as signals: `UIManager.canPause`, which is also false
during healing, ghost dash, hitstop, dialogue, every pickup, every checkpoint and
every upgrade panel; and the respawn-animation flag, which is equally true for
the spike and lava death animations, which are gameplay.

## The trigger language

A split condition is an expression over named game state, parsed by
`Core/Trigger.cs`: OR of ANDs, no parentheses, with atoms like `flag:sword`,
`int:heartFragments>=4`, `skill:Dash`, `collectible:<id>`, `scene:forest`,
`ending:any`, `credits`, `newgame`. `Timeline` walks the split list in order and
performs at most one action per tick. See [splits.md](splits.md).

`IProbe` is the whole interface between the language and the game, which keeps
the grammar and its tests independent of how any single value is read.

## Lineage

The architecture — a LiveSplit `IComponent` with `ComponentCategory.Control`, a
memory layer with no LiveSplit reference, `Result`/`RefreshError` instead of
exceptions, and a process hook with `Hooked`/`Exited` — follows
[SoulSplitter](https://github.com/FrankvdStam/SoulSplitter) by FrankvdStam. The
code here is our own; the design lessons are theirs.

SoulSplitter ships several files; this one ships a single DLL, with
`InfiniteMemory` merged in at build time — see
[releasing.md](releasing.md#one-dll).
