<!--
  The mod-page description, kept here so it is version-controlled alongside the code
  it describes. Paste the whole thing into the listing.

  This is NOT the README. README.md is for anyone reading the repository and goes into
  the reasoning; this is for someone deciding whether to download it. When a release
  changes what a player sees, change both.

  The Forge shows the word "Tarkov" as "T*****", so say "EFT" instead. The page also
  came through without the screen table once, so the screens are a plain list.

  Before posting: drop a screenshot in where the comment says, and fill the page's
  short-description field with --
    "ULTRAWIDE ONLY. Widens the stash to fill a 21:9 or 32:9 monitor. Does nothing
     on 16:9. Edits your profile -- back it up."
-->

# ⚠️ ULTRAWIDE MONITORS ONLY

**On a 16:9 monitor this does nothing.** EFT scales its menu by height, so 1080p,
1440p and 4K all get the same width to work with and none of it is spare. 16:10 too.
You need a screen wider than 16:9 for there to be any room to fill.

---

Your stash is 10 columns wide, and on an ultrawide there are a few hundred pixels sitting
empty beside it. This widens the stash panel and fills them. On 3440x1440 that's
**10x68 → 19x36** — same capacity, half the scrolling.

<!-- screenshot goes here -->

**What you get:**

- **16:9** (1920x1080, 2560x1440, 3840x2160) — 10 columns, nothing changes
- **21:9** (2560x1080, 3440x1440) — 19 columns
- **32:9** (5120x1440) — 39 columns

## ⚠️ Back up your profile first

Changing the shape of your stash means moving items, so **this mod rewrites your profile
file**. It takes a backup before every change (your original plus the two most recent,
kept in `%LOCALAPPDATA%\UltrawideStash\backups` so they stay out of your profiles folder)
and hands the result to SPT to save — but it's a young mod, and anything that rewrites a profile
can damage one. **Copy `SPT_Runtime/user/profiles` somewhere safe before your first
start.**

Nothing in the mod deletes an item, so the expected worst case is a misplaced item rather
than a missing one. Expected, not guaranteed.

## Install

Unzip over your SPT folder. No setup, no config needed. **Start the server before the
game**, as usual.

Removing it later takes one extra step — see [Uninstalling](#uninstalling).

## Good to know

- **Same capacity by default** — wider and shorter, not extra storage. Set
  `compensateRows: false` in the config to keep every row.
- **Every edition**, hideout stash upgrades included.
- Items that no longer fit get packed back in; items that already fit are never shuffled
  — including anything in extra rows from a stash-rows bonus.
- Sizes itself to your screen. You can set `columns` by hand in
  `ultrawidestash.config.json`, capped at what your monitor can show.
- **Wide at the traders and on the flea market** (new in 1.1.0) — on the trader screen
  the stash grows left and the Buy / Sell column moves over to make room. When you list
  an item on the flea market, the add-offer window gets wider so your whole stash shows.
- **Wide on the transfer screens too** — the scav loot transfer after a raid, receiving
  items from mail, and putting items into a hideout area. The stash grows into the empty
  space beside the screen and **never covers the buttons** (Next, Sell All, Receive All
  and the rest). Turn these, the traders and the flea window off with
  `WidenTransferScreens` in the BepInEx config.
- **Menu only — never in raid.** Your inventory in raid looks exactly like vanilla:
  crates, bodies and loot panels are left alone. The stash widens again when you're back
  in the menu.
- **Stuck at 10 columns?** Look in the **server** log. If the game couldn't widen the
  stash, it says why there. The usual cause is EFT itself set to a 16:9 resolution on a
  wider monitor. Set EFT's resolution in *Settings → Graphics* to your monitor's own.
- **Stash narrower than your screen, or scrolling sideways?** Check `GearPanelReserve`
  in `BepInEx/config/com.mybutthasarash.ultrawidestash.cfg` is **620**. A higher number
  leaves the stash less room: at 1000 a 32:9 screen gets 27 columns instead of 39. The
  server log warns you when it's changed.

# Uninstalling — set `columns` to 10 first

1. In `ultrawidestash.config.json`, set `"columns": 10` — the number, not `"auto"`.
2. **Start the server once** and let it finish loading. It packs everything back into a
   vanilla stash, backing up your profile first.
3. Delete the files — and, once you're happy with your stash, the backups folder at
   `%LOCALAPPDATA%\UltrawideStash`.

**If you just delete it instead**, your stash snaps back to 10 columns and the game moves
anything in columns 11+ to your **sorting table** — check there first. Anything it misses
is still in your profile at its old coordinates; SPT never prunes out-of-bounds items.
Reinstall and follow the three steps, or run the included `repair-stash.ps1` (plain
PowerShell, no mod needed, shows you what it would do before writing).

## Compatibility

Fine with auto-sort, **UI Fixes**, **Advanced Stash Sorting** and **Loot In Vicinity**
(the mod stays out of raid, so its Nearby Items panel is untouched).

**SVM (Server Value Modifier)** works too. If you've given your stash more rows with SVM,
those rows are kept. With the default settings your stash gets wider and shorter but
holds the same number of cells SVM gave it. Set `compensateRows: false` to keep SVM's
rows and get the extra width as well.

**UIScale.Reloaded** works too, but you no longer need it. Before 1.0.5 this mod only
widened the stash if UIScale.Reloaded was installed as well. Without it, EFT keeps the
inventory screen as a 16:9 block in the middle of the monitor, and the stash stayed at
10 columns. 1.0.5 widens that block to the full screen itself.

For **SPT 4.1.x**. Tested on 4.1.6 at 3440x1440, with UIScale.Reloaded and without it,
and **by a player on a real 5120x1440 (32:9) monitor**, where the stash filled the
screen. The trader screen and the flea window (1.1.0) have been tested at 3440x1440 only.
At 5120x1440 they should fit all 39 columns, but nobody has tried it yet. 2560x1080 hasn't
been reported yet either. If you play at either, please say how it looks.
