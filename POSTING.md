# HuntsAndRifts 1.0.0 — posting sheet

POSTED 2026-09-06: Nexus https://www.nexusmods.com/vrising/mods/41 (mod 41) and Thunderstore https://thunderstore.io/c/v-rising/p/Team_GreenEye/HuntsAndRifts/ (categories Mods, Client, Oakveil Update). Icon: N1 composite; Nexus header 1300x372.

Package: `dist/HuntsAndRifts-1.0.0-thunderstore.zip` (manifest.json, README.md, CHANGELOG.md, icon.png 256x256, plugins/HuntsAndRifts.dll).
Simple zip for friends: `dist/HuntsAndRifts-1.0.0.zip`.

## Decisions needed before posting

1. **website_url** Done: https://github.com/Greeneye0/HuntsAndRifts (repo created, AGPL-3.0 LICENSE added).
2. **License.** Done: AGPL-3.0 (LICENSE in repo and package).
3. **Icon.** Done: icon.png is the N1 composite (name over the Church of the Damned card and the rift card). Nexus header: `dist/nexus_header_1400x400.png`.
4. **Verify in game first** (Thunderstore moderators flagged rapid re-releases on Satisvampory, so 1.0.0 should be the tested build): the stray "T1/T2" text beside the map panel, the rift phase wording while a rift winds down, tier symbols with both tiers open, and the other session's scrolling/cleanup changes. The log lines to check: `HuntsAndRifts stray-diag`, `HuntsAndRifts gates:`, `HuntsAndRifts rift card shows`.
5. **Folder name.** The project folder is still `C:\VRisingMods\HuntClock` (another session works there). Assembly, GUID, plugin name, manifest, README, and changelog are renamed; the folder is not.

## Thunderstore upload form (v-rising community)

- Team: **Team_GreenEye**
- Community: **V Rising**
- Categories: **Client-side**, **Mods** (add **Tools/Utilities** if the community offers it)
- NSFW: no
- File: `dist/HuntsAndRifts-1.0.0-thunderstore.zip`

manifest.json as packaged:

```json
{
  "name": "HuntsAndRifts",
  "version_number": "1.0.0",
  "website_url": "https://github.com/Greeneye0/HuntsAndRifts",
  "description": "Client-only UI: servant hunts grouped with timers, power and specializations, click to focus the map; both Mortium Rift tiers with timers on the map card and plot tooltips; pickup bag totals.",
  "dependencies": ["BepInEx-BepInExPack_V_Rising-1.733.2"]
}
```

(191 characters; Thunderstore's limit is 250.)

## Nexus Mods listing (if you also post there, as with Satisvampory)

**Name:** HuntsAndRifts
**Category:** User Interface (or Gameplay if UI is not offered)
**Summary (short):** Client-side UI for servant hunts and Rift Incursions: hunt cards with timers, power and specializations, click-to-focus map, both Rift tiers with timer bars, and bag totals on pickups.

**Description (BBCode):**

```
[b]HuntsAndRifts[/b] adds servant hunt details, Rift Incursion timers, and pickup totals to V Rising's UI. It runs on your game client only and changes no gameplay rules. Nothing to install on the server.

[b]Servant hunts (map, throne view)[/b]
[list]
[*]Hunts are grouped into cards. Click a hunt card, or a servant's name, timer, or location, to focus the map on that hunt.
[*]Each servant shows name, hunt power (the number from Choose a Servant), specialization icons, and a smaller status line with time left.
[*]A green ring marks a specialization the hunt favors.
[*]Hover a hunt zone to see its required power and favored specializations; occupied hunts also show each servant's power and icons.
[*]Idle servants are listed under Available; injured or dead under Injured / Dead. Long rosters scroll.
[/list]

[b]Rift Incursions[/b]
[list]
[*]The map card shows a timer row for each Mortium tier: time left while active, otherwise the countdown to the next one, each with its own bar and tier symbol. Overlapping tiers are supported.
[*]Hovering a rift plot adds the tier timing to its Status line.
[/list]

[b]Also[/b]
[list]
[*]Choose a Servant shows time left on Away on Hunt and recuperating entries.
[*]Pickup text shows your bag total, e.g. +5 Bone (1234).
[*]The Servant Hunts help box on the map is hidden by default; set [i]no_map_tip = false[/i] in the config to show it.
[/list]

[b]Install[/b]
Requires V Rising 1.1 and BepInExPack V Rising 1.733.2. Close the game, copy HuntsAndRifts.dll into BepInEx\plugins, start the game.
```

**Credits field:** none required (original code).

## Announcement blurb (Discord / changelog top)

HuntsAndRifts 1.0.0 (formerly HuntClock): client-side V Rising UI for servant hunts and Rift Incursions. Hunt cards with timers, power and specializations, click to focus the map, both Rift tiers with their own timer bars, tier timing on rift tooltips, and bag totals on pickups. Client only, no server install.
