# Scroll regression: dim rows and flashing scrollbar

The first scrolling build ordered rows before they shared a parent. HuntHeaders.Get
also moved existing headers back to the original grid every refresh. Moving those
headers into scroll content appended their full-card background Images after the
servant rows, rendering the translucent backgrounds over the servant text and icons.

Headers now retain their scroll-content parent when its original grid matches.
Prepare assigns the final sibling order after all rows share that parent, ensuring
each card background precedes its servants.

Size also manually hid the scrollbar while ScrollRect's default Permanent visibility
could show it again during LateUpdate. ScrollRect now exclusively owns visibility
through AutoHide; Size no longer toggles the scrollbar object. The scrollbar track
uses the list's top padding so it starts below the title rather than beside it.

Validation: Release build passed (existing unused labelSrc warning). In-game checks
remain pending: readable servant rows, stable hidden bar for short lists, and usable
scrollbar for long lists without map click-through.
