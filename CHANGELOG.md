# Changelog

Historical entries describe the behavior of each release. See [README.md](README.md) for current features and configuration.

## 1.0.0

First release under the name **HuntsAndRifts** (previously HuntClock 1.0.0 to 1.2.21, listed below for history).

- Servant hunts: grouped hunt cards with timers, power and specialization icons, matching-specialization rings, click-to-focus map, Available / Injured-Dead sections, scrolling for long rosters.
- Rift Incursions: both Mortium tiers on the map card with their own timer bars and tier symbols (overlapping tiers supported); tier timing on rift plot tooltips.
- Hunt zone tooltips: required power and favored specializations; occupied hunts show each servant's power and icons.
- Choose a Servant: time left on Away on Hunt and recuperating entries.
- Pickup floaters show the bag total.
- Config `[Map] no_map_tip` (default true) hides the Servant Hunts help box; set it to false to show the box again.

### Unreleased (rolled into 1.0.0)

- Added scrolling for long servant lists and stronger cleanup when closing the map.
- Prevented ambiguous servant names and stale castle responses from supplying another servant's details.
- Rift timers use event timestamps independently of the game's display language; missing schedules read "awaiting schedule."
- Active tier symbols appear beside their corresponding timer rows.
- Removed the duplicate Progenitor sample Rift display when HuntsAndRifts supplies the card.

### 1.2.21

- Rift plot tooltip: on an inactive plot a tier that is running elsewhere reads "active elsewhere, Xm left" instead of implying this plot is active.

### 1.2.20

- Servants list: no more ghost cards after visiting another castle (header objects are hidden on map close and swept each pass); only rows visible in the current list container are grouped.
- Rift Incursions card: second row label/timer/bar mirror the vanilla row (fonts copied from the card); level line reads "World Event"; the clock no longer reads back its own text (it aged the last vanilla value instead).
- Rift plot tooltip grows to fit both tier lines.
- New config `[Map] no_map_tip` (default false): hide the "Servant Hunts" help box in the map's servant view.

### 1.2.19

