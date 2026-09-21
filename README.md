# Ultrawide Stash

Makes Escape from Tarkov's stash wider than 10 columns, so it fills the horizontal
space an ultrawide monitor has and a 16:9 one does not.

**Nothing in this repo has ever run in the game.** Everything below was read out of the
game assembly and SPT's database by static analysis. The logic is tested; the result on
screen is not. Version 0.2.0 is a first cut plus a measuring tool, not a finished mod:
it makes the stash wider and tells you whether the UI can draw it. It does not yet fix
the UI if the answer is no.

---

## Why there is room to fill

EFT's menu canvas runs in `ConstantPixelSize` mode. `UICanvasScalerController` sets its
scale factor to `min(width / 1920, height / 1080)` â€” so on any screen 1080p or taller,
**the height alone decides the scale and extra width is simply extra room**.

| Screen | Scale | Canvas, in logical units |
| --- | --- | --- |
| 1920x1080 | 1.000 | 1920 x 1080 |
| 2560x1440 | 1.333 | 1920 x 1080 |
| 3440x1440 | 1.333 | **2580** x 1080 |
| 5120x1440 | 1.333 | **3840** x 1080 |

A 3440x1440 screen has **660 logical pixels of width that a 16:9 screen does not**. That
is the empty space, and it is genuinely addressable â€” not letterboxing.

A stash cell is 63 pixels plus a 1-pixel border, from
`EFT.UI.DragAndDrop.ItemViewFactory.GetCellPixelSize`, which is literally
`columns * 63 + 1`. So:

| Columns | Drawn width |
| --- | --- |
| 10 (vanilla) | 631 px |
| 16 (default here) | 1009 px |
| 20 | 1261 px |
| 40 (the cap) | 2521 px |

40 is the cap because 40 columns is 2521px and fits a 3440x1440 canvas; 41 is 2584px and
does not.

## Why the width is a server change

Nothing in the client hard-codes 10. `GridView.OnGridResized` sets the grid's
`LayoutElement.minWidth` and `RectTransform.sizeDelta` from whatever dimensions it is
handed. The 10 lives entirely in the stash **item template**, the client is served that
template over `/client/items`, and so changing it on the server changes the grid.

What that does **not** do is guarantee the panel has room to draw the result. Whether
the stash's `ScrollRect` viewport stretches with the canvas or is pinned to a fixed
width is serialized prefab data â€” unreadable from the assembly. That is what the probe
is for.

---

## The two halves

| | Installs to | Does |
| --- | --- | --- |
| `UltrawideStash.Server.dll` | `SPT_Runtime/user/mods/UltrawideStash/` | Sets the stash width |
| `UltrawideStash.Probe.dll` | `BepInEx/plugins/` | Measures the stash panel and logs it. Changes nothing |

They are independent. The probe is useful on a vanilla 10-wide stash too â€” it still
reports how much room there is.

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

> **Back up `SPT_Runtime\user\profiles` first.** Items you place past column 10 are
> outside the grid if you ever remove this mod. That is inherent to changing a grid's
> size, not a defect â€” but it is your stash.

## Configure

`SPT_Runtime/user/mods/UltrawideStash/ultrawidestash.config.json`, written with defaults
on first run:

```json
{
  "columns": 16,
  "compensateRows": true,
  "verbose": false
}
```

**`columns`** â€” how many cells across. Vanilla is 10; the cap is 40. Narrowing is
refused.

**`compensateRows`** â€” `true` shortens the stash as it widens, so total capacity stays
at vanilla. An Edge of Darkness stash goes from 10x68 (680 cells) to 16x43 (688) â€” much
less scrolling, no meaningful balance change. `false` keeps every row, so 16 columns
means 16x68 and 60% more space.

It rounds up rather than down, so the stash is never smaller than vanilla. That matters
for sorting â€” see Compatibility.

Rows are **never** cut below the deepest row you have something standing on. The server
reads every profile at startup, works out the real footprint of each stored item
(including rotation), and clamps. If that means capacity goes up rather than staying
flat, it goes up â€” losing an item is not an acceptable price for a tidy number.

**`verbose`** â€” log every stash's before and after rather than one summary line.

### Every row is full width

You will not get 16 slots per row and a short 12-slot row at the bottom. The grid is
always an exact rectangle, for two independent reasons:

