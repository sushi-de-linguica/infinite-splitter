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
IgtSource.Update    the game's five slot clocks -> one number
NewGameDetector     run count and fresh-clock signals -> a one-tick pulse
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
is invisible. The moment the clock is deliberately held — the pause menu with
"exclude pause time" on, or a loading screen — ours freezes while LiveSplit's
creeps forward, and the display flickers between the two.

So `TimerModelLink.SetGameTime` re-asserts the flag on every write. That method is
the single funnel for both the per-tick push and the writes `Timeline` makes
around a split, which is why the invariant is enforced there rather than in the
component's `Update`.

## The trigger language

A split condition is an expression over named game state, parsed by
`Core/Trigger.cs`: OR of ANDs, no parentheses, with atoms like `flag:sword`,
`int:heartFragments>=4`, `skill:Dash`, `collectible:<id>`, `scene:forest`,
`credits`, `newgame`. `Timeline` walks the split list in order and performs at
most one action per tick. See [splits.md](splits.md).

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
