# splitcheck — the live attach tool

`tools/splitcheck/` is one binary with two jobs. On Linux it runs the offline
suite ([testing.md](testing.md)); published for win-x64 it attaches to the
running game, so the memory layer can be brought up and debugged with LiveSplit
entirely out of the picture.

```bash
make splitcheck     # publish to PUBLISH_DIR (default: a temp dir on C:)
```

It lands in `PUBLISH_DIR`, not in the repo, and nothing puts it on `PATH` — so
every invocation below needs the full path. From WSL, Windows interop runs it
in place:

```bash
"/mnt/c/Users/$USER/AppData/Local/Temp/splitcheck/splitcheck.exe" --attach --watch
```

`--watch` redraws one line with a carriage return, so piping it anywhere wants
`| tr '\r' '\n'` to come out as rows.

Steam must be running for the game to resolve its save folder.

| Mode | |
|---|---|
| `--selftest` | the offline suite; no game, no LiveSplit |
| `--attach --dump` | everything the resolver worked out |
| `--attach --watch` | live table at 4 Hz: IGT, loading removed, clock rate, load signals, scene |
| `--attach --ids` | every collectible and activation id the save holds |
| `--attach --eval "<trigger>"` | evaluate a trigger expression against the running game |
| `--attach --find-scene <name>` | re-derive the scene pointer chain after a Unity update |
| `--attach --watch-scene --chains <file>` | check candidate chains through a transition |

## Getting moving fast

```bash
cd infinite-splitter
make all                                      # selftest + compile + package
make splitcheck                               # publish the attach tool for win-x64
"$PUBLISH_DIR/splitcheck.exe" --attach --watch
```

`--watch` is the fastest sanity check that everything still resolves. It prints
the IGT and the raw clock behind it, how much loading has been removed, the
measured rate, both load signals and the scene:

- the **rate** should sit at 1.000 while playing. Anything far off means the
  wrong field or the wrong slot.
- **`raw`** is what the game's own pause menu shows, digit for digit. **`igt`**
  is what LiveSplit gets, and **`cut`** is the difference, so `raw - cut` has to
  equal `igt`.
- **`loading`** is a save being read; **`sceneLoad`** is a portal, fast travel or
  a return to the menu. Walk through a portal and the second has to go true for
  the whole fade — that is the half `loading` alone never covered.
- a scene reading **`(native)`** means the live read resolved. **`(approx)`**
  means it fell back and `scene:` splits will land at the next save rather than
  at the door — and the scenes that are never saved (the two endings, the
  credits) stop being reachable at all.
- **`runs = -1`** is a state, not a fault, and nothing depends on it — the column
  is there to cross-check the detector, which announces every new game it sees on
  its own line.

## Authoring a route

`--eval` is how a split condition is written: type the expression, stand in front
of the boss, watch it flip.

```bash
splitcheck.exe --attach --eval "flag:melogBestiary & !scene:menu"
```

`--ids` lists what `collectible:` and `activation:` can name for the current
save. See [splits.md](splits.md).

## After a Unity update

Everything the Mono resolver needs re-derives itself at attach and either keeps
working or fails loudly in the Debug tab. The one exception is the live scene
read, whose pointer chain is fixed at build time; `--find-scene` and
`--watch-scene` exist to replace it when a new Unity version moves it, and until
then the component falls back to the approximate scene on its own.
