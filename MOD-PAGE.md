<!--
  The mod-page description, kept here so it is version-controlled alongside the code
  it describes. Paste the whole thing into the listing.

  This is NOT the README. README.md is for anyone reading the repository and goes into
  the reasoning; this is for someone deciding whether to download it. When a release
  changes what a player sees, change both.

  Before posting: drop a screenshot in where the comment says, and fill the page's
  short-description field with --
    "ULTRAWIDE ONLY. Widens the stash to fill a 21:9 or 32:9 monitor. Does nothing
     on 16:9. Edits your profile -- back it up."
-->

# ⚠️ ULTRAWIDE MONITORS ONLY

**On a 16:9 monitor this does nothing.** Tarkov scales its menu by height, so 1080p,
1440p and 4K all get the same width to work with and none of it is spare. 16:10 too.
You need a screen wider than 16:9 for there to be any room to fill.

---

Your stash is 10 columns wide, and on an ultrawide there are a few hundred pixels sitting
empty beside it. This widens the stash panel and fills them. On 3440x1440 that's
**10x68 → 19x36** — same capacity, half the scrolling.

<!-- screenshot goes here -->

| Your screen | You get |
| --- | --- |
| 1920x1080, 2560x1440, 3840x2160 (16:9) | 10 columns — nothing changes |
| 2560x1080, 3440x1440 (21:9) | 19 columns |
| 5120x1440 (32:9) | 39 columns |

## ⚠️ Back up your profile first

Changing the shape of your stash means moving items, so **this mod rewrites your profile
file**. It takes a timestamped `.bak` before every change and hands the result to SPT to
save — but it's a young mod, and anything that rewrites a profile
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
- Items that no longer fit get packed back in; items that already fit are never shuffled.
- Sizes itself to your screen. You can set `columns` by hand in
  `ultrawidestash.config.json`, capped at what your monitor can show.
- **Menu only — never in raid.** Your inventory in raid looks exactly like vanilla:
  crates, bodies and loot panels are left alone. The stash widens again when you're back
  in the menu.

# Uninstalling — set `columns` to 10 first

1. In `ultrawidestash.config.json`, set `"columns": 10` — the number, not `"auto"`.
2. **Start the server once** and let it finish loading. It packs everything back into a
   vanilla stash, backing up your profile first.
3. Delete the files.

**If you just delete it instead**, your stash snaps back to 10 columns and the game moves
anything in columns 11+ to your **sorting table** — check there first. Anything it misses
is still in your profile at its old coordinates; SPT never prunes out-of-bounds items.
Reinstall and follow the three steps, or run the included `repair-stash.ps1` (plain
PowerShell, no mod needed, shows you what it would do before writing).

## Compatibility

Fine with auto-sort, **UI Fixes**, **Advanced Stash Sorting** and **Loot In Vicinity**
(the mod stays out of raid, so its Nearby Items panel is untouched).

For **SPT 4.1.x**. Tested on 4.1.6 at 3440x1440.
