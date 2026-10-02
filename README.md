# Ultrawide Stash

Makes Escape from Tarkov's stash wider than 10 columns, so it fills the horizontal
space an ultrawide monitor has and a 16:9 one does not.

**It runs in game at 21:9, from 1.0.5 on any install.** Up to 1.0.4 it only widened
the stash where **UIScale.Reloaded** was also installed, as it was on the development
machine. That mod stretches EFT's inventory screen, which vanilla keeps as a fixed 16:9
frame in the middle of the monitor. 1.0.5 does that stretch itself. It has been played
on SPT 4.1.6 at 3440x1440, with UIScale.Reloaded and without it: a 19x36 stash in a
widened panel, with no scrollbar and no dead space. **32:9 (5120x1440) has been run on a
physical monitor** by players: on 1.0.5 the inventory stretched to the full width and,
with `GearPanelReserve` at its default, the stash filled it; on 1.1.1 the trader screen
showed all 39 columns too, confirmed from the player's log. See [Status](#status) for what has and has not been
checked.

**From 1.1.0 the stash is wide at the traders and on the flea market too** — on the
trader screen, and in the add-offer window you list items from. Up to 1.0.5 both kept
the 10-column panel, so a wide stash scrolled sideways at a trader and was cut off when
listing an item. See [Traders and the flea market](#traders-and-the-flea-market-110).

**On a 16:9 monitor this mod does nothing, and that is deliberate.** 1080p, 1440p and 4K
all get exactly the same canvas width and none of them have room to spare — see
[Why there is room to fill](#why-there-is-room-to-fill). On a wider screen it works out
of the box: the server reads the resolution EFT last ran at and sizes the stash on its
first start, and the probe's measurement takes over from then on.

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
| `UltrawideStash.Probe.dll` | `BepInEx/plugins/` | Stretches EFT's 16:9 inventory screen to the full width, then widens the stash panel in the menu (never in raid) — on the character screen, the scav loot transfer, receiving mail items, hideout area transfers, the trader screen and the flea market's add-offer window — measures the character screen's, and writes it down for the server |
| `repair-stash.ps1` | `SPT_Runtime/user/mods/UltrawideStash/` | Standalone recovery. Needs only PowerShell — not the mod |

The server half also writes two files into its own folder on first start:
`ultrawidestash.config.json` and `HOW-TO-UNINSTALL.txt`. The probe writes a third,
`ultrawidestash.measured.json`, the first time you open your stash.

The two DLLs belong together: the server owns the width, and the probe is what makes
room for it on screen and measures how much room there is. Without the probe the server
still sizes the grid from the resolution EFT saved, but nothing widens the panel, so a
wider grid scrolls sideways. The probe is useful on a vanilla 10-wide stash too — it
still reports how much room there is, and why not when there is none.

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

> **Back up `SPT_Runtime\user\profiles` first.** The mod takes its own backup before
> it edits anything (kept in `%LOCALAPPDATA%\UltrawideStash\backups`, not in your SPT
> folder), and it is built so that removing it cannot strand an item — but it does write
> to your profile, and a backup you took yourself is worth having anyway.

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

1. **The probe's measurement**, if there is one for the screen you are on. The probe
   measures the real stash panel on your real screen and writes
   `ultrawidestash.measured.json` next to this config: the columns the widened panel
   can show. That is the only source that has seen the truth, so it wins. A measurement
   taken at a different resolution is ignored.
2. **A prediction from your screen size** otherwise — read from the resolution EFT saved
   in the registry (Windows), or from `screenWidth`/`screenHeight` when that cannot be
   read. It works out what the probe will do to the panel: 19 columns at 3440x1440, 39 at
   5120x1440, 10 on any 16:9 screen.

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

### Client settings

`BepInEx/config/com.mybutthasarash.ultrawidestash.cfg`, section `[Layout]`:

- **`WidenStashPanel`** (`true`) — narrow the gear side of the character screen and
  give the width to the stash panel. Since 1.0.4 the panel is widened only as far as
  the grid needs: if the grid is narrower than the room there is (after a resolution
  change, before the server restarts, or with `columns` set lower), the rest stays with
  the gear side instead of sitting as empty panel beside the grid.
- **`GearPanelReserve`** (`620`) — canvas px each gear panel keeps when it does. **Leave
  it at 620** unless you want a narrower stash. Every px you add comes off the stash
  panel, twice over: at 1000 a 5120x1440 screen gets 27 columns instead of 39. The
  server reads this setting from the same file when it sizes the grid, and its log warns
  when it isn't 620. Below 520 it is raised to 520, or the character doll clips.
- **`WidenTransferScreens`** (`true`, new in 1.0.3) — also widen the stash panel on the
  scav loot transfer after a raid, on the screen for receiving mail items, and on the
  hideout screen for putting items into an area. From 1.1.0 it covers the trader screen
  and the flea market's add-offer window as well.

### The other screens that show your stash (1.0.3)

The grid is one item, so it is the same width on every screen that draws it. Up to
1.0.2 only the character screen's panel was widened, and everywhere else a 19-wide grid
sat in the vanilla 680 px panel behind a horizontal scrollbar. The scav loot transfer
was skipped outright: the game shows it with its in-raid flag set, although it only ever
appears back in the menu.

These screens are not laid out like the character screen, and where their buttons sit
is prefab data the game's code does not carry. So the probe measures the screen as it is
drawn and grows the panel only into space nothing occupies: right first, into the empty
canvas beside the screen's 16:9 frame, then left. A panel standing in the way on the
left can slide into its own empty margin. **Buttons are never moved and never
covered** — one that reaches into the bottom of the panel's span (Next on the scav
screen, Receive All on the mail screen) is cleared by bringing the panel's bottom edge
up, by at most a row and a half, and one beside the panel stops it growing. The hideout
screen is re-planned whenever a tab change swaps the area's grid or the filter window
opens.

Prestige and the in-raid transit transfer are left vanilla. Nothing measured on any
screen but the character screen is written for the server — before 1.0.3, the first
stash panel opened at a resolution was measured whichever screen it was on.

### Traders and the flea market (1.1.0)

**The trader screen** is laid out the other way round from the transfer screens: it
already fills the monitor, with the trader's goods on the left, the deal column (Buy /
Sell, Deal, the price panel) in the middle and your stash at the right edge. There is no
room on the right, so the stash grows **left**, and the deal column slides left into the
empty space beside the trader's goods. At 3440x1440 the stash goes from 684 to 1212 px
(19 columns) and the deal column moves 245 px. The stash's filter strip, which hangs
just outside the panel's left edge, is kept clear too.