- **It cannot be anything else.** A stash template carries exactly two integers,
  `cellsH` and `cellsV`. There is no field that could describe a ragged row, and EFT
  indexes the grid as a flat `List<bool>` of `GridWidth * GridHeight` addressed
  `y * GridWidth + x` (`Grid.FillSpaceBuffer`).
- **The remainder is rounded up into a whole row.** 680 cells at 16 columns is 42.5
  rows, which is where the worry comes from â€” but that becomes **43 full rows** (688
  cells), not 42 rows and a stub. The 8 extra cells are ordinary cells in an ordinary
  last row.

Two tests pin it: capacity is always an exact multiple of the column count, including
when the occupancy guard forces more rows than the capacity maths asked for.

---

## Compatibility

Checked by reading the code, not by playing. All three were read at 0.2.0.

### EFT's own auto-sort â€” compatible by construction

`ItemManipulator.Sort` empties every grid, orders the items with `ItemSorter.Sort`,
then calls `Grid.AddAnywhere` on each one with a retry budget of five. `AddAnywhere`
goes to `FindFreeSpace` â†’ `FindFreeSpaceInGrid`, and **every method in that path reads
the grid's own `GridWidth`/`GridHeight`**. Nothing in it hard-codes 10, or any width.

### Advanced Stash Sorting (`com.slpf.advstashsorting`) â€” compatible

It is a **BepInEx client plugin**, not a server mod, despite how it is listed. It
replaces both the sort order and the placement: `OrderedStashLayoutPlanner` reads
`grid.GridWidth` and `grid.GridHeight` and passes them into `OrderedLayoutEngine`,
where every bound is `request.Width` / `request.Height`. No hard-coded width anywhere.

One thing it asserts is worth knowing: `CopyLayout` throws
`"Grid layout dimensions are inconsistent"` unless `grid.Layout.Count` equals
`GridWidth * GridHeight`. That invariant holds here â€” the grid is built from the
already-modified template before anything sees it â€” and the probe now prints it so a
failure would be immediately explicable rather than mysterious.

### UI Fixes (`com.tyfon.uifixes`) â€” compatible

Its client half has no stash-width assumption. Its **server** half does read the
template â€” `PutToolsBackAddItemsPatch` calls `grid.Properties.CellsH.Value` and
`CellsV.Value` â€” but it reads them live, per request, long after this mod has run at
`PostLoad`. It therefore picks up the new width automatically.

The two `AcceptableValueRange<int>(1, 10)` in its settings are mousewheel scroll speed,
not columns.

### The hideout stash bonus stacks, and is worth understanding

`InventoryHelper.GetPlayerStashSize` reads `CellsH`/`CellsV` off the template and then
**adds** the profile's `StashSize` bonus to the row count. So this mod sets the base and
the hideout bonus is applied on top â€” they compose, and neither overwrites the other.

The side effect: a bonus row is worth more when the stash is wider. At 16 columns a
+10-row bonus is 160 cells rather than 100. So `compensateRows` holds the *base* at
vanilla, and a profile with hideout bonuses ends up somewhat above vanilla overall. That
is in your favour and not worth engineering around â€” scaling the bonus would mean
patching `GetPlayerStashSize`, which is exactly the kind of thing that fights other mods.

### Why capacity rounds up, not down

Sorting is the reason. Both the vanilla sort and Advanced Stash Sorting fail outright
when the result will not fit â€” the latter with its own `InsufficientSortSpaceError`.
Rounding rows down would lose up to `columns - 1` cells, so a nearly-full stash that
sorted before this mod could refuse to sort after it.

So `compensateRows` rounds **up**: the grid is never smaller than vanilla. It overshoots
by less than one row â€” 688 cells against 680 on an Edge of Darkness stash at 16 columns,
about 1%. Two tests hold both ends of that.

### Stash Management Helper â€” listed, not audited

The probe names it (`com.markosz.stashmanagementhelper`) if it is loaded, because knowing
it is there makes a report easier to read. **Its code was not reviewed** â€” only its GUID
was looked up. It is in the census for completeness, not because it has been cleared.

### Other server mods that change stash size

This reads whatever is in the template when it runs and treats that as the baseline, so
"hold capacity" means the capacity it found, not BSG's. If you also run a storage
expansion mod, load order decides which is the baseline, and `verbose: true` prints the
before and after for each stash so you can see what happened.

