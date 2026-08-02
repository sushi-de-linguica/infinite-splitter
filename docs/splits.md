# Writing splits

A split condition is an expression over the game state the component can see.
The settings dialog ships the common ones as presets and validates what you type
as you type it, so you only need this page when you outgrow the list.

## The preset list

Everything in the **Preset** dropdown on the Splits tab. Picking one fills in the
name and the trigger; both stay editable, so the fastest way to a new split is
usually the nearest preset plus a guard.

The dropdown labels entries `Group — Name`.

### Boss

The route, in order. Each boss also hands over the ability that opens the next
stretch, in the same frame the flag flips — so the boss row and its Skill row
below fire at the same instant.

| Dropdown entry | Trigger | Fires on | Grants |
|---|---|---|---|
| Boss — Tenroh | `flag:tenrohBestiary` | Tenroh's death, forest | Charged Attack |
| Boss — Queen Wasps | `flag:queensWaspsBestiary` | Queen Wasps' death, forest | Dash |
| Boss — Galegrim | `flag:galegrimBestiary` | Galegrim's death, volcano | Double Jump |
| Boss — Melog | `flag:melogBestiary` | Melog's death, volcano | Flames |
| Boss — Kairuz | `flag:kairuzBestiary` | Kairuz's death, castle | Water Upgrade |
| Boss — Etrom | `flag:etromBestiary` | Etrom's death, castle | Ghost |
| Boss — Elder | `flag:elderBestiary` | the elder's death — the end of the run | — |

### Creature

| Dropdown entry | Trigger | Fires on |
|---|---|---|
| Creature — Scythehead | `flag:scytheheadBestiary` | the first scythehead killed in the castle |
| Creature — Sorcerer | `flag:SorcererBestiary` | the first sorcerer killed in the castle |

Neither is on the route — they are optional castle kills, useful for a 100 %
category. `flag:` falls back to case-insensitive matching, so the capital S in
`SorcererBestiary` is the game's spelling rather than a requirement.

### Item

| Dropdown entry | Trigger | Fires on |
|---|---|---|
| Item — Sword | `flag:sword` | picking the sword up in the lobby, at the top of a run |

### Skill

Reads the ability list, so these fire the moment the ability is actually usable.
Redundant with the Boss rows in a vanilla run — worth using when you want the
split to follow the *ability* rather than the kill.

| Dropdown entry | Trigger | Fires on |
|---|---|---|
| Skill — Charged Attack | `skill:ChargedAttack` | Tenroh's death |
| Skill — Dash | `skill:Dash` | Queen Wasps' death |
| Skill — Double Jump | `skill:DoubleJump` | Galegrim's death |
| Skill — Flames | `skill:flames` | Melog's death |
| Skill — Water Upgrade | `skill:waterUpgrade` | Kairuz's death |
| Skill — Ghost | `skill:Ghost` | Etrom's death |


### Area

| Dropdown entry | Trigger | Fires on |
|---|---|---|
| Area — Enter Lobby | `scene:lobby` | the lobby becoming the active scene |
| Area — Enter Forest | `scene:forest` | the forest becoming the active scene |
| Area — Enter Volcano | `scene:volcano` | the volcano becoming the active scene |
| Area — Enter Castle | `scene:castle` | the castle becoming the active scene |

These fire as you go through the door — unless the Debug tab reports the scene as
`(approx)`, in which case they land at the next checkpoint instead. `scene:` is
an exact match on the game's own spelling: `castle initial` is a separate scene
and does **not** satisfy `scene:castle`.

### Milestone

| Dropdown entry | Trigger | Fires on |
|---|---|---|
| Milestone — 4 Heart Fragments | `int:heartFragments>=4` | **never — see below** |
| Milestone — All Diaries | `flag:allDiariesCollected` | talking to the librarian while holding all 16 |
| Milestone — Merchant unlocked | `flag:merchantOn` | meeting the merchant in the forest |
| Milestone — Teleport unlocked | `flag:teleportationOn` | unlocking fast travel at the first checkpoint that offers it |

Two traps in this group:

- **`heartFragments` is the remainder toward the next heart, so it only ever
  holds 0 or 1** — the shipped preset can never fire. Split on the running total
  instead: `int:heartFragmentsCollected>=4`.
- **`allDiariesCollected` is the turn-in, not the pickup.** It flips when the
  librarian acknowledges the set. To split on collecting the sixteenth diary, use
  `count:diary>=16`.

### End

In route order: the kill, the ending you pick, the credits. The default route ends
on `ending:any`.

| Dropdown entry | Trigger | Fires on |
|---|---|---|
| End — Elder's death | `credits` | the elder's death; latched for the rest of the run |
| End — Any ending | `ending:any` | whichever ending cutscene you chose |
| End — Ending 1 | `ending:1` | the first ending's cutscene |
| End — Ending 2 | `ending:2` | the second ending's cutscene |
| End — Credits roll | `scene:credits` | the credits scene starting, after either ending |
| End — Post-credits (100 % only) | `scene:post credits cutscene` | the scene after the credits |

The elder's death is not the end of a run: you still choose one of two endings and
sit through its cutscene before the credits roll. `credits` latches on the kill,
so a route ending there stops the timer early. It is kept because it is the only
end-of-run signal that does not depend on the scene read. `end` is an alias, and
the latch clears when a run starts.

`ending:any` is exactly `scene:final cutscene 1|scene:final cutscene 2` without the
chance of misspelling either. `ending:` takes `1`, `2` or `any` and rejects
anything else as you type it.

**One ending never enters a cutscene scene.** Of the three ways out after the
elder, one rolls the credits from the lobby without loading `final cutscene 1` or
`2` at all — on that ending an `ending:` split does not fire, and the run never
finishes. `credits` covers all three.