It is laid out when the trader screen opens, before its first frame, so nothing visibly
moves — not on a trader's first load, not when clicking between traders, not on Buy /
Sell. When you come back to a trader from another screen, the game puts the stash panel
back to its own width; the probe notices that before the frame is drawn and lays it out
again.

**The flea market's add-offer window** draws your stash in a grid of its own and cut a
wide one off. It is a centred window built by the game's layout system, so the probe
asks that layout for more room instead of moving things by hand: the stash side gets as
much more width as the grid needs and the window grows by the same amount, about its
own centre (1200 → 1770 px at 3440x1440). The price side keeps its size. The game's own
keep-on-screen check then runs at the new width, so a window you dragged near an edge
stays on screen. If a screen is too narrow for the whole grid, the window widens as far
as it can and the rest scrolls.

Both are menu screens and are never widened in raid. `WidenTransferScreens` turns both
off with the others.

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

### UIScale.Reloaded — compatible, and the reason 1.0.4 and earlier ever worked

In vanilla EFT the inventory screen is a fixed 1920 px frame centred on the monitor, at
every aspect ratio. UIScale.Reloaded's inventory stretch widens it to the whole screen,
putting `LeftSide` 12 px from the left and 702 px from the right, with the stash panel
680 px wide and 12 px from the right. Every version before 1.0.5 was developed and
tested with that mod installed, so it only worked alongside it. On every other install
there was no room, and the stash stayed at 10 columns.

1.0.5 makes the same stretch itself, to the same edges, when it finds the vanilla frame,
and puts it back in raid. With UIScale.Reloaded installed there is nothing to stretch,
and it widens UIScale's layout as before. UIScale applies its stretch a few frames after
the screen opens. If that lands after the widening, the probe notices, logs it and
widens again.

### Stash Management Helper — listed, not audited

The probe names it (`com.markosz.stashmanagementhelper`) if it is loaded, because knowing
it is there makes a report easier to read. **Its code was not reviewed** — only its GUID
was looked up. It is in the census for completeness, not because it has been cleared.

### Other server mods that change stash size

