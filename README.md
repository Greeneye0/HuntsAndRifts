# HuntsAndRifts

HuntsAndRifts adds servant hunt details, Rift timers, and inventory totals to V Rising's UI. It runs **on your game client only** and leaves gameplay rules unchanged.

## Servant hunts

- Each servant on a hunt now shows the time left, the hunt location, their specialization icons, and their hunt power (the number from **Choose a Servant**).
- Hunts are listed as color-coded cards, so you can see at a glance which servants are on which hunt. A **green ring** marks a specialization the hunt favors.
- Click a hunt card, or a servant's name, timer, or location, to center the map on that hunt.
- Hover over a hunt on the map to see its time left, required power, and favored specializations, plus the power and specializations of the servants assigned to it.
- Servants not on a hunt are listed under **Available** and **Injured / Dead**. Long rosters scroll.

Details can take a moment to arrive from the game. If a name matches multiple servants and the row cannot be identified uniquely, HuntsAndRifts keeps its vanilla status and omits the added details rather than assigning another servant's information.

Want your hunts to repeat on their own? Pair this with [Satisvampory](https://thunderstore.io/c/v-rising/p/Team_GreenEye/Satisvampory/), a server-side mod from the same team that auto-repeats servant hunts and more.

## Rift Incursions

The map card shows a separate timer row for each Mortium tier.

| Example | Meaning |
| --- | --- |
| `T2 (80+) active: 12m 5s` | Time remaining for the active tier. |
| `T1 (57+) next in 17m 53s` | Countdown to an observed upcoming event. |
| `T2 (80+) awaiting schedule` | The client has no confirmed upcoming time for this tier. |

HuntsAndRifts uses event timestamps available to the client. It does not guess the other tier's schedule. Active bars drain; waiting bars fill over the wait observed by HuntsAndRifts. If you join partway through a wait, its bar starts from that observation. An unavailable schedule has an empty bar.

Hovering over a Rift on the map shows how much time is left. A tooltip may say **active elsewhere**: the tier is running, but the hovered plot is inactive.

## Pickup totals

Pickup text includes your bag total. For example, `+5 Bone (1234)` means you picked up five Bone and now carry 1,234.

## Install

Requires V Rising 1.1 and [BepInExPack V Rising 1.733.2](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/).

1. Close V Rising.
2. Copy `HuntsAndRifts.dll` into your game's `BepInEx\plugins` folder, replacing the previous copy when updating. (Or install with r2modman / Thunderstore Mod Manager.)
3. Start the game.

## Show the map tip

**Default: `no_map_tip = true` — the Servant Hunts help box is hidden.**

To bring the help box back, close the game and open `BepInEx\config\fangly.HuntsAndRifts.cfg` (created on first run), then change the setting under `[Map]`:

```ini
[Map]
no_map_tip = false
```

Restart the game for the change to take effect. This only affects the Servant Hunts help box in the throne map view. It is a **config-only option**; there is no `.map_tip` chat command.

## Thanks

Thanks to **Lomack** and **Katen** for testing.

Source code and issues: [github.com/Greeneye0/HuntsAndRifts](https://github.com/Greeneye0/HuntsAndRifts). Licensed under AGPL-3.0.

See the [changelog](https://github.com/Greeneye0/HuntsAndRifts/blob/main/CHANGELOG.md) for release history.
