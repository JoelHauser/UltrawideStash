# UltrawideStash -- working notes for Claude

Makes the EFT stash wider than 10 columns so it fills the horizontal room an ultrawide
has. Two halves: an SPT server mod that changes the stash item template, and a
**read-only** BepInEx probe that measures the stash panel and logs what it finds.

**Nothing here has ever run in the game.** Everything was read out of the patched game
assembly and SPT's database by static analysis. 75 logic tests and 16 database checks
pass; that means the arithmetic is right, not that the stash looks right.

## The box this was built on

| | |
| --- | --- |
| SPT install | `C:\HUH` -- **never launched**, profile is a 0.4 KB `TEST` stub |
| SPT version | 4.1.5 |
| EFT client | `0.16.9.5.40743` |
| BepInEx | 5.4.23.5, HarmonyLib 2.9.0 |
| Server runtime | .NET 10, `SPTarkov.Server.Core` 4.1.2 |

The user plays on **3440x1440** on a different machine, where their SPT lives on `H:`.
They said "H:\HUH" in the original request; there is no `H:` drive on the development
box, and `C:\HUH` is the one that exists here. Do not assume paths carry between them.

```
scripts\pack.ps1 -SPTPath C:\HUH            # build both halves, test, zip
scripts\pack.ps1 -SPTPath C:\HUH -Install
scripts\test-database.ps1 -SPTPath C:\HUH   # the five stash ids against real items.json
dotnet test tests\UltrawideStash.Server.Tests
```

**Run those through PowerShell, not Bash** -- the `C:HUH` mangling trap, same as
LoadingRaid, CamoPatch and SPT-Casino.

## The finding the whole mod rests on

`UICanvasScalerController.RunResolutionObserver` sets the canvas scale factor to
`Math.Min(width / 1920f, height / 1080f)`, and `Utils.SetCanvasRestriction` applies it
with `uiScaleMode = ConstantPixelSize`. So **the height alone sets the scale** and any
width past 16:9 is extra canvas:

- 3440x1440 -> scale 1.333 -> canvas **2580 x 1080** logical units
- 1920x1080 -> scale 1.000 -> canvas 1920 x 1080

660 extra logical pixels on the user's monitor. That is the empty space they asked
about, and it is real.

Note there is a second, unused method on that class: `ReferenceScaleFactor` computes
`Mathf.Lerp(log2(w/1920), log2(h/1080), 1)` -- and `t = 1` means it returns the height
term outright, so it agrees with the live path. Do not mistake it for a different rule.

## Why the width is a server change and nothing more

`EFT.UI.DragAndDrop.GridView.OnGridResized(int, int)` sets `LayoutElement.minWidth` and
`_rectTransform.sizeDelta` from `ItemViewFactory.GetCellPixelSize`, whose IL is exactly
`X * 63 + 1, Y * 63 + 1`. **Nothing in the client hard-codes 10.** The width lives in
the item template, the client is served it over `/client/items`, so the server owns it.

The five player stashes, all `cellsH: 10`, verified against `items.json` by
`scripts\test-database.ps1`:

| Id | Edition | Vanilla |
| --- | --- | --- |
| `566abbc34bdc2d92178b4576` | Standard | 10x30 |
| `5811ce572459770cba1a34ea` | Left Behind | 10x40 |
| `5811ce662459770f6f490f32` | Prepare for Escape | 10x50 |
| `5811ce772459770e9e5f9532` | Edge of Darkness | 10x68 |
| `6602bcf19cc643f44a04274b` | The Unheard Edition | 10x72 |

Deliberately excluded: `5c0a596086f7747bef5731c2` (dev stash 10x300), the 8-wide
containers (they would trip the narrowing guard anyway), and every hideout/scav stash.

## The open question, and why the probe exists

`SimpleStashPanel._stashScroll` is a `ScrollRect`. Whether its viewport stretches with
the canvas or is pinned to a fixed width is **serialized prefab data** -- not readable
from the assembly, at all, by any amount of Cecil. So it is measured at runtime instead.

This is the same discipline as `ScreenFit` in the LoadingRaid repo, for the same reason,
and the notes there apply: a number taken off the live hierarchy holds at any resolution
and any screen shape without knowing how the UI was authored.