- Rift Incursions card: both tiers with two timer bars (active: time left; otherwise next in). The card grows by a row and the territories panel moves up with it. The separate IMGUI overlay is gone while the card is available.
- Rift plot tooltip: tier timing on the Status line ("T2 (80+) 12m left" when active; both tiers' countdowns when inactive).
- Choose a Servant: time left on Away on Hunt / recuperating entries.

### 1.2.18

- Matched specialisations are marked with a green ring (hunt cards and servant panels); icons are never dimmed.
- Servant names bold and larger; status line smaller, not bold.
- Cards inset evenly from both panel edges; the Servants panel now sizes itself to the list.
- Header cards use the game's click handler (clicks on the list no longer reach the map).
- Hunt tooltip matches the hovered zone entity, so empty hunts such as Hallowed Mountains show power and specialisations.
- Choose a Servant shows time left on Away on Hunt / recuperating entries.

### 1.2.17

- Servants list redesigned as hunt cards (navy / blood red) with servant panels inside; bold larger names, smaller status line; symmetric panel margins. HuntsAndRifts lays the list out itself while the map is open (vanilla grid restored on close).
- Clicks on the list no longer reach the map behind it; hunt cards are clickable and zoom the map to the hunt.
- Hunt tooltip matches the hovered zone entity (fixes e.g. "Hallowed Mountain" vs "Hallowed Mountains") and shows power + favoured specialisations for empty hunts too.
- Choose a Servant: time left on Away on Hunt / recuperating entries.

- Servants list: compact one-line hunt headers (name, timer, favoured icons, power number); HuntsAndRifts lays the list out itself while the map is open (the vanilla grid gives every child the same 60 px cell). Vanilla grid restored on close. No scroll view exists in the vanilla panel.
- Everyone not hunting is listed under **Available** and then **Injured / Dead** section headers, using the game's own localized labels.
- Coffin data (hunt, timer, zone, gear) is resolved by the servant's NetworkId from the throne response when available, with name matching as fallback. Two castles or two servants sharing a name no longer cross over.
- Servant-info response is polled 4x/s so power and icons appear as soon as the server answers.
- Icon flicker fix: the per-refresh pass now uses the same hunt context as the periodic pass.
- Hunt tooltip block is two compact lines with explicit height; matching icons are full colour, non-matching dim (no rings).

### 1.2.16

- **Servants list is organised by hunt.** Each active hunt gets a header row (hunt name, time left, the hunt's favoured specialisation icons greyed when no assigned servant has them, and `Power N`), followed by its servants. Groups alternate background tints; the header is a darker step of the same tint. Everyone not on a hunt is listed after, untinted. Clicking a header or any row in a group centres the map on that hunt.
- The number after a servant's name is now the **power** shown in Choose a Servant (hunt proficiency), not the coffin gear level. Specialisation icons that match the servant's current hunt glow green.
- **Click-to-centre zooms in first** when the map is zoomed out: vanilla clamps the pan so the map never leaves the window, which made centring a no-op at fit-to-screen zoom.
- **Hunt tooltip fix:** 1.2.15's Harmony patches on `MapTooltip.Show`/`ShowMission` are gone (those take generic buffer structs by value and broke the vanilla tooltip). The tooltip is now decorated from the map update tick by matching its title to the hunt zone.

### 1.2.15

- Clicking the servant **name, icon, timer or location** on an Away on Hunt row centres the map. A press hit-test against those rects runs alongside the row click, so it no longer matters which child the pointer raycast lands on.
- Servant rows are **grouped by hunt**: Away on Hunt rows first, servants on the same destination together, groups ordered by soonest return. Only sibling order changes.
- **Hunt tooltip:** hovering a hunt zone on the map adds a `Power N` line (the hunt's difficulty) with the hunt's favoured specialisation icons and names. When the hunt is occupied, each servant line in the tooltip shows that servant's gear score and specialisation icons.
- The T1/T2 **rift overlay is hidden** while the map is in the servant hunt view.
- Rows show the **gear score** after the name (`GS 84`, from the coffin's gear level) and the servant's two **specialisation icons** (e.g. Tracker, Dunley Farmlands Hunter) from the client's servant info response, using the vanilla perk sprites.

### 1.2.14

- Click-to-centre fix: `MapZoneData.CenterPosWS` is zero on the client for hunt zones, so 1.2.13 never had a position. The zone centre now falls back to the zone's `UiPolygonMesh` bounds, then the map region-label entity (`MapRegionNameComponent` + `Translation`), then the mean of the zone polygon vertices. All reads use the non-generic raw component accessors. The source used is logged once per zone.

### 1.2.13

- Hunt destination on Away on Hunt rows is green.
- Left-click an Away on Hunt row to centre the world map on that hunt's zone. Uses the same offset math as the vanilla "centre on player" key; zoom is kept. Display/navigation only, nothing is sent to the server.

### 1.2.12

- T1/T2 overlay uses the **whole rift card** (entry + bar + text) in IMGUI space and sits **under that box**. No more sliding to the right of the bar.

### 1.2.11

- T1/T2 overlay sits **below the whole rift card** (not on the Status/timer row).

### 1.2.10

- Rift overlay sits **under** the vanilla timer/bar instead of overlapping it.

### 1.2.9

- Away on Hunt rows also show the destination in smaller text (Ancient Village, Fishing Lake, …).

### 1.2.8

- Overlay sits lower next to the vanilla timer/bar.

### 1.2.7

- Active remaining matches the vanilla `Active:` timer (not UtcNow / full duration). Overlay sits a hair lower on that timer.

### 1.2.6

- Map overlay shows **both** T1 (57+) and T2 (80+). Vanilla still only networks the next/active rift; HuntsAndRifts derives the other tier from that live event plus `WarEventGameSettings` duration and interval (T2 after T1 ends, and the reverse).

### 1.2.5

- Active rift remaining matches the vanilla bar (fill × duration, plus MapMenuMapper server time). Fixes T1 showing ~6m ahead of Active.

### 1.2.3

- Overlay remaining time uses client `ServerTime` so T2 matches the vanilla Active bar (not local UTC / TEST).
- Missing tiers are omitted; TEST rows only if `IncludeTestTiers` is true.
- Panel docks left of the vanilla Rift Incursions widget (16:9 fallback on ultrawide instead of the far screen corner).

### 1.1.2

- Safe both-tiers map view: IMGUI overlay while `MapMenuMapper` is running. Queries every `WarEvent_NetworkedData` (and `WarEvent` if present) on `Client_0`. Shows only the tracks the client actually has.
- Does not clone `MapWarEventInfoEntry`, does not call `SetData`, does not patch Burst/jobs. Vanilla widget stays.

### 1.1.1

- **Hotfix:** disabled the world-map rift widget clone. 1.1.0 Harmony postfix on `MapMenuMapper.UpdateWarEventInfo` crashed on map open (`MarshalDirectiveException`: cannot marshal parameter #2, non-blittable generic — `SetData` / `TimeLocalizationKeys`). Throne hunt remaining and pickup floater totals unchanged.

### 1.1.0

- Remaining hunt time on the throne Servants list (same clock as 1.0.0).
- World map shows both Mortium rift tiers (T1 Minor / T2 Major, plus Primal if present) instead of one overwriting the other. **Yanked — crashed on map open; see 1.1.1.**
- Pickup floater (`+5 Bone`) also shows bag total, e.g. `+5 Bone (1234)`.

### 1.0.0

- First client build: remaining hunt time on Away on Hunt rows (and injured recovery when that value is already on the coffin).