This reads whatever is in the template when it runs and treats that as the baseline, so
"hold capacity" means the capacity it found, not BSG's. If you also run a storage
expansion mod, load order decides which is the baseline, and `verbose: true` prints the
before and after for each stash so you can see what happened.

**SVM (Server Value Modifier)** — its *Hideout → Stash* setting sets the row count of
all five stashes, and it runs early in the server's load (`OnLoadOrder.Preload + 5`),
well before this mod (`PostLoad`). So SVM's rows are the baseline: an Edge of Darkness
stash SVM has made 10×100 becomes 19×53 on a 3440×1440 screen, the same 1,000 cells,
just wider and shorter. Set `compensateRows` to `false` to keep all of SVM's rows *and*
the extra width. Either way, a stash that is still taller than the panel scrolls up and
down, as it does without this mod.

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
- The grid it packs into is the one SPT and the game actually use: the template's rows
  **plus the profile's own `StashRows` bonus**, as SPT's `GetPlayerStashSize` adds it.
  Before 1.0.2 the bonus was ignored, and anything kept in those last rows was moved
  back up on every server start — pinned, locked or not.
- Every profile it edits is backed up first, and the original is only replaced on the
  last step, so a failure part-way leaves the file untouched. Backups live outside SPT, in
  `%LOCALAPPDATA%\UltrawideStash\backups\<install>` (the server log and
  `HOW-TO-UNINSTALL.txt` give the exact folder). Each profile keeps
  `<profile>.json.ultrawidestash-original.bak` -- the profile before the mod first changed
  it, never overwritten -- plus the two most recent timestamped `.bak` files. Backups older
  versions left in `user/profiles` — including those of profiles since deleted — are
  moved there and trimmed on the next server start. The profiles themselves are never
  touched by this.

SPT has already loaded every profile by the time the mod runs (`SaveCallbacks` sits at
`OnLoadOrder.SaveCallbacks`, before `PostLoad`), so a loaded profile is moved in memory
and saved through SPT's own `SaveServer` — the file is edited directly only for a
profile SPT did not load. Before 1.0.1 it edited only the file, and SPT's next save
wrote the unmoved copy back over it.

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

**Set `columns` to `10`, start the server once, then delete the files.** The mod's
profile backups are outside SPT, in `%LOCALAPPDATA%\UltrawideStash\backups` — delete
that folder too once you are happy with your stash.

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

The probe widens the panel, measures the result, and hands its answer straight to the
server. You do not have to do anything for this — it happens every time you open the
character screen — but this is how to read it when something looks wrong.

1. Install both halves. Leave `columns` at `"auto"`.
2. Launch, open the character screen, and let it sit for a second. The probe writes
   `ultrawidestash.measured.json` into the server mod's folder.
3. On the next server start `"auto"` uses the measured number. On a first install the
   server has usually predicted the same number already, so nothing changes.
4. For the detail, find `BepInEx\LogOutput.log` and grep for `[UltrawideStash]`.

A measurement only takes effect on the **next** server start — the grid you are looking
at was built from the template the server already served. Nothing tries to resize a
stash that is on screen.

If the two halves are on different machines, or you are not running the probe, there is
no measurement and `"auto"` falls back to the estimate from `screenWidth`/`screenHeight`.
Set those, or set `columns` to a number.

You get one block per screen resolution per session, like:

