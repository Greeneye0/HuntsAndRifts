# Duplicate map tier labels (2026-09-06)

The user's screenshot shows stray T1/T2 rows to the right of My Territories while
HuntClock's two timer rows are already visible in the Rift Incursions card.

The installed HuntClock DLL matched the pre-fix build by SHA256, and its legacy
IMGUI host was already disabled. The running client's BepInEx LogOutput.log
contained these lines (captured before the log rotates):

```text
[Info   :Progenitor] Ui: attach MapWarEventInfo requested by greeneye.progenitor.sample as progenitor-ui-greeneye.progenitor.sample-MapWarEventInfo
[Info   :Progenitor Sample] Sample rifts: client listening for event id 0x536D5266; rows refreshed every 15 frame(s). row sink = Ui.AttachToVanilla(MapWarEventInfo) as progenitor-ui-greeneye.progenitor.sample-MapWarEventInfo
[Info   :Progenitor] Ui: attached MapWarEventInfo parent=MapInfoLayout layout=ignored rows=2 source=first instance=-782798 plugin=greeneye.progenitor.sample
```

Inference from source and logs: the second display is Progenitor Sample's sibling
attachment (see Progenitor/docs/ui.md, "Sibling container, never under TextGroup"),
rather than HuntClock's retired overlay. RiftDisplayCompatibility targets that
exact owner-specific sibling and adds an owned transparent CanvasGroup while
HuntClock supplies timers. It restores the sample display when the map closes,
the plugin unloads, or takeover is unavailable. It does not change Progenitor's
code or sample data handling. The direct sibling lookup avoids scene-wide scans.

Validation: Release build passed, with the existing unused labelSrc warning in
RiftCardExtension.cs. Visual confirmation after installation remains pending.

The user subsequently confirmed the duplicate-label fix in game.

## Countdown corrections

The following session logged Minor active with 238 seconds remaining and Major
upcoming in 3238 seconds. The extra 3000 seconds came from the other tier's settings,
not a confirmed Major deadline. Source inspection also found three literal backspace
characters in the own-text regex; a reproduction failed to recognize HuntClock's
`T2 (80+) active: 11m 30s` line.

The replacement rejects HuntClock text, consumes each vanilla reading once, ages it
using unscaled time, and invalidates the reading when the network event identity or
phase changes. Independently observed tier countdowns are retained for overlap;
unobserved and expired next schedules are unavailable, not inferred from the other
tier. World changes clear observations. The unassociated machine-local server JSON
fallback is no longer consulted. State changes log timing source and seconds.

Both bars now use the corresponding track countdown. Unknown schedules have empty
bars; a wait first observed partway through its cycle starts at zero and fills over
that observed wait. Previous guessed/persisted intervals are not used. This does not
claim knowledge of time elapsed before observation.

Validation: 13 game-independent regression checks in HuntClock.TimerTests passed.
In-game transition validation of this countdown change remains pending.
