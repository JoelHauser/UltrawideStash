<!--
  The mod-page description, kept here so it is version-controlled alongside the code
  it describes. Paste everything below this comment into the listing.

  This is NOT the README. README.md is for anyone reading the repository and goes into
  the reasoning; this is for someone deciding whether to download it. Keep it to what a
  player needs today: no version history, no internals. When a release changes what a
  player sees, change both.

  The Forge shows the word "Tarkov" as "T*****", so say "EFT" instead. The page also
  came through without a table once, so use plain lists.

  Before posting: drop a screenshot in where the comment says, and fill the page's
  short-description field with --
    "ULTRAWIDE ONLY. Widens the stash to fill a 21:9 or 32:9 monitor. Does nothing
     on 16:9. Edits your profile -- back it up."
-->

# ⚠️ ULTRAWIDE MONITORS ONLY

On a 16:9 monitor (1080p, 1440p, 4K) this does nothing. EFT's menu has no spare width
there to fill.

---

Widens your stash to fill the empty space beside it on an ultrawide. Same capacity, less
scrolling: on 3440x1440 a 10x68 stash becomes **19x36**.

<!-- screenshot goes here -->

- **21:9** (2560x1080, 3440x1440): 19 columns
- **32:9** (5120x1440): 39 columns

## ⚠️ Back up your profile first

The mod moves items to fit the new stash shape, so it rewrites your profile. It keeps
its own backups in `%LOCALAPPDATA%\UltrawideStash\backups`, but copy
`SPT_Runtime/user/profiles` somewhere safe before your first start anyway.

## Install

Unzip over your SPT folder. No setup needed.

## Features

- **Wide everywhere in the menu:** the character screen, traders, the flea market's
  add-offer window, the scav loot transfer, mail and hideout transfers. Buttons are never
  covered.
- **Never in raid:** your inventory in raid stays vanilla.
- **Every edition**, hideout stash upgrades included.
- **Fika:** each player's stash fits their own screen, so 16:9 friends keep the normal
  stash. Every player needs the mod's client plugin.
- **Sizes itself to your screen.** To set it by hand, change `columns` in
  `ultrawidestash.config.json`. Set `compensateRows: false` to keep every row and get
  extra storage instead of the same capacity.

## Troubleshooting

- **Stuck at 10 columns?** The server log says why. Usually EFT is set to a 16:9
  resolution: set it to your monitor's own in *Settings → Graphics*.
- **Scrolling sideways?** Check `GearPanelReserve` is **620** in
  `BepInEx/config/com.mybutthasarash.ultrawidestash.cfg`.

## Uninstalling

1. Set `"columns": 10` in `ultrawidestash.config.json` (the number, not `"auto"`).
2. Start the server once. It packs everything back into a vanilla stash.
3. Delete the mod's files.

If you deleted it without doing this, items past column 10 may be in your sorting
table. Reinstall and follow the steps, or run the included `repair-stash.ps1`.

## Compatibility

For **SPT 4.1.x**. Works with auto-sort, UI Fixes, Advanced Stash Sorting, Loot In
Vicinity, SVM (your extra rows are kept), UIScale.Reloaded (not needed) and Kaeno's
Trader Scrolling.