```
[UltrawideStash] widened: 'LeftSide' 1866.0 -> 1272.0 px, stash panel 680.0 -> 1250.0 px, gap between them 34.0 px.
[UltrawideStash] the panel shows 19 columns.
[UltrawideStash] ===== stash measurement =====
[UltrawideStash] screen 3440x1440; canvas scale 1.333; canvas logical 2580x1080
[UltrawideStash] stash grid 19x36 cells; rect 1198.0x2269.0 px (a 19-wide grid draws at 1198 px)
[UltrawideStash] out-of-bounds items: none
[UltrawideStash] grid layout: 684 cells, consistent with 19x36
[UltrawideStash] companion plugins: UI Fixes 6.0.3, Advanced Stash Sorting 1.0.5 (106 plugins loaded in total)
[UltrawideStash] ancestors, grid outward -- name | rect | anchors | components:
[UltrawideStash]   [0] GridView(Clone) | 1198.0x2269.0 | ax 0.00-0.00 fixed | GridView,LayoutElement
[UltrawideStash]   ...
[UltrawideStash]   [6] Stash Panel | 1250.0x873.0 | ax 1.00-1.00 fixed | -
[UltrawideStash]   [7] Items Panel | 2580.0x1080.0 | ax 0.00-1.00 STRETCH | ItemsPanel,DrawMultiSelect
[UltrawideStash]   ...
[UltrawideStash] columns that fit inside the clipping ancestor: 19 (you have 19); panel 1250.0 px less 48.0 px of chrome
[UltrawideStash] widening: room for 19 columns; the panel shows 19, the grid is 19
[UltrawideStash] panel map -- 'Items Panel' and below, left to right, in canvas px:
[UltrawideStash]   ...
[UltrawideStash] CHECK: viewport 1202.0 px vs grid 1198.0 px -- fits, 4.0 px spare
[UltrawideStash] measurement written to ...\UltrawideStash\ultrawidestash.measured.json -- restart the SPT server and "columns": "auto" will use it.
[UltrawideStash] =============================
```

What to read from it:

- **`CHECK`** — read this first. `fits` is right. `OVERFLOW` means a horizontal
  scrollbar (the grid is wider than the panel), and `DEAD SPACE` means empty columns
  beside the grid.
- **`out-of-bounds items`** — anything but `none` means the stash is holding items you
  cannot reach. Stop and restore a profile backup.
- **`widened:` / `nothing to widen:` / `cannot widen:`** — the line above the block. It
  says what the probe did to the screen, or why it did nothing.
- **The `STRETCH` / `fixed` column** — `Stash Panel` is the `fixed` ancestor that clips
  the grid, and it is what the probe widens.
- **`measurement written to`** — where it went. If it says it could not find the server
  mod folder, the server half is not installed where the loader looks, and `"auto"` will
  keep using its estimate.
- **`grid layout`** — must say `consistent`. If it does not, sorting mods will refuse to
  sort, and this line is why.
- **`companion plugins`** — which stash-touching mods were loaded, and at what version,
  so a report describes itself.
- **`widening:`** — either the room there is (`room for 19 columns; the panel shows 19,
  the grid is 19`) or why there is none (`not possible -- ...`). The room is what gets
  written to `ultrawidestash.measured.json` (1.0.4). Before 1.0.4 the file held the
  panel's width as it stood. The panel is widened only as far as the grid needs, and
  any room left over is what `"auto"` grows into at the next server start.

### My stash stays at 10 columns

Look in the **server** log, just after the `Width:` line. When the game could not widen
its stash panel, it says why, for example:

```
[UltrawideStash] The game could not widen its stash panel when it last measured it
(2560x1440, where EFT's menus are 1920 px wide): the gear side of the inventory screen
is 1206 px wide and keeps 1240 px for itself, which leaves no room for another column.
The stash stays 10 columns wide. That is a 16:9 (or narrower) layout, ...
```

The usual cause is EFT itself running at a 16:9 resolution on a wider monitor. EFT's
menus are 1920 px wide at *every* 16:9 resolution, so there is nothing to widen into.
Set EFT's resolution in *Settings → Graphics* to your monitor's own, open your stash
once, and restart the server. A 32:9 monitor (5120×1440) gets a 3840 px wide menu and
a 39-column stash. That was simulated on a 3440×1440 display for 1.0.4, and players
have since run 1.0.5 and 1.1.1 on physical 32:9 monitors.

The other cause is a changed **`GearPanelReserve`** in the BepInEx config. A higher
value leaves the stash less room: at 1000 a 5120×1440 screen gets 27 columns instead of
39. From 1.0.5 the server reads that setting when it predicts the width, and warns when
it isn't 620.

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

Built against SPT 4.1.5 / EFT 0.16.9.5.40743 / BepInEx 5.4.23.5, and played on SPT 4.1.6.
Clean at 0 warnings; 277 logic tests and 19 database checks pass. The probe carries no
`Assembly-CSharp` or `spt-*` reference and `pack.ps1` asserts it.

Compatibility with auto-sort, Advanced Stash Sorting and UI Fixes was established by
reading their code — see Compatibility. UI Fixes and Advanced Stash Sorting are loaded
alongside it on the test install, but their sorting has not been exercised against a
wide stash specifically.

