# UltrawideStash -- working notes for Claude

Makes the EFT stash wider than 10 columns so it fills the horizontal room an ultrawide
has. Two halves: an SPT server mod that changes the stash item template, and a
**read-only** BepInEx probe that measures the stash panel and logs what it finds.

**Nothing here has ever run in the game.** Everything was read out of the patched game
assembly and SPT's database by static analysis. 43 logic tests and 16 database checks
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

## Capacity, and the guard that makes it safe

The user chose "wider and shorter, same capacity". That means shortening, and shortening
a played stash strands everything below the new last row.

So `StashWidener.DeepestOccupiedRowByStash` walks every profile through
`SaveServer.GetProfiles()`, finds the items whose `ParentId` is the stash, and computes
`Y + height` -- **with rotation applied**, because `ItemRotation.Vertical = 1` swaps the
template's width and height and getting that backwards under-reports depth, which is the
direction that loses items. `StashLayout.For` then never returns fewer rows than that.

If honouring the items means capacity rises instead of staying flat, it rises. Losing an
item is not an acceptable price for a tidy number, and there is a test that says so
(`AFullStashRefusesToBeShortened`).

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
default and an occupancy guard; probe measures and logs. Built clean, 43 logic tests and
16 database checks pass, `releases\UltrawideStash_V0.1.0.zip` packed.

**Next work is to read the user's probe log**, specifically the `STRETCH`/`fixed` chain,
and write the client-side width fix against it. Do not write UI patches before that log
exists -- the anchoring is unknowable from here, and a guess costs a round trip.