`StashMeasure` walks from the stash's `GridView` up to the `Canvas`, printing each
ancestor's rect, its horizontal anchors (`STRETCH` vs `fixed`) and its component type
names. The `fixed` ancestor nearest the canvas is what will clip a widened grid, and its
components say what a fix has to change. **Read that log before writing any UI patch.**

## Capacity, the guard, and the bug that guard had

The user chose "wider and shorter, same capacity". Shortening a played stash is the one
thing here that can move a player's belongings, so it is guarded. **0.2.0's guard did not
work at all**, and the way it failed is worth keeping.

### The load-order bug (fixed in 0.3.0)

0.2.0 asked `SaveServer.GetProfiles()` from its `IOnLoad` at `OnLoadOrder.PostLoad`. From
the IL:

- SPT orders `IOnLoad` with `Enumerable.OrderBy` on `Injectable.TypePriority` --
  **ascending** (`DependencyInjectionHandler.InjectAll`).
- The default `TypePriority` is **`int.MaxValue`** (2147483647), both as the ctor default
  and as `DependencyInjectionExtensions.GetTypePriority`'s fallback.
- `SaveCallbacks` -- whose `OnLoadAsync` calls `SaveServer.LoadAsync()` and is what fills
  that dictionary -- carries a **bare `[Injectable]`**, so it sits at that default.
- `OnLoadOrder.PostLoad` is **1,000,000**.

One million sorts before two billion, so the mod ran first and the dictionary was empty.
`deepestOccupiedRow` was always 0 and rows were always cut to the capacity target. On an
Edge of Darkness profile at 16 columns that is 68 rows down to 43, with nothing stopping
it. **A guard that reads an empty collection looks exactly like a guard that passed.**

`ProfileScan` reads `user/profiles/*.json` off disk instead, which takes the ordering
question off the table. It fails safe: anything it cannot parse returns `Confident = false`,
and `StashWidener` then passes the **vanilla row count** as the occupancy floor, so
`StashLayout` clamps rows to vanilla and nothing shortens.

The profile path comes from `SaveServer`'s private `profileFilepath` where it can be
reflected, falling back to `AppContext.BaseDirectory` + `user/profiles` -- the literal
`SaveServer.RemoveProfile` itself uses.

### Why the game's rescue does not fire (settled, third attempt)

This was got wrong twice. 0.3.0 said the rescue works. The first correction said the
Sorting Table "has a 0x0 grid" as though it were incapable. Both were sloppy; the real
answer is **timing**, and it is now traced end to end:

- `MainMenuShowOperation.MoveBrokenItemsToSortingTable` gathers `OverlappingItems` +
  `OutOfBoundsItems` and calls `Grid.FindFreeSpace` on the Sorting Table.
- The Sorting Table item (`602543c13fee350cd564d032`) declares `cellsH: 0, cellsV: 0`.
- **`GridSerializer.Deserialize` turns those zeroes into the stretch flags**: it passes
  `cellsH == 0` and `cellsV == 0` into `Grid..ctor`'s two stretch parameters. So a zero
  dimension means *growable*, and the Sorting Table's grid is stretchable in both axes.
  It is not incapable -- do not write that again.
- It is, however, **unsized**. `Grid.GetFreeLocation` is a pure search: two loops bounded
  by `firstDimensionSize`/`secondDimensionSize`, no growth anywhere. At `0 x 0` neither
  loop runs and it returns null.
- The only thing that sizes it is `SortingTableWindow.ShowGrid` -> `SortingTable.ClampSize`
  -> `Grid.ClampSize(w, h, force: false)`. `ClampSize` *can* widen (it inserts columns and
  calls `set_GridWidth` at IL_0230, gated on `CanStretchHorizontally` at IL_010d, which is
  true here) -- but it only runs when the player opens that window, which is after the
  main-menu rescue.

Net: on the launch after the mod is removed, the rescue logs `Cannot find free space on
sorting table for a bad item` per item and skips. It might succeed on a later main-menu
load in a session where the Sorting Table window was opened first. Not plannable.

### What 0.4.0 does instead

