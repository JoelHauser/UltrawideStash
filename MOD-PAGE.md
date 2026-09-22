<!--
  The mod-page description, kept here so it is version-controlled alongside the code
  it describes. Paste the whole thing into the listing.

  This is NOT the README. README.md is for anyone reading the repository and goes into
  the reasoning; this is for someone deciding whether to download it. When a release
  changes what a player sees, change both.

  Before posting: drop a screenshot in where the comment says, and fill the page's
  short-description field with --
    "ULTRAWIDE ONLY. Widens the stash to fill a 21:9 or 32:9 monitor. Does nothing
     on 16:9."
-->

# ⚠️ ULTRAWIDE MONITORS ONLY

**If your monitor is 16:9 — 1080p, 1440p or 4K — this mod will do nothing for you.**
Tarkov's menu scales by height, so every 16:9 screen has exactly the same width to work
with and none of it is spare. 16:10 is the same story. You need a screen wider than 16:9
(21:9 or 32:9) for there to be any room to fill.

---

Your stash is 10 columns wide and there are a few hundred pixels sitting empty beside it
on an ultrawide. This widens the stash panel and fills them.

On a 3440x1440 screen that's **10x68 → 19x36** — same capacity, roughly half the
scrolling.

<!-- screenshot goes here -->

| Your screen | You get |
| --- | --- |
| 1920x1080, 2560x1440, 3840x2160 (16:9) | 10 columns — nothing changes |
| 2560x1080, 3440x1440 (21:9) | 19 columns |
| 5120x1440 (32:9) | 39 columns |

## Install

Unzip over your SPT folder. Works straight away — no setup, no config needed.

Two files go in: the server mod (`SPT_Runtime/user/mods/UltrawideStash/`) sets the width,
the plugin (`BepInEx/plugins/`) widens the panel to fit it.

**Start the server before the game**, as usual.

Removing it later takes one extra step — see [Uninstalling](#uninstalling).

## Good to know

- **Same capacity by default.** Wider and shorter, so you scroll less — not extra storage.
  Set `compensateRows` to `false` in the config if you'd rather keep every row.
- **Works on every edition**, and hideout stash upgrades are handled.
- **Your items are safe.** Nothing is deleted, anything that would end up off the edge of
  a resized stash is moved back in for you, and your profile is backed up first.
- It sizes itself to your screen automatically. You can set `columns` by hand in
  `ultrawidestash.config.json`, but it's capped at what your monitor can actually show.

# Uninstalling — set `columns` to 10 first

**Your items are safe either way — but there is a clean way and a messy way.**

If you just delete the files, your stash snaps back to 10 columns and anything in columns
11+ is out of bounds. **The game moves those to your sorting table** when you next load
into the menu, so check there first — that is where they'll be. Anything it doesn't
catch is still sitting in your profile at its old coordinates, untouched; SPT never
deletes or prunes out-of-bounds items.

The clean way puts them back in the stash instead of leaving you to re-sort a full
sorting table:

1. Open `SPT_Runtime/user/mods/UltrawideStash/ultrawidestash.config.json` and set
   `"columns": 10` — the number `10`, not `"auto"`.
2. **Start the server once** and let it finish loading.
3. Now delete the two files.

Step 2 is where the mod does the work for you:

- **Your profile is backed up first**, to a timestamped `.bak` beside it, before a single
  item is moved.
- Items are packed back into the 10-wide stash. Only what genuinely won't fit goes to the
  sorting table — which the mod never touches, so it survives the uninstall.
- It's all-or-nothing per stash. If something can't be placed, nothing is changed at all.

### If you already deleted it

1. **Look at your sorting table** — most of it should be there.
2. Anything missing is still in your profile. Either reinstall the mod and follow the
   three steps above, or run `repair-stash.ps1` from the download — plain PowerShell, no
   mod and no matching SPT version needed. It backs up first, and shows you what it would
   do before you let it write.

## Compatibility

Fine with auto-sort, **UI Fixes** and **Advanced Stash Sorting**.

For **SPT 4.1.x**. Tested on 4.1.6 at 3440x1440. Back up `SPT_Runtime/user/profiles`
before your first run — it edits your profile.