**Which moment each one records.** They are seconds apart, and the gap is the
elder's death animation, the ending transition and the load of the cutscene scene
— the game's clock counts through all of it.

| Last split | Stops the timer at | Load-dependent |
|---|---|---|
| `credits` | the elder's death — the same instant the game writes the time into the save file | no |
| `ending:any` | the ending cutscene finishing loading | yes |
| `scene:credits` | the credits scene finishing loading | yes |

If you want the number on the board to match the one in the save, use `credits`.
Only that one is measured at the same instant, and only that one does not charge
your run for a scene load.

Note that the game's own screens print `00h 18m` — hours and minutes, seconds
truncated — so they cannot confirm anything finer than a minute either way.

**Post-credits fires on almost no run.** It needs ending 2 **and** a save with
everything finished — full map, 16 diaries, 10 heart fragments, 10 artifacts,
every spirit. It is in the list for 100 % routes; on anything else that split
never comes.

**Where `(approx)` bites.** Every row above except `credits` reads the active
scene. The fallback behind `(approx)` is a field written only when the game saves,
and no ending or credits scene ever saves — so those splits do not land late, they
do not fire at all. Use `credits` until the scene read is fixed.

### Not in the dropdown

`newgame` and `never` have no preset because they are the Start and Reset
defaults rather than splits. `always`, `igt:`, `collectible:`, `activation:` and
`count:` are typed by hand — see below.

## The atoms

| Atom | Reads | Example |
|---|---|---|
| `flag:NAME` | a named boolean | `flag:merchantOn` |
| `int:NAME OP N` | a named number | `int:heartFragments>=4` |
| `skill:NAME` | an unlocked ability, by name or ordinal | `skill:Dash` |
| `collectible:ID` | a collected pickup id | `collectible:artifactforest1` |
| `activation:ID` | a world switch id — levers, doors, arenas | `activation:arenaCastle2` |
| `count:PREFIX OP N` | how many collected ids start with PREFIX | `count:diary>=16` |
| `scene:NAME` | the active scene | `scene:volcano` |
| `ending:N` | which ending is playing — `1`, `2` or `any` | `ending:any` |
| `credits` (or `end`) | latches on the elder's death | `credits` |
| `newgame` | one tick, when a run starts | `newgame` |
| `igt:OP TIME` | the clock being sent to LiveSplit | `igt:>=1:30:00` |
| `always` / `never` | constants | `reset = never` |

`&` binds tighter than `|`, `!` negates a single atom, and there are no
parentheses — a condition that needs them wants a different split.

```ini
Melog = flag:melogBestiary & !scene:menu
Any%  = flag:etromBestiary | flag:elderBestiary
```

Comparisons accept `>= > <= < = !=`. A bare `int:souls` means "not zero".

Times are written `12.5`, `1:30` or `1:02:03.4`.

## Finding the names

Names are discovered from the running game, not memorised from a list here:

- the **presets** in the Splits tab cover the bosses, the abilities, the areas
  and the usual milestones — pick one and edit it (they are all listed under
  [The preset list](#the-preset-list));
- the **Debug tab** reports a name the game does not have, so a typo shows up
  as a split that never fires rather than as silence;
- `splitcheck --attach --ids` prints every collectible and activation id the
  current save holds;
- `splitcheck --attach --eval "<expression>"` watches a condition flip while you
  play, which is the fastest way to confirm a split before trusting it in a run.

See [splitcheck.md](splitcheck.md).

## Things worth knowing

- **`collectible:` and `count:` match case-sensitively**, unlike `flag:`, which
  falls back to case-insensitive. If a `count:` prefix reports fewer than you
  expect, check the casing against `--ids`.
- **`count:` matches a prefix, not a category.** Unrelated ids that happen to
  share the first letters are counted too. When that matters, name the ids you
  mean instead.
- **Scene names reach the trigger exactly as the game spells them**, spaces
  included — `scene:castle initial`, `scene:final cutscene 1`.
- **The clock does not stop for the ending.** It only freezes on the main menu, so
  a `scene:credits` split shows a time that includes the ending cutscene you sat
  through. That is why the default route ends on `ending:any` instead; add
  `scene:credits` after it if you want the cutscene as its own segment.
- **`scene:` accuracy is reported in the Debug tab.** `(native)` means the
  split fires as you go through the door. `(approx)` means the component could
  not read the live scene and fell back to a value the game only refreshes when
  it saves, so the split will land at the next checkpoint instead.
- **`collectible:` and `activation:` are latches, not events.** An id appears
  once and stays for the rest of the run.
- **`newgame` is a one-tick pulse.** With `restart_on_new_game` on it restarts a
  run in progress, which is what makes `reset = never` usable: the run then
  survives save / quit / load, and only a genuinely fresh run clears it.

## Recipes

The default route, which ships as the preset list:

```ini
[splits]
Sword       = flag:sword
Tenroh      = flag:tenrohBestiary
Queen Wasps = flag:queensWaspsBestiary
Galegrim    = flag:galegrimBestiary
Melog       = flag:melogBestiary
Kairuz      = flag:kairuzBestiary
Etrom       = flag:etromBestiary
Elder       = flag:elderBestiary
End         = ending:any
```

Collection milestones:

```ini
All diaries  = count:diary>=16
10 fragments = int:heartFragmentsCollected>=10
Merchant     = flag:merchantOn
Teleport     = flag:teleportationOn
```

Area entries, with a guard so a menu frame cannot fire them:

```ini
Enter Forest  = scene:forest & !scene:menu
Enter Volcano = scene:volcano
```
