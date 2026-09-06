# Specialty icons missing after an early lookup

The client log records Dunley Farmlands Hunter (81754057) in both perk sets for
the reported servants, including Corey and Lewie. Earlier session evidence also
resolved that ID to Stunlock_Icon_ServantPerk_Zone_Dunley. The user screenshots
show the region icon in vanilla but absent from the custom hunt and servant UI.

Source inspection found three recovery failures: the shared lookup cached a null
sprite indefinitely; servant rows retried only when perk IDs changed; tooltips
skipped rebuilding when the mission and width stayed the same.

The shared lookup now caches successful sprites only, with failed lookups retried
at most twice per second per perk. Rows retry unresolved icon slots. Tooltips
retry unresolved specialties even while hovering the same hunt. Hunt headers
reserve slots from the perk count rather than the number of loaded sprites.
Retry state clears alongside the world/session icon cache.

Validation: Release compilation passed. Visual confirmation of Dunley icons in
the tooltip and servant rows remains pending installation and an in-game check.
