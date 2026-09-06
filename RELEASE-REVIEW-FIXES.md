# Review fixes: servant identity, scrolling, timer language, cleanup

## Changes

- Response records are indexed by their 12-byte network identity. Name-only UI
  lookups accept unique names only. The vanilla ServantListItem.Data exposes
  CurrentCondition, ServantName and Status, without a servant ID (local client
  dump.cs:387180). Ambiguous names retain vanilla status without guessed added
  details. HuntReader no longer falls back to a coffin selected by name.
- Empty, missing or invalid responses clear records. A managed prefix on
  ServantInfoEventSystem_Client.Refresh(Entity) clears records on throne changes
  and waits for the response to differ from the previous throne's snapshot.
  World changes invalidate response and perk caches. Missing record data also
  hides previously rendered icons and removes stale appended power.
- Long lists use an owned ScrollRect, clipped viewport and scrollbar, capped to
  60% of screen height in canvas units. Short lists retain their natural height.
  Native rows are returned to their saved parents and geometry during cleanup.
- Rift countdowns read the event's UTC ticks directly; localized UI strings are
  no longer timing input. This uses the existing verified UTC-tick finding in
  ../Progenitor-research/war-events.md section 2.4. It avoids language-specific
  units and UI rounding. Client/server wall-clock skew remains a client-only
  limitation; no server companion or clock synchronization was added.
- LayoutElement values, RectTransform dimensions and text presentation are saved
  and restored. Added cards, icons, row backgrounds, scroll UI and tooltip rows
  are destroyed on close/unload. Non-layout Rift card shifting is idempotent.
  The hidden map tip is restored when disabled/unloaded.

## Validation and remaining checks

Release compilation passed. The console regression suite passed 29 checks,
including duplicate-name ambiguity, replacement/empty records, missing IDs,
timestamp arithmetic under six cultures, and short/long/scaled viewport sizing.
These tests do not execute Unity's UI or Harmony hooks.

Live checks still needed: scroll/drag a long roster without map click-through;
switch between castles with duplicate names; close/reopen the map repeatedly;
verify tooltip reuse and a Rift phase transition. Package and version unchanged.