**Verified in game**, on SPT 4.1.6 at 3440x1440. Everything up to 1.0.4 ran on an
install with UIScale.Reloaded, which is the only reason it worked there. See
Compatibility.

- **EFT's own inventory screen, stretched** (1.0.5) — with UIScale.Reloaded switched off,
  the probe found the vanilla 1920 px frame in a 2580 px canvas, stretched it, and
  widened the stash to 19 columns (`CHECK ... fits, 4.0 px spare`). The screen went back
  to the vanilla frame in raid. With UIScale.Reloaded on again it did not stretch, and
  widened UIScale's layout the same way.
- **The widened stash** — 19x36, drawn in full, no horizontal scrollbar, no dead space
  (`CHECK ... fits, 4.0 px spare`).
- **The measurement handshake** — the probe writes its file, and the next server start
  logs `auto: 19 columns, from the probe's measurement`.
- **Writing to profiles** — relocating out-of-bounds items in a loaded profile and saving
  it through SPT (1.0.1), and a profile's `StashRows` bonus rows left alone (1.0.2).
- **Never in raid** — a screen widened in the menu is put back to vanilla on the first
  inventory open in raid (1.0.0).
- **The mail transfer screen** — 19 columns, buttons clear (1.0.3).
- **The trader screen** (1.1.0) — 19 columns (`CHECK ... fits, 4.0 px spare`), the deal
  column slid 245 px, steady on first loads, trader clicks, Buy / Sell and coming back
  from the character screen.
- **The flea market's add-offer window** (1.1.0) — 1200 → 1770 px, 19 columns, on screen
  when reopened and after being dragged to an edge.
- **16:9 and 32:9 layouts, simulated** (1.0.4, with UIScale.Reloaded) — by setting
  EFT's UI scale so a 3440x1440 display lays the menu out as a 1920- or 3840-wide
  canvas. At 1920 the panel stays vanilla and the log says why. At 3840 a 39x68 stash
  filled a 2510 px panel.
- **The server's "could not widen" warning** (1.0.4) — seen in a player's server log.
  It gave the wrong advice there: it read the inventory screen's own 1920 px frame as the
  menu width, so it called a 3440x1440 screen 16:9. 1.0.5 reads the real canvas.
- **A physical 32:9 monitor** (1.0.5, reported by a player) — at 5120x1440 the inventory
  stretched to the full width. With `GearPanelReserve` at 1000 the panel held 27 columns
  and a 39-wide grid scrolled sideways. Set back to 620, the stash filled the screen.
  From screenshots and the player's word; their log was not seen.
- **The trader screen at 32:9** (1.1.1, a player's log, Forge issue #3). At 5120x1440
  EFT draws the trader screen as a 1920 px block in the middle of the monitor, not full
  width as at 21:9, so in 1.1.0 the stash got 11 of 39 columns and scrolled sideways.
  With 1.1.1 the log shows `stretched: the trader screen was a 1920 px frame in a 3840 px
  canvas`, the stash panel 642 → 2472 px with all 39 columns (`CHECK ... fits, 4.0 px
  spare`), the deal column slid 874 px, and every re-check fitting over trader clicks,
  Buy / Sell and returns from the character screen, which showed 39 columns too. The
  add-offer window fitted 39 columns in the same player's 1.1.0 log. Seen alongside
  Kaeno's Trader Scrolling.

**Not yet verified**, in rough order of risk:

- **A physical 21:9 monitor other than 3440x1440** (2560x1080, 3840x1600).
- **The server reading `GearPanelReserve`** (1.0.5). Unit-tested; its warning has not
  been seen in a live log.
- **The scav loot transfer and hideout area transfer screens** (1.0.3). They use the same
  code as the mail screen, but have not been played through.
- **A clean install with 1.0.5.** The stretch was verified with UIScale.Reloaded
  switched off on the development install, which still runs about a hundred other
  plugins, not on an install with nothing else loaded.
- **The server ignoring a pre-1.0.5 measurement** (1.0.5). Unit-tested; its log line has
  not been seen yet.
- **The Sorting Table overflow.** Re-parenting an item into the Sorting Table is written
  from the JSON shape rather than from watching the game do it. It only fires when the
  stash cannot take everything back.
- **`repair-stash.ps1` on a real profile.** Verified against synthesised data only, and
  it treats every item as 1×1 because it has no item database to size them from.

## Repository

https://github.com/JoelHauser/UltrawideStash

## Licence

MIT.
