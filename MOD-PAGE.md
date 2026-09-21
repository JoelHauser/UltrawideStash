<!--
  The mod-page description, kept here so it is version-controlled alongside the code
  it describes. Paste the whole thing into the listing.

  This is NOT the README. README.md is for anyone reading the repository and goes into
  the reasoning; this is for someone deciding whether to download it. When a release
  changes what a player sees, change both.

  Before posting: drop a screenshot in where the comment says, and fill the page's
  short-description field with something like --
    "Widens the stash to fill an ultrawide monitor. Sizes itself to your screen;
     does nothing on 16:9."
-->

Makes Tarkov's stash wider than 10 columns, so it fills the horizontal space an ultrawide
monitor has and a 16:9 one doesn't. A 3440x1440 screen has around **660 logical pixels
sitting empty** beside the stash. This puts your stash in it.

<!-- screenshot goes here -->

## Read this first if you're on 16:9

**On a 16:9 monitor this does nothing by default, and that's correct.** EFT scales its
menu by height alone, so 1080p, 1440p and 4K all get a canvas exactly 1920 units wide —
none of them have room to spare. Only a wider-than-16:9 screen gains any.

| Screen | Spare width | Extra columns |
| --- | --- | --- |
| 1920x1080 / 2560x1440 / 3840x2160 | none | — |
| 2560x1080 | 640 px | ~10 |
| 3440x1440 | 660 px | ~10 |
| 5120x1440 | 1920 px | ~30 |

## What it does

- **Sizes itself to your screen.** The default is `"auto"`. A bundled client plugin
  measures your real stash panel and writes down what fits; the server uses it. Ask for
  more than fits and it's clamped and logged — a too-wide grid gets *clipped*, not
  shrunk, and clipped columns look exactly like lost items.
- **Same capacity by default.** The stash gets wider and shorter — less scrolling, not
  more storage. Set `compensateRows` to `false` if you'd rather keep every row.
- **Every edition.** Standard through Unheard, and the hideout stash upgrades are handled
  — upgrading can never shorten your stash.
- **Your items stay put.** Nothing is ever deleted, rows are never cut below what you're
  actually storing, and your profile is backed up before any edit.

## Install

Unzip over your SPT root. Two files:

- `SPT_Runtime/user/mods/UltrawideStash/` — sets the width
- `BepInEx/plugins/UltrawideStash.Probe.dll` — measures your screen

Start the game, open your stash once, then **restart the server**. `"auto"` now knows
what fits.

## Config

`SPT_Runtime/user/mods/UltrawideStash/ultrawidestash.config.json`

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

| Setting | What it does |
| --- | --- |
| `columns` | `"auto"`, or a number. Vanilla is 10. Clamped to what your screen can show. |
| `screenWidth` / `screenHeight` | Only used before anything has been measured. Handy on a dedicated server. |
| `ignoreMeasurement` | `true` uses `columns` exactly as written, no ceiling. You're on your own. |
| `compensateRows` | `true` holds total capacity at vanilla. `false` keeps every row. |

## Uninstalling

**Don't just delete it.** Set `"columns": 10` (the number, not `"auto"`), start the
server once so everything is packed back into a vanilla stash, then delete the files.
A `HOW-TO-UNINSTALL.txt` is written into the mod folder, and a standalone PowerShell
recovery script ships alongside if you forget.

## Compatibility

Works with EFT's own auto-sort, **UI Fixes**, and **Advanced Stash Sorting** — none of
them hard-code a stash width. Established by reading their source, not by testing.

## ⚠️ Untested in-game

Built entirely from static analysis of the game client and SPT's database. The logic has
**144 automated tests** behind it, but none of this has run in a live game. It writes to
your profile. **Back up `SPT_Runtime/user/profiles` first.**

Built for **SPT 4.1.5** / EFT 0.16.9.5.40743.