`StashRepack` + `ProfileStore` relocate out-of-bounds items **in the profile file** on
every start, against the grid's final dimensions.

The ordering that made 0.2.0's guard broken is what makes this correct: we run at
`PostLoad` (1,000,000), `SaveCallbacks` sits at the default `int.MaxValue`, SPT orders
ascending -- so the file is edited before the server ever reads it. No second copy, no
reconciliation.

Rules worth keeping:

- **Driven by final size, not by whether this run changed anything.** Dropping `columns`
  16 -> 10 alters no template (it is already 10) but the profile is full of x >= 10.
  Missing this would make the uninstall path do nothing.
- **Items that fit are never moved.** Reshuffling a stash someone arranged is its own
  kind of damage.
- **Overflow goes to the Sorting Table.** `StashRepack.IntoSortingTable` places what the
  stash cannot hold at 7 columns (`SortingTableWindow.ShowGrid` -> `ClampSize(7, 7)`,
  hardcoded), growing downward. Transfers re-parent the item (`parentId` to the sorting
  table, `slotId` "hideout" -- the sorting table's grid is named "hideout" too). The mod
  never touches the Sorting Table's template, so anything parked there survives removal.
- **All-or-nothing per stash.** Every profile is planned before any is written; an item
  that fits neither the stash nor the table aborts the whole stash, template change
  included.
- **Backup, temp file, then replace.** `ProfileStore.ApplyMoves` copies to a timestamped
  `.bak`, writes a `.tmp`, and only then overwrites.
- Biggest-first placement, because singles placed first fragment the grid and a large
  case then fails on space that existed.

### The occupancy arithmetic

`StashOccupancy.Place` computes `Y + height` **with rotation applied**, because
`ItemRotation.Vertical = 1` swaps the template's width and height. Getting that backwards
under-reports depth, which is the direction that loses items. `ProfileScan.Rotation` reads
an unrecognised rotation value **as rotated** for the same reason.

Only the stash's direct children count. An item inside a backpack has a `y` belonging to
the backpack's grid, and reading it as a stash row invents depth that is not there.

## Compatibility, and how it was established

Asked for at 0.2.0: auto-sort, Tyfon's UI Fixes and Advanced Stash Sorting. All three
were settled by **reading code** -- the game's from the patched assembly, the two mods
from their MIT sources -- not by running anything. Clones were made under the session
scratchpad; re-clone rather than trusting this summary if anything looks off.

- **UI Fixes** -- https://github.com/tyfon7/UIFixes, GUID `com.tyfon.uifixes`
- **Advanced Stash Sorting** -- https://github.com/slpf/AdvancedStashSorting, GUID
  **`com.slpf.advstashsorting`** (note: NOT `advancedstashsorting`, which is what the
  name suggests and what was guessed wrong first). Listed on sp-mod.com as a server
  mod; it is a **client plugin**.
- **Stash Management Helper** -- GUID `com.markosz.stashmanagementhelper`, read only
  for its GUID.

### Auto-sort is width-agnostic by construction

`ItemManipulator.Sort(CompoundItem, InventoryController, bool)` -- from the IL, not a
guess:

```
for each grid: check every item passes the filter        -> AutomaticSortNonFilteredItemError
InventoryController.IsAllowedToSort                      -> CannotSortItemError
remove every item from every grid (ItemAddress.Remove)
ItemSorter.Sort(items)                                   <- ordering ONLY, by type
retry budget = 5 (ldc.i4.5)
for each item: Grid.AddAnywhere(item, EErrorHandlingType) <- placement
  on failure: roll back the last GridAddResult, decrement the budget
```

`ItemSorter` only orders. `Grid.AddAnywhere` -> `FindFreeSpace` -> `FindFreeSpaceInGrid`
does the placing, and the Grid methods that read `GridWidth`/`GridHeight` are exactly:
`get/set_GridWidth`, `get/set_GridHeight`, both ctors, `RaiseResizeEvent`, `AddInternal`,
`Resize`, `CheckLayout`, `SetLayout`, `LiesWithinGrid`, `HasFreeSpaceForItems`,
`FindFreeSpaceInGrid`, `FillSpaceBuffer`, `ClampSize`. No literal width anywhere.

Note `Grid.Resize(Item, IntVec2, IntVec2, bool)` resizes an **item within** the grid
(a folding stock changing footprint), not the grid itself. Easy to misread.

### The two mods

Neither hard-codes a stash width. Verified by grep over both trees for a literal 10 near
grid/stash/column/cell words: UI Fixes' only hits are two
`AcceptableValueRange<int>(1, 10)` for **mousewheel scroll speed**; ASS has none.

- **ASS reimplements placement.** `OrderedStashLayoutPlanner` reads `grid.GridWidth` /
  `grid.GridHeight` (lines 21-22) into an `OrderedLayoutRequest`, and
  `OrderedLayoutEngine` bounds everything on `request.Width`/`request.Height`.
  `BeamWidth = 48` is a search beam, not a grid width.
- **ASS asserts an invariant worth remembering.** `CopyLayout` throws
  `"Grid layout dimensions are inconsistent"` unless
  `grid.Layout.Count == GridWidth * GridHeight`. The probe now prints this, because it
  is the precise explanation for that mod refusing to sort.
- **UI Fixes' server half** reads `grid.Properties.CellsH.Value` in
  `PutToolsBackAddItemsPatch`, live per request, well after our `PostLoad` edit. Its
  client half has no width assumption.
- ASS already ships a `UIFixesCompatPatch` prefixing
  `UIFixes.SortPatches+StackFirstPatch`, so those two coordinate between themselves and
  neither needs anything from us.

### The hideout bonus composes; do not try to scale it

`InventoryHelper.GetPlayerStashSize(PmcData)` reads `CellsH`/`CellsV` off the template
(falling back to 10 and 66 when the value is 0) and then **adds** the profile's
`StashSize` bonus to the rows. So our edit is the base and the bonus stacks on top.

Consequence: a bonus row is worth `columns` cells, so it is worth more when wider.
`compensateRows` therefore holds the **base** at vanilla, and a profile with hideout
bonuses ends up above vanilla overall. Left alone deliberately -- scaling it would mean
patching `GetPlayerStashSize`, which is where every other stash mod also lives.

### Why compensation rounds UP (changed in 0.2.0)

0.1.0 floored, on the reasoning that capacity should never exceed vanilla. That was
wrong, and the compatibility read is what found it: **both** sorters fail outright when
the result will not fit -- ASS with its own `InsufficientSortSpaceError` -- and flooring
loses up to `columns - 1` cells. A nearly-full stash that sorted before the mod could
refuse to sort after it, a visible regression bought for a 1% number.

Ceiling now, so the grid is never smaller than vanilla.
`CompensatingNeverDecreasesCapacity` and `CompensatingStaysWithinOneRowOfVanilla` hold
both ends.

## How this is put together

```
src/UltrawideStash.Server/
  ModMetadata.cs      the one IModMetadata; SPT throws on a second in a folder
  StashSettings.cs    ultrawidestash.config.json, written with defaults on first run
  StashLayout.cs      the shape decision, pure ints, no SPT type -- the tested part
  StashOccupancy.cs   footprint maths, also pure -- the other tested part
  StashWidener.cs     IOnLoad at PostLoad; the only file that touches SPT or the database

src/UltrawideStash.Probe/
  GameTypes.cs        every game member, resolved by patched name, in one place
  StashMeasure.cs     the walk, the arithmetic and the report
  ProbePlugin.cs      BepInPlugin; one postfix on SimpleStashPanel.Show, then poll
```

### Why the probe references no game assembly

Same reason as LoadingRaid, and worth not relearning: the `Assembly-CSharp.dll` in
`Managed` is **not** the one the game runs. The Launcher applies
`SPT_Runtime\SPT_Data\Launcher\Patches\SPT-core\...\Assembly-CSharp.dll.delta`
(HDiffPatch, zstd) at startup and that delta **renames obfuscated types**. An install
that has never been launched still holds the unpatched original -- `C:\HUH` is one.

`pack.ps1` asserts the built DLL carries no `Assembly-CSharp` and no `spt-*` reference.
As of 0.1.0 it references only `mscorlib`, `System.Core`, `BepInEx`, `0Harmony`,
`UnityEngine.CoreModule` and `UnityEngine.UIModule`.

`UnityEngine.dll` (the facade) **is** referenced at compile time -- BepInEx's
`BaseUnityPlugin` derives from `MonoBehaviour` as typed against it -- but it type-forwards
and does not survive into the output's reference list. Do not remove it; the build fails
with CS0012 without it.

### Regenerating the patched assembly

`hpatchz.exe` survives in older session scratchpads; the delta is in the install. Output
must be **16,233,472** bytes from a 15,994,432-byte input.

```
hpatchz.exe <SPT>\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll
            <SPT>\SPT_Runtime\SPT_Data\Launcher\Patches\SPT-core\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll.delta
            <out>\Assembly-CSharp.dll
```

`Mono.Cecil.dll` is in `SPT_Runtime\`. **Keep a copy outside the scratchpad** -- the
LoadingRaid notes record losing it twice, and it is the difference between reading the
game and guessing at it.

## Traps hit while building this

- **`Path` is ambiguous in the server project.** `SPTarkov.Server.Core.Models.Eft.Common.Tables.Path`
  collides with `System.IO.Path` under `ImplicitUsings`. Qualify it.
- **An XML comment cannot contain `--`.** The prose style of these repos uses `--` for
  an em dash constantly, and it is a hard MSBuild parse error (MSB4025) in a `.csproj`.
  Fine in C# `///` comments, fatal in project files.
- **A too-long bash heredoc silently truncated** and failed with "unexpected EOF looking
  for matching `'`". Use the Write tool for files over ~150 lines.
- **A confident wrong number in a doc comment, caught only by its own test.** The cap was
  documented as "40 columns is 2521px -- already past the whole canvas". 2521 is *less*
  than 2580; 40 columns fits a 3440x1440 canvas exactly and 41 does not. The test
  asserting the claim failed, which is the only reason it was found. Write the assertion
  even when the claim feels obvious.
- **Three passes to get the Sorting Table right, and the first two were confident.**
  "The rescue works", then "the Sorting Table has a 0x0 grid so it cannot hold
  anything", then finally the truth: the zeroes are *stretch flags*
  (`GridSerializer.Deserialize` passes `cellsH == 0` / `cellsV == 0` straight into
  `Grid..ctor`), the grid is growable, and what actually defeats the rescue is that
  nothing has sized it yet at that moment. A magic-looking constant in a template is
  worth chasing to its deserializer before concluding anything about it.
- **A method named for what it intends is not evidence it succeeds.**
  `MoveBrokenItemsToSortingTable` was read as proof that stranded items get rescued,
  and that went into the README and into an answer to the user. Reading the rest of
  it -- the Sorting Table's 0-wide template, stash grids having no horizontal stretch,
  `FindFreeSpaceInGrid` not growing anything -- says it will usually log an error and
  skip. Follow the call through to whether its precondition can actually hold before
  quoting it as a guarantee, especially when someone is about to rely on it.
- **A guard that reads an empty collection looks like a guard that passed.** 0.2.0's
  occupancy guard called `SaveServer.GetProfiles()` before SPT had loaded any, got an
  empty dictionary, and reported "nothing is stored" every time -- so it never once
  stopped a row being cut. It had tests, and they passed, because they tested
  `StashLayout` with a number the real caller could never produce. **Test the wiring
  that produces the number, not just the function that consumes it.**
- **Default `TypePriority` in SPT's DI is `int.MaxValue`, and ordering is ascending.**
  So a bare `[Injectable]` runs *last*, and anything with an explicit `OnLoadOrder`
  runs *before* it. `PostLoad` is not last; it is 1,000,000 out of 2,147,483,647.
- **SPT 4.x server mods live under `SPT_Runtime\user\mods\`, not a root-level `user\`.**
  There is no `<SPT>\user\` at all. 0.2.0's zip staged the server DLL at `user\mods\`,
  so unzipping over the SPT root would have created a dead folder and the mod would
  never have loaded -- **silently**, because a server mod in the wrong place is simply
  not found. `pack.ps1 -Install` had the path right all along, so the two install routes
  disagreed and only the zip was wrong. `pack.ps1` now asserts the staged layout against
  an explicit expected list before it zips. The sibling LoadingRaid notes already
  recorded this trap for profiles (`SPT_Runtime\user\`, not `C:\HUH\user\`); it is the
  same trap and it was walked into anyway.
- **`ConvertFrom-Json` cannot read the big SPT tables** (keys differing only by case,
  case-insensitive parser). `test-database.ps1` walks lines instead, which is also far
  quicker on 18 MB. Inherited from the LoadingRaid notes and still true.

## Untested, and what to look for

In rough order of risk:

1. **Does the widened grid draw, or clip?** The whole point. Read the probe's
   `STRETCH`/`fixed` column.
2. **Does the Harmony patch fire?** `SimpleStashPanel.Show` patched with
   `MonoBehaviour __instance`. The base chain is SimpleStashPanel -> UIInputNode ->
   InputNode -> InputNodeAbstract -> `Sirenix.OdinInspector.SerializedMonoBehaviour` ->
   MonoBehaviour, so it is a genuine supertype and Harmony should accept it. Not run.
3. **Is the tallest GridView the stash?** True on paper by a wide margin. An open
   container has its own GridView.
4. **The occupancy scan on a real profile.** Never run against one -- there is no played
   profile on this box. `Item.Location` is typed `object` and is an `int` for magazines,
   which is why the scan pattern-matches on `ItemLocation` rather than casting.
5. **A stash whose template changed between sessions.** Widening should be
   non-destructive. That is reasoning, not observation.
6. **`Grid.OutOfBoundsItems`** is resolved optionally and reported. If it ever says
   anything but `none`, something has gone wrong and the report says to restore a backup.

## Publishing

- Remote **https://github.com/JoelHauser/UltrawideStash.git**, on `main`.
- GUID `com.mybutthasarash.ultrawidestash`, shared by both halves, following the prefix
  the sibling repos are registered under.
- Version lives in each csproj `<Version>`, in `ModMetadata.Version` and in
  `ProbePlugin.PluginVersion`. `pack.ps1` refuses to pack if any of them disagree.
- Commit as `Joel Hauser <jhauser@bostonlightsource.com>` -- **not** the gmail address.
  Configured locally; the global identity is unset on this machine.
- Commit bodies are prose and end with a `Co-Authored-By:` trailer.
- **Write commit messages to a file and use `git commit -F <file>`** with
  `UTF8Encoding($false)`. Piping a here-string adds a BOM in Windows PowerShell 5.1, and
  passing it as an argument makes git treat it as a pathspec. LoadingRaid's 1.0.0 shipped
  a BOM in its subject this way and had to be force-pushed.
- **The Forge forbids mods substantially written by AI agents.** The user has
  acknowledged this for their other repos and said "we're all good" -- do not re-raise it.

## Where this was left off

2026-09-21: **0.1.0**, the first cut. Server mod sets the width with a capacity-holding
default and an occupancy guard; probe measures and logs.

**0.2.0**, compatibility. Auto-sort, UI Fixes and Advanced Stash Sorting all read the
grid's own width -- see the Compatibility section for the evidence. Two changes came out
of that read: compensation rounds **up** rather than down, so sorting can never fail for
want of the cells flooring threw away; and the probe now reports the `Grid.Layout`
invariant ASS asserts, plus a census of which companion plugins are loaded. Built clean,
75 logic tests and 16 database checks pass.

**0.3.0**, item safety, prompted by the user asking what happens to a player's items on
install, on update and on uninstall. Answering it properly found that 0.2.0's occupancy
guard never worked -- see the load-order bug above -- and that the game rescues
out-of-bounds items to the Sorting Table by itself, which is why that failure would have
been survivable. `ProfileScan` replaces the guard and fails safe. 64 logic tests.

**0.4.0**, because "we cannot have this be the case at all" -- correct response to being
told an uninstall could leave items invisible. The mod now relocates out-of-bounds items
in the profile itself rather than hoping the game will. Uninstalling is a documented,
tested procedure: set columns to 10, start once, delete. 68 logic tests.

**Next work is to read the user's probe log**, specifically the `STRETCH`/`fixed` chain,
and write the client-side width fix against it. Do not write UI patches before that log
exists -- the anchoring is unknowable from here, and a guess costs a round trip.
