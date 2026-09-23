# Ultrawide Stash

Makes Escape from Tarkov's stash wider than 10 columns, so it fills the horizontal
space an ultrawide monitor has and a 16:9 one does not.

**Nothing in this repo has ever run in the game.** Everything below was read out of the
game assembly and SPT's database by static analysis. The logic is tested; the result on
screen is not. Version 0.6.0 makes the stash wider and tells you whether the UI can draw
it. It does not yet fix the UI if the answer is no.

**On a 16:9 monitor this mod does nothing until it has measured your screen, and that is
deliberate.** 1080p, 1440p and 4K all get exactly the same canvas width and none of them
have room to spare — see [Why there is room to fill](#why-there-is-room-to-fill). Install
the probe, open your stash once, restart the server, and `"auto"` will use whatever room
actually turns out to be there.

---

## Why there is room to fill

EFT's menu canvas runs in `ConstantPixelSize` mode. `UICanvasScalerController` sets its
scale factor to `min(width / 1920, height / 1080)` — so on any screen 1080p or taller,
**the height alone decides the scale and extra width is simply extra room**.

| Screen | Scale | Canvas, in logical units | Spare width |
| --- | --- | --- | --- |
| 1920x1080 | 1.000 | 1920 x 1080 | none |
| 2560x1440 | 1.333 | 1920 x 1080 | none |
| 3840x2160 (4K) | 2.000 | 1920 x 1080 | none |
| 1920x1200 (16:10) | 1.000 | 1920 x **1200** | none — it gains height |
| 2560x1080 | 1.000 | **2560** x 1080 | 640 px |
| 3440x1440 | 1.333 | **2580** x 1080 | 660 px |
| 5120x1440 | 1.333 | **3840** x 1080 | 1920 px |

A 3440x1440 screen has **660 logical pixels of width that a 16:9 screen does not**. That
is the empty space, and it is genuinely addressable — not letterboxing.

**Resolution is irrelevant; only aspect ratio counts.** A 4K 16:9 monitor has precisely
the same width budget as a 1080p one, and it is zero. This is the single most important
thing to understand before setting `columns`, and it is why the default is `"auto"`.

It also means **the UI cannot be squashed by this mod**. `ConstantPixelSize` means a
cell is always 63 logical pixels; nothing scales to fit. A grid too wide for the panel
does not shrink — it overflows and gets clipped, and the columns past the edge look
exactly like a stash that has eaten your things. Nothing is actually lost (see
[What happens to your items](#what-happens-to-your-items)), but that is the failure the
`"auto"` default exists to prevent.

A stash cell is 63 pixels plus a 1-pixel border, from
`EFT.UI.DragAndDrop.ItemViewFactory.GetCellPixelSize`, which is literally
`columns * 63 + 1`. So:

| Columns | Drawn width | Fits a 1920 canvas? | Fits a 2580 canvas? |
| --- | --- | --- | --- |
| 10 (vanilla) | 631 px | yes | yes |
| 16 | 1009 px | only if the panel has slack | yes |
| 20 | 1261 px | no | yes |
| 40 | 2521 px | **601 px wider than the whole screen** | just |

Up to 0.5.0 the cap was a flat 40, worked out from that 2580-wide canvas. That is one
monitor's ceiling: on the 1920 canvas every 16:9 screen gets, 40 columns is wider than
the entire screen, so the cap protected precisely the people who most needed it. The
ceiling is now worked out per screen — from the probe's measurement where there is one,
and otherwise from a deliberately pessimistic estimate that grants only the canvas width
beyond 16:9 and assumes the panel has no slack of its own.

## Why the width is a server change

Nothing in the client hard-codes 10. `GridView.OnGridResized` sets the grid's
`LayoutElement.minWidth` and `RectTransform.sizeDelta` from whatever dimensions it is
handed. The 10 lives entirely in the stash **item template**, the client is served that
template over `/client/items`, and so changing it on the server changes the grid.

What that does **not** do is guarantee the panel has room to draw the result. Whether
the stash's `ScrollRect` viewport stretches with the canvas or is pinned to a fixed
width is serialized prefab data — unreadable from the assembly. That is what the probe
is for.

---

## What ships

| | Goes to | Does |
| --- | --- | --- |
| `UltrawideStash.Server.dll` | `SPT_Runtime/user/mods/UltrawideStash/` | Sets the stash width, and keeps stored items inside it |
| `UltrawideStash.Probe.dll` | `BepInEx/plugins/` | Widens the stash panel in the menu (never in raid), measures it, and writes it down for the server |
| `repair-stash.ps1` | `SPT_Runtime/user/mods/UltrawideStash/` | Standalone recovery. Needs only PowerShell — not the mod |

The server half also writes two files into its own folder on first start:
`ultrawidestash.config.json` and `HOW-TO-UNINSTALL.txt`. The probe writes a third,
`ultrawidestash.measured.json`, the first time you open your stash.

The two DLLs are independent, but they work best together: the server owns the width and
the probe is the only thing that can see how much room there is to draw it. Without the
probe the server falls back to a deliberately pessimistic estimate, which on a 16:9
screen means it does nothing. The probe is useful on a vanilla 10-wide stash too — it
still reports how much room there is.

## Install

```
scripts\pack.ps1 -SPTPath <your SPT root> -Install
```

Or unzip `releases\UltrawideStash_V<version>.zip` over the SPT root — the archive
carries the full paths, including `SPT_Runtime\user\mods\`.

After a manual install, check the server DLL really landed at
`<SPT>\SPT_Runtime\user\mods\UltrawideStash\UltrawideStash.Server.dll`.
A server mod in the wrong folder is not loaded and **says nothing about it** — the stash
simply stays 10 wide.

> **Back up `SPT_Runtime\user\profiles` first.** The mod takes its own `.bak` before
> it edits anything, and it is built so that removing it cannot strand an item — but it
> does write to your profile, and a backup you took yourself is worth having anyway.

## Configure

`SPT_Runtime/user/mods/UltrawideStash/ultrawidestash.config.json`, written with defaults
on first run:

```json
{
  "columns": "auto",
  "screenWidth": 1920,
  "screenHeight": 1080,
  "ignoreMeasurement": false,
  "compensateRows": true,
  "verbose": false
}
```

**`columns`** — `"auto"`, or a number. Vanilla is 10. Narrowing is refused.

`"auto"` uses whatever your screen can actually show:

1. **The probe's measurement**, if there is one. The probe measures the real stash panel
   on your real screen and writes `ultrawidestash.measured.json` next to this config.
   That is the only source that has seen the truth, so it wins.
2. **An estimate from `screenWidth`/`screenHeight`** otherwise. It grants only the canvas
   width beyond 16:9 and assumes the panel has no slack of its own, so on a 16:9 screen
   it comes to vanilla 10 — the mod changes nothing until it has measured something.

A number is honoured, but still **clamped to what the screen can show**, and the log
says so when it clamps. A grid wider than the panel is not resized to fit; it is clipped,
and the columns past the edge look exactly like lost items.

**`screenWidth` / `screenHeight`** — the screen the *game* runs at, used only to work out
a safe ceiling before anything has been measured. Worth setting on a dedicated server,
where the probe's measurement never reaches the server's machine. Remember that only the
aspect ratio matters: `3840 x 2160` and `1920 x 1080` give the same answer.

**`ignoreMeasurement`** — `true` uses `columns` exactly as written, with no ceiling at
all. For someone who knows their panel better than the probe does. Off by default,
because the clamp is the whole protection against a grid nobody can see.

> **Upgrading from 0.5.0 or earlier?** The old default was `"columns": 16`, which on a
> 16:9 screen is 1009px of grid against a canvas with no room to spare. Your existing
> config is left exactly as it is — nothing is rewritten — but if you are on 16:9 and
> your stash has been clipped, this is why, and `"auto"` is the fix.

**`compensateRows`** — `true` shortens the stash as it widens, so total capacity stays
at vanilla. An Edge of Darkness stash goes from 10x68 (680 cells) to 16x43 (688) — much
less scrolling, no meaningful balance change. `false` keeps every row, so 16 columns
means 16x68 and 60% more space.

It rounds up rather than down, so the stash is never smaller than vanilla. That matters
for sorting — see Compatibility.

Rows are **never** cut below the deepest row you have something standing on. The server
reads every profile at startup, works out the real footprint of each stored item
(including rotation), and clamps. If that means capacity goes up rather than staying
flat, it goes up — losing an item is not an acceptable price for a tidy number.

**`verbose`** — log every stash's before and after rather than one summary line.

### Every row is full width

You will not get 16 slots per row and a short 12-slot row at the bottom. The grid is
always an exact rectangle, for two independent reasons:

- **It cannot be anything else.** A stash template carries exactly two integers,
  `cellsH` and `cellsV`. There is no field that could describe a ragged row, and EFT
  indexes the grid as a flat `List<bool>` of `GridWidth * GridHeight` addressed
  `y * GridWidth + x` (`Grid.FillSpaceBuffer`).
- **The remainder is rounded up into a whole row.** 680 cells at 16 columns is 42.5
  rows, which is where the worry comes from — but that becomes **43 full rows** (688
  cells), not 42 rows and a stub. The 8 extra cells are ordinary cells in an ordinary
  last row.

Two tests pin it: capacity is always an exact multiple of the column count, including
when the occupancy guard forces more rows than the capacity maths asked for.

---

## Compatibility

Checked by reading the code, not by playing. All three were read at 0.2.0.

### EFT's own auto-sort — compatible by construction

`ItemManipulator.Sort` empties every grid, orders the items with `ItemSorter.Sort`,
then calls `Grid.AddAnywhere` on each one with a retry budget of five. `AddAnywhere`
goes to `FindFreeSpace` → `FindFreeSpaceInGrid`, and **every method in that path reads
the grid's own `GridWidth`/`GridHeight`**. Nothing in it hard-codes 10, or any width.

### Advanced Stash Sorting (`com.slpf.advstashsorting`) — compatible

It is a **BepInEx client plugin**, not a server mod, despite how it is listed. It
replaces both the sort order and the placement: `OrderedStashLayoutPlanner` reads
`grid.GridWidth` and `grid.GridHeight` and passes them into `OrderedLayoutEngine`,
where every bound is `request.Width` / `request.Height`. No hard-coded width anywhere.

One thing it asserts is worth knowing: `CopyLayout` throws
`"Grid layout dimensions are inconsistent"` unless `grid.Layout.Count` equals
`GridWidth * GridHeight`. That invariant holds here — the grid is built from the
already-modified template before anything sees it — and the probe now prints it so a
failure would be immediately explicable rather than mysterious.

### UI Fixes (`com.tyfon.uifixes`) — compatible

Its client half has no stash-width assumption. Its **server** half does read the
template — `PutToolsBackAddItemsPatch` calls `grid.Properties.CellsH.Value` and
`CellsV.Value` — but it reads them live, per request, long after this mod has run at
`PostLoad`. It therefore picks up the new width automatically.

The two `AcceptableValueRange<int>(1, 10)` in its settings are mousewheel scroll speed,
not columns.

### The hideout stash upgrade — you do not have to own Edge of Darkness

This is handled, and it is worth knowing how, because it is the one place where the mod
could have lost somebody's items.

A Standard-edition player does not keep the Standard stash. The hideout's **Stash** area
(`5d484fc0654e76006657e0ab`, area type 3) carries a `StashSize` bonus on each stage, and
that bonus holds a **`templateId`, not a row count** — its `value` is `0.0`. Upgrading
the area moves you onto a different stash template entirely:

| Stash level | Template | Vanilla size |
| --- | --- | --- |
| 1 | `566abbc34bdc2d92178b4576` Standard | 10x30 |
| 2 | `5811ce572459770cba1a34ea` Left Behind | 10x40 |
| 3 | `5811ce662459770f6f490f32` Prepare for Escape | 10x50 |
| 4 | `5811ce772459770e9e5f9532` Edge of Darkness | 10x68 |

Those are exactly the templates this mod widens, so upgrading keeps your extra columns.
The mod edits all five regardless of which one you are on, and
`scripts\test-database.ps1` asserts that ladder against the real `areas.json` — if a
future SPT changes it, the check fails rather than the player finding out.

**The bug this caused, fixed in 0.6.0.** Row compensation is clamped up to the deepest
row you have something standing on, and until 0.6.0 that clamp was worked out per
template from the profiles *currently sitting on it*. So:

- A Standard player with items down to row 28 got Standard planned at 16x**28** — the
  clamp beat the compensated 19.
- Nothing was on Left Behind, so its clamp was 0 and it was planned at 16x**25**.
- They upgrade the hideout. The game swaps their template, the stash silently loses
  three rows, and everything on rows 25–27 is out of bounds until the next server start.

Since a profile can only ever move **up** that ladder, a rung must now be at least as
deep as every rung below it. The floor is carried up the ladder as a running maximum, so
no upgrade can ever shorten your stash. It is deliberately not a global maximum across
all profiles — on a shared install, one player's deep Edge of Darkness stash must not
hand another player rows they never earned on a rung they cannot reach.

The Unheard Edition stash (`6602bcf19cc643f44a04274b`, 10x72) is edition-only and is not
on the hideout ladder. It sits at the top because it is the largest, so nothing can
migrate off it onto a shorter rung.

**Additive bonuses still compose.** `InventoryHelper.GetPlayerStashSize` reads
`CellsH`/`CellsV` off the template and then adds any `StashSize` bonus *value* to the row
count. The vanilla hideout contributes `0.0`, but another mod may not, and this mod sets
the base either way — they compose and neither overwrites the other. A bonus row is
worth more when the stash is wider, so a profile carrying one ends up somewhat above
vanilla overall. That is in your favour and not worth engineering around: scaling it
would mean patching `GetPlayerStashSize`, which is exactly where every other stash mod
also lives.

### Why capacity rounds up, not down

Sorting is the reason. Both the vanilla sort and Advanced Stash Sorting fail outright
when the result will not fit — the latter with its own `InsufficientSortSpaceError`.
Rounding rows down would lose up to `columns - 1` cells, so a nearly-full stash that
sorted before this mod could refuse to sort after it.

So `compensateRows` rounds **up**: the grid is never smaller than vanilla. It overshoots
by less than one row — 688 cells against 680 on an Edge of Darkness stash at 16 columns,
about 1%. Two tests hold both ends of that.

### Loot In Vicinity — compatible since the 1.0.0 re-release

Its **Nearby Items** column is the right-hand panel of the in-raid inventory: a fake
stash with a fixed 10x12 grid, shown through the same `SimpleStashPanel.Show` the probe
patches. The first 1.0.0 build widened that panel in raid. The re-release reads the
game's own `inRaid` argument on `SimpleStashPanel.Show` and `ItemsPanel.Show`: in raid it
neither widens nor measures, and it puts a screen widened at the hideout stash back to
vanilla before the raid inventory is drawn. The same fix covers vanilla crates and
bodies, which go through the same panel.

### Stash Management Helper — listed, not audited

The probe names it (`com.markosz.stashmanagementhelper`) if it is loaded, because knowing
it is there makes a report easier to read. **Its code was not reviewed** — only its GUID
was looked up. It is in the census for completeness, not because it has been cleared.

### Other server mods that change stash size

This reads whatever is in the template when it runs and treats that as the baseline, so
"hold capacity" means the capacity it found, not BSG's. If you also run a storage
expansion mod, load order decides which is the baseline, and `verbose: true` prints the
before and after for each stash so you can see what happened.

It refuses to narrow a stash, so it can never undo another mod's widening.

---

## What happens to your items

The short version: **nothing this mod does deletes an item, and the game has its own
recovery for the one bad case.**

### The game's own rescue — it does fire, and this section had it wrong three times

**Observed 2026-09-22: the mod was deleted, the game started, and the out-of-bounds items
were on the Sorting Table.** Everything that follows is the static reading, which
predicted the opposite; it is kept because the mechanism it describes is still the best
account of *how* the rescue can fail, and because the sequence of confident wrong answers
is the point.

`MainMenuShowOperation.MoveBrokenItemsToSortingTable` runs on every main-menu load. It
gathers each grid's `OverlappingItems` and `OutOfBoundsItems`, calls `Grid.FindFreeSpace`
on the **Sorting Table**, and moves what it can there.

The Sorting Table's template declares its grid as `cellsH: 0, cellsV: 0`, and
`GridSerializer.Deserialize` turns those zeroes into **stretch flags** — a zero dimension
means "growable". So the grid is not incapable; it is **unsized until something sizes
it**, and the only thing that does is `SortingTableWindow.ShowGrid` calling
`SortingTable.ClampSize`, i.e. you opening the Sorting Table window.
`FindFreeSpace` → `FindFreeSpaceInGrid` → `GetFreeLocation` is a pure search over the
current dimensions and grows nothing, so at `0 x 0` it returns null and logs
`Cannot find free space on sorting table for a bad item`.

From which this section concluded the rescue could not help on the launch that mattered.
The game disagreed. The likeliest reconciliation is the escape hatch that was noted and
then waved away: **the rescue runs on every return to the menu, not only at launch**, so
once `ShowGrid` has sized the table to 7x7, every later main-menu load has somewhere to
put things — which covers any profile whose owner has ever opened that window. Not
confirmed; confirming it needs the client log and a cold launch on a profile that never
has.

Plan on the clean uninstall anyway. Not because the rescue fails, but because it empties
a wide stash onto a 7-wide table, and the mod can put those items back where they were.

### The mod keeps your items reachable itself

Because of the above, this mod does not depend on the game's rescue. **On every server
start it relocates any item that does not fit the stash it is about to produce**, writing
the new positions into the profile.

- It runs against the grid's **final** dimensions, whether or not this start changed
  them. That matters because the profile can hold items from a previous, wider
  configuration.
- Items that already fit **keep their exact position**. Only the ones that would be
  stranded move.
- It packs biggest-first into the first free space, so the result looks like something
  the game's own sort would have produced.
- If the stash genuinely cannot hold everything, the excess goes to the **Sorting
  Table**, which stretches downward without limit and which this mod never touches. So
  "your stash is too full to go back to vanilla" is not a dead end.
- Only if something fits neither — an item more than 7 cells wide, which is to say never
  — does it **change nothing at all** for that stash and say so in the log. It will never
  apply a size that hides an item.
- Every profile it edits gets a timestamped `.bak` alongside it first, and the original
  is only replaced on the last step, so a failure part-way leaves the file untouched.

This is possible because of an ordering detail: the mod runs at `PostLoad`, and SPT's
`SaveCallbacks` — which loads profiles — sits at the default priority of `int.MaxValue`
and runs later. The files are edited *before* the server reads them, so there is no
second copy to reconcile.

### Installing, and updating

Columns only ever grow on install, and every `(x, y)` valid at 10 wide is still valid
wider, so widening on its own strands nothing.

Rows are the part that could, because `compensateRows` shortens as it widens. Rows are
clamped to the deepest row anything is actually standing on, so the ordinary case needs
no relocation at all — and anything left over is relocated as above. Raising or lowering
`columns` later is re-checked from scratch on every start.

### Uninstalling

**No — you cannot simply delete the mod.** There is one step first, and it takes one
server start.

**Set `columns` to `10`, start the server once, then delete the files.**

Use the number `10`, **not `"auto"`** — auto means "as wide as this screen allows",
which is the opposite of what an uninstall needs.

That single start packs everything back into a vanilla-shaped stash — the log will say
how many items it relocated — and from then on the mod is doing nothing, so removing it
changes nothing. The recommendation is in the startup log too, so it is hard to miss.

The one thing that start cannot do for you is happen after the fact. If you remove the
DLLs without it, items in column 10 and beyond become unreachable — **not deleted**, the
server never prunes, so they are still in the profile with their coordinates.

Two ways back, and neither loses anything:

- **Reinstall** at the same `columns`, start the server, and everything is where you left
  it. Then uninstall properly.
- **Or run `scripts/repair-stash.ps1`**, which needs nothing but PowerShell — not the
  mod, not a matching SPT version, not a build. It packs each stash back into vanilla
  dimensions, overflows anything that will not fit into the Sorting Table, and reports
  without writing unless you pass `-Apply`:

  ```
  scripts\repair-stash.ps1 -SPTPath C:\YourSPT            # report only
  scripts\repair-stash.ps1 -SPTPath C:\YourSPT -Apply     # do it
  ```

  It takes a timestamped `.bak` beside every profile it touches. This is what makes the
  hazard a nuisance rather than a trap: **the recovery outlives the mod.**

---

## The measurement procedure

The probe answers the one question that decides whether a client-side UI fix is needed,
and since 0.6.0 it also hands its answer straight to the server.

1. Install both halves. Leave `columns` at `"auto"` for the first run.
2. Launch, open your stash, and let it sit for a second. The probe writes
   `ultrawidestash.measured.json` into the server mod's folder.
3. **Restart the SPT server.** `"auto"` now uses the measured number.
4. For the detail, find `BepInEx\LogOutput.log` and grep for `[UltrawideStash]`.

A measurement only takes effect on the **next** server start — the grid you are looking
at was built from the template the server already served. Nothing tries to resize a
stash that is on screen.

If the two halves are on different machines, or you are not running the probe, there is
no measurement and `"auto"` falls back to the estimate from `screenWidth`/`screenHeight`.
Set those, or set `columns` to a number.

You get one block per screen resolution per session, like:

```
[UltrawideStash] ===== stash measurement =====
[UltrawideStash] screen 3440x1440; canvas scale 1.333; canvas logical 2580x1080
[UltrawideStash] stash grid 16x43 cells; rect 1009.0x2710.0 px (a 16-wide grid draws at 1009 px)
[UltrawideStash] out-of-bounds items: none
[UltrawideStash] grid layout: 688 cells, consistent with 16x43
[UltrawideStash] companion plugins: UI Fixes 3.2.0, Advanced Stash Sorting 1.0.6 (14 plugins loaded in total)
[UltrawideStash] ancestors, grid outward -- name | rect | anchors | components:
[UltrawideStash]   [0] Grid | 1009.0x2710.0 | ax 0.00-0.00 fixed | GridView,LayoutElement
[UltrawideStash]   [1] Content | ... | ax 0.00-1.00 STRETCH | VerticalLayoutGroup,ContentSizeFitter
[UltrawideStash]   [2] Viewport | ... | ax 0.00-0.00 fixed | RectMask2D,Image
[UltrawideStash]   ...
[UltrawideStash] canvas width 2580.0 px; widest ancestor that stretches with it: 1920.0 px
[UltrawideStash] columns that would fit the widest stretching ancestor: 30 (you have 16)
[UltrawideStash] measurement written to ...\UltrawideStash\ultrawidestash.measured.json -- restart the SPT server and "columns": "auto" will use it.
[UltrawideStash] =============================
```

What to read from it:

- **`out-of-bounds items`** — anything but `none` means the stash is holding items you
  cannot reach. Stop and restore a profile backup.
- **The `STRETCH` / `fixed` column** — this is the answer. A `fixed` ancestor between
  the grid and the canvas is what clips a widened grid, and its name and components say
  exactly what a fix has to change.
- **`columns that would fit`** — the ceiling for this monitor, and what gets written to
  `ultrawidestash.measured.json`. You no longer have to copy it by hand.
- **`measurement written to`** — where it went. If it says it could not find the server
  mod folder, the server half is not installed where the loader looks, and `"auto"` will
  keep using its estimate.
- **`grid layout`** — must say `consistent`. If it does not, sorting mods will refuse to
  sort, and this line is why.
- **`companion plugins`** — which stash-touching mods were loaded, and at what version,
  so a report describes itself.

## Building

```
scripts\pack.ps1                 # build, test, zip
scripts\pack.ps1 -Install        # and install
scripts\test-database.ps1        # stash ids against a real database
scripts\repair-stash.ps1         # standalone stash repair (report only)
dotnet test tests\UltrawideStash.Server.Tests
```

Each finds the SPT install by itself: `$env:SPT_PATH` if set, otherwise the nearest
install at or above the script or the current directory. Pass `-SPTPath <path>` to
override. Run them through PowerShell, not Bash — a backslash path gets mangled
otherwise.

The probe references **no game assembly**. The `Assembly-CSharp.dll` in `Managed` is not
the one the game runs — the SPT Launcher applies a delta at startup that renames
obfuscated types — so every game member is resolved by its patched name at runtime
through `AccessTools`, in `GameTypes.cs`. The plugin therefore builds against any
install, launched or not, and `pack.ps1` asserts the DLL carries no `Assembly-CSharp` or
`spt-*` reference.

## Status

Built against SPT 4.1.5 / EFT 0.16.9.5.40743 / BepInEx 5.4.23.5. Clean at 0 warnings;
168 logic tests and 19 database checks pass. The probe carries no `Assembly-CSharp` or
`spt-*` reference and `pack.ps1` asserts it.

Compatibility with auto-sort, Advanced Stash Sorting and UI Fixes was established by
reading their code — see Compatibility — not by running them. None of the three is
installed on the development machine.

**Nothing here has run in the game.** Untested, in rough order of risk:

- **Writing to profiles.** From 0.4.0 the mod edits your profile JSON to keep items
  reachable. It backs up first, writes to a temp file, replaces last, and refuses rather
  than half-finishing — but it has never written a real profile. On a first widen the log
  should say `0 item(s) relocated`, because widening alone cannot strand anything.
  Anything else on a plain widen is a bug worth reporting.
- **Whether the widened grid is drawn or clipped.** The whole reason the probe exists.
- **The measurement handshake (0.6.0).** The probe writing
  `ultrawidestash.measured.json` into the server mod's folder, and the server reading it
  back, has never run. Both halves fail safe if it does not work — the probe logs why it
  could not write, and `"auto"` falls back to the estimate — but the happy path is
  unproven. Check the file appears after you open your stash, and that the next server
  start logs `auto: N columns, from the probe's measurement`.
- **Whether the estimate is pessimistic in the right direction.** It assumes the stash
  panel has no slack beyond 16:9. If the panel is in fact *narrower* than the canvas,
  an unmeasured ultrawide could still be given more columns than fit. That is what the
  measurement exists to correct, and it is why the probe matters more than the estimate.
- **The Sorting Table overflow.** Re-parenting an item into the Sorting Table is written
  from the JSON shape rather than from watching the game do it. It only fires when the
  stash cannot take everything back.
- **Whether the probe's Harmony patch fires at all.** `SimpleStashPanel.Show` is patched
  with `MonoBehaviour __instance`, which is a genuine supertype, but that has not run.
- **Whether the tallest GridView is really the stash.** It is by a wide margin on paper
  — 30 rows minimum against a backpack's handful — but an open container has its own.
- **A real played profile.** Every profile test runs against synthesised JSON; the only
  profile on the development machine is an unplayed stub.
- **`repair-stash.ps1` on a real profile.** Verified against synthesised data only, and
  it treats every item as 1×1 because it has no item database to size them from.
- **The probe's diagnostics.** The companion census reads BepInEx's
  `Chainloader.PluginInfos` and the layout line reads `Grid.Layout` — neither has run.

## Repository

https://github.com/JoelHauser/UltrawideStash

## Licence

MIT.