It refuses to narrow a stash, so it can never undo another mod's widening.

---

## Uninstalling safely

Removing the mod puts the stash back to 10 columns. Anything sitting in column 10 or
beyond is then outside the grid, and the same applies to rows if you were running with
`compensateRows: false` and a hideout bonus that later went away.

The safe order is:

1. **In game, move everything into the first 10 columns.** Auto-sort will not do this for
   you â€” it packs into the grid it currently has, which is the wide one.
2. Check the probe's **`out-of-bounds items: none`** line after a restart. That is the
   game's own `Grid.OutOfBoundsItems`, so it is the authoritative answer rather than a
   guess.
3. Then delete `SPT_Runtime/user/mods/UltrawideStash/` and
   `BepInEx/plugins/UltrawideStash.Probe.dll`.

If you skip step 1, restore the profile backup you took at install. This is inherent to
changing a grid's size â€” any mod that does it has the same exit â€” but it is the one thing
here that can cost you something.

---

## The measurement procedure

The probe answers the one question that decides whether a client-side UI fix is needed.

1. Install both halves. Leave `columns` at its default for the first run.
2. Launch, open your stash, and let it sit for a second.
3. Find `BepInEx\LogOutput.log` and grep for `[UltrawideStash]`.

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
[UltrawideStash] =============================
```

What to read from it:

- **`out-of-bounds items`** â€” anything but `none` means the stash is holding items you
  cannot reach. Stop and restore a profile backup.
- **The `STRETCH` / `fixed` column** â€” this is the answer. A `fixed` ancestor between
  the grid and the canvas is what clips a widened grid, and its name and components say
  exactly what a fix has to change.
- **`columns that would fit`** â€” the ceiling for this monitor. Set `columns` from this
  rather than from taste.
- **`grid layout`** â€” must say `consistent`. If it does not, sorting mods will refuse to
  sort, and this line is why.
- **`companion plugins`** â€” which stash-touching mods were loaded, and at what version,
  so a report describes itself.

## Building

```
scripts\pack.ps1 -SPTPath C:\HUH            # build, test, zip
scripts\pack.ps1 -SPTPath C:\HUH -Install   # and install
scripts\test-database.ps1 -SPTPath C:\HUH   # stash ids against a real database
dotnet test tests\UltrawideStash.Server.Tests
```

Run those through PowerShell, not Bash â€” `C:\HUH` gets mangled to `C:HUH` otherwise.

The probe references **no game assembly**. The `Assembly-CSharp.dll` in `Managed` is not
the one the game runs â€” the SPT Launcher applies a delta at startup that renames
obfuscated types â€” so every game member is resolved by its patched name at runtime
through `AccessTools`, in `GameTypes.cs`. The plugin therefore builds against any
install, launched or not, and `pack.ps1` asserts the DLL carries no `Assembly-CSharp` or
`spt-*` reference.

## Status

Built against SPT 4.1.5 / EFT 0.16.9.5.40743 / BepInEx 5.4.23.5. Clean at 0 warnings;
49 logic tests and 16 database checks pass.

Compatibility with auto-sort, Advanced Stash Sorting and UI Fixes was established by
reading their code - see Compatibility - not by running them. None of the three is
installed on the development machine.

Untested, in rough order of risk:

- **Whether the widened grid is drawn or clipped.** The whole reason the probe exists.
- **Whether the probe's Harmony patch fires at all.** `SimpleStashPanel.Show` is patched
  with `MonoBehaviour __instance`, which is a genuine supertype, but that has not run.
- **Whether the tallest GridView is really the stash.** It is by a wide margin on paper
  â€” 30 rows minimum against a backpack's handful â€” but an open container has its own.
- **The occupancy scan against a real played profile.** The only profile on the
  development machine is an unplayed stub.
- **What the game does with a stash whose template changed between sessions.** Widening
  should be non-destructive; that is reasoning, not observation.
- **The 0.2.0 diagnostics themselves.** The companion census reads BepInEx's
  `Chainloader.PluginInfos`, and the layout line reads `Grid.Layout` â€” neither has run,
  and the three GUIDs watched for have never been seen matching a live plugin.

## Repository

https://github.com/JoelHauser/UltrawideStash

## Licence

MIT.
