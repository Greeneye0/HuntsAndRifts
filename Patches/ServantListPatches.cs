using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using ProjectM.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HuntsAndRifts;

[HarmonyPatch]
internal static class ServantListPatches
{
    private static readonly Regex ClockSuffix = new(
        @"\s+\d+h(?: \d+m)?$|\s+\d+m$|\s+\d+s$|<color=[^>]*>.*?</color>|</?size[^>]*>",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private const string DestColor = "#6CE38A";
    private static readonly Dictionary<IntPtr, TrackedRow> Tracked = new();
    // Row (ServantListItem) pointer -> servant name, for click-to-centre on hunt rows.
    private static readonly Dictionary<IntPtr, string> HuntRows = new();
    // Every row RefreshData has touched (any status), for grouping rows by hunt.
    private static readonly Dictionary<IntPtr, TrackedRow> AllRows = new();
    private static int _lastCenterFrame = -1;
    private static bool _mouseWasDown;
    private static bool _loggedNoMouse;
    // Two alternating translucent tints for hunt groups; the hunt header row uses the same hue
    // a step darker (see RegroupIfDue) so it reads as the group's title.
    // Card (outer) colours alternate per hunt; servant panels inside a card use the lighter
    // inset colour of the same hue. Section cards (Available / Injured-Dead) are neutral.
    // In-game palette: the deep navy of the Servants title bar and a dark blood red, each with
    // a lighter inset step for the servant panels. Sections use the panel charcoal.
    private static readonly Color[] GroupPalette =
    {
        new Color(0.07f, 0.11f, 0.19f, 0.92f), // navy card
        new Color(0.21f, 0.08f, 0.09f, 0.92f), // blood-red card
    };
    private static readonly Color[] InsetPalette =
    {
        new Color(0.16f, 0.22f, 0.34f, 0.88f), // navy inset
        new Color(0.36f, 0.16f, 0.17f, 0.88f), // blood-red inset
    };
    private static readonly Color SectionCard = new Color(0.12f, 0.12f, 0.14f, 0.92f);
    private static readonly Color SectionInset = new Color(0.24f, 0.24f, 0.27f, 0.85f);
    private const float CardInset = 5f;
    // Horizontal margin between the panel edge and the cards (matches the title's inset).
    private const float ListMargin = 12f;
    // The grid rect extends ~8 px past the visible panel on the right, so the right margin is larger.
    private const float ListMarginRight = 20f;
    private static float _nextRegroup;
    private static bool _loggedMultiParent;
    private static bool _loggedRegroupError;
    private static bool _loggedLayoutError;
    private static bool _loggedRegroup;

    private struct TrackedRow
    {
        public ServantListItem Item;
        public string Name;
        public ServantListItem.Status Status;
    }

    [HarmonyPatch(typeof(ServantListItem), nameof(ServantListItem.RefreshData))]
    [HarmonyPostfix]
    private static void RefreshDataPostfix(ServantListItem __0, ServantListItem.Data __1)
    {
        try
        {
            var item = __0;
            if (item == null)
                return;
            var statusText = item.CurrentStatus;
            if (statusText == null)
                return;

            var data = __1;
            var status = data.Status;
            var name = data.ServantName;
            var ptr = statusText.Pointer;
            if (AllRows.TryGetValue(item.Pointer, out var prev) && prev.Name != name)
                _rowSwaps++;
            _refreshCalls++;
            AllRows[item.Pointer] = new TrackedRow { Item = item, Name = name, Status = status };
            // Use the same hunt context as the periodic pass; otherwise non-matching icons flip
            // white on every vanilla refresh and dim again half a second later (flicker).
            RowHuntPerks.TryGetValue(item.Pointer, out var perksForRow);
            RowDecor.Apply(item, name, perksForRow);

            if (status == ServantListItem.Status.OnAHunt || status == ServantListItem.Status.Injured)
            {
                Tracked[ptr] = new TrackedRow
                {
                    Item = item,
                    Name = name,
                    Status = status
                };
                if (status == ServantListItem.Status.OnAHunt)
                    HuntRows[item.Pointer] = name;
                else
                    HuntRows.Remove(item.Pointer);
                ApplyClock(statusText, name, status);
            }
            else
            {
                Tracked.Remove(ptr);
                HuntRows.Remove(item.Pointer);
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts RefreshData postfix: " + ex.Message);
        }
    }

    [HarmonyPatch(typeof(LocalizedText), nameof(LocalizedText.LateUpdate))]
    [HarmonyPostfix]
    private static void LocalizedTextLateUpdatePostfix(LocalizedText __instance)
    {
        try
        {
            if (Tracked.Count == 0 || __instance == null)
                return;
            if (!Tracked.TryGetValue(__instance.Pointer, out var row))
                return;
            if (row.Item == null)
            {
                Tracked.Remove(__instance.Pointer);
                return;
            }
            ApplyClock(__instance, row.Name, row.Status);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts LateUpdate postfix: " + ex.Message);
        }
    }

    [HarmonyPatch(typeof(MapMenuMapper), "OnStopRunning")]
    [HarmonyPostfix]
    internal static void MapMenuStopPostfix()
    {
        RestoreLayout();
        Tracked.Clear();
        HuntRows.Clear();
        AllRows.Clear();
        RowHuntPerks.Clear();
        RowDecor.Clear();
        HuntHeaders.Clear();
        TooltipDecor.Clear();
        UiTextState.Restore();
        RestoreLayout();
    }

    // Left-clicking an Away on Hunt row centres the world map on that hunt's zone.
    // Vanilla wires no click delegates on ServantListItem rows, so this only adds behaviour.
    [HarmonyPatch(typeof(GridSelectionEntry), nameof(GridSelectionEntry.OnPointerClick))]
    [HarmonyPostfix]
    private static void EntryClickPostfix(GridSelectionEntry __instance, PointerEventData eventData)
    {
        try
        {
            if (__instance == null || eventData == null)
                return;
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            if (HuntHeaders.TryGetByEntry(__instance.Pointer, out var header))
            {
                if (!string.IsNullOrEmpty(header.FirstServant))
                {
                    Plugin.Logger?.LogInfo("HuntsAndRifts: click on hunt card '" + header.Dest + "'.");
                    CenterOnServant(header.FirstServant);
                }
                return;
            }
            if (HuntRows.Count == 0)
                return;
            if (!HuntRows.TryGetValue(__instance.Pointer, out var name))
                return;
            Plugin.Logger?.LogInfo("HuntsAndRifts: click on servant row '" + name + "'.");
            CenterOnServant(name);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts row click postfix: " + ex.Message);
        }
    }

    /// <summary>Called from the MapMenuMapper.OnUpdate postfix while the map is open.</summary>
    internal static void Tick()
    {
        PollRowClicks();
        RegroupIfDue();
        DecorateIfDue();
    }

    private static float _nextDecor;
    private static int _refreshCalls;
    private static int _rowSwaps;
    private static float _nextRateLog;

    // Perk info arrives from the server after the list is first drawn, and the name text is
    // re-set by vanilla on every refresh, so re-apply the decoration a few times a second.
    private static void DecorateIfDue()
    {
        var now = Time.unscaledTime;
        if (now < _nextDecor)
            return;
        _nextDecor = now + 0.5f;
        if (now >= _nextRateLog)
        {
            if (_nextRateLog > 0f && _refreshCalls > 0)
                Plugin.Logger?.LogInfo("HuntsAndRifts: " + _refreshCalls + " row refreshes / " + _rowSwaps + " name swaps in the last 5 s ("
                    + AllRows.Count + " rows).");
            _nextRateLog = now + 5f;
            _refreshCalls = 0;
            _rowSwaps = 0;
        }
        if (AllRows.Count == 0)
            return;
        try
        {
            foreach (var kv in AllRows)
            {
                var row = kv.Value;
                if (row.Item == null)
                    continue;
                try
                {
                    if (row.Item.gameObject == null || !row.Item.gameObject.activeSelf)
                        continue;
                }
                catch
                {
                    continue;
                }
                RowHuntPerks.TryGetValue(kv.Key, out var huntPerks);
                RowDecor.Apply(row.Item, row.Name, huntPerks);
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts decorate: " + ex.Message);
        }
    }

    private static void CenterOnServant(string name)
    {
        if (_lastCenterFrame == Time.frameCount)
            return;
        if (!HuntReader.TryGetHuntPos(name, out var worldXZ))
        {
            Plugin.Logger?.LogInfo("HuntsAndRifts: clicked hunt row " + name + " but no zone position is known.");
            return;
        }
        _lastCenterFrame = Time.frameCount;
        HuntMapFocus.CenterOn(worldXZ);
    }

    // The pointer raycast may land on the servant icon, the name, or the status text rather
    // than the row itself, so the row's OnPointerClick is not always reached. Independently
    // of the event system: on left mouse press, hit-test every hunt row's icon/name/status
    // rects with the screen point and centre on the first hit.
    private static void PollRowClicks()
    {
        if (HuntRows.Count == 0)
            return;
        // wasPressedThisFrame never fired from this update (the map system runs before the
        // input system's frame flip), so detect the press edge ourselves from isPressed.
        Vector2 screen;
        try
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
            {
                if (!_loggedNoMouse)
                {
                    _loggedNoMouse = true;
                    Plugin.Logger?.LogInfo("HuntsAndRifts: InputSystem Mouse.current is null; relying on UI click handlers.");
                }
                return;
            }
            var pressed = mouse.leftButton.isPressed;
            var edge = pressed && !_mouseWasDown;
            _mouseWasDown = pressed;
            if (!edge)
                return;
            screen = mouse.position.value;
        }
        catch
        {
            return;
        }

        foreach (var h in HuntHeaders.All)
        {
            try
            {
                if (h.Root == null || !h.Root.activeInHierarchy || string.IsNullOrEmpty(h.FirstServant))
                    continue;
                if (Contains(h.Rect, screen))
                {
                    Plugin.Logger?.LogInfo("HuntsAndRifts: click on hunt header '" + h.Dest + "'.");
                    CenterOnServant(h.FirstServant);
                    return;
                }
            }
            catch
            {
            }
        }

        foreach (var kv in HuntRows)
        {
            if (!AllRows.TryGetValue(kv.Key, out var row) || row.Item == null)
                continue;
            ServantListItem item;
            try
            {
                item = row.Item;
                if (item.gameObject == null || !item.gameObject.activeInHierarchy)
                    continue;
            }
            catch
            {
                continue;
            }
            if (RowContains(item, screen))
            {
                Plugin.Logger?.LogInfo("HuntsAndRifts: click on servant row '" + kv.Value + "'.");
                CenterOnServant(kv.Value);
                return;
            }
        }
    }

    private static bool RowContains(ServantListItem item, Vector2 screen)
    {
        try
        {
            if (Contains(item.GetComponent<RectTransform>(), screen)) return true;
            var name = item.ServantName;
            if (name != null && Contains(name.rectTransform, screen)) return true;
            var status = item.CurrentStatus;
            if (status != null && status.Text != null && Contains(status.Text.rectTransform, screen)) return true;
            var icon = item.ServantIcon;
            if (icon != null && Contains(icon.rectTransform, screen)) return true;
        }
        catch
        {
        }
        return false;
    }

    private static bool Contains(RectTransform rt, Vector2 screen)
    {
        if (rt == null)
            return false;
        Camera cam = null;
        try
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera;
        }
        catch
        {
        }
        return RectTransformUtility.RectangleContainsScreenPoint(rt, screen, cam);
    }

    // Hunt perk GUID hashes per row (servants on a hunt), for icon highlighting.
    private static readonly Dictionary<IntPtr, HashSet<int>> RowHuntPerks = new();

    private sealed class Group
    {
        public string Dest;
        public double Remaining = double.MaxValue;
        public readonly List<(ServantListItem item, int sibling, string name)> Rows = new();
        public readonly HashSet<int> ServantPerks = new();
    }

    // Layout, in order:
    //   [hunt header: name, timer, favoured perks (grey when unmet), Power]
    //   servant rows on that hunt (soonest return first)
    //   ... next hunt (alternating background tint) ...
    //   then every other servant (available, injured, dead) in vanilla order, untinted.
    // Rows are the vanilla objects; only sibling order, a tint Image, and header objects are
    // added. GridLayoutGroup ignores inactive pooled rows, so active rows and headers are
    // simply assigned sibling indices 0..n-1.
    private static void RegroupIfDue()
    {
        var now = Time.unscaledTime;
        if (now < _nextRegroup)
            return;
        _nextRegroup = now + 0.25f;
        if (AllRows.Count == 0)
            return;
        try
        {
            // Only rows that are actually visible (activeInHierarchy: pooled rows from another
            // throne's list stay activeSelf inside a hidden container) and that share the list
            // container holding the most live rows.
            var candidates = new List<(ServantListItem item, int sibling, string name, ServantListItem.Status status, Transform parent)>(AllRows.Count);
            var perParent = new Dictionary<IntPtr, int>();
            foreach (var kv in AllRows)
            {
                var row = kv.Value;
                var item = row.Item;
                if (item == null)
                    continue;
                GameObject go;
                try { go = item.gameObject; } catch { continue; }
                if (go == null || !go.activeInHierarchy)
                    continue;
                var t = item.transform;
                var par = ServantScroll.OriginalParent(t.parent);
                if (par == null)
                    continue;
                candidates.Add((item, t.GetSiblingIndex(), row.Name, row.Status, par));
                perParent[par.Pointer] = perParent.TryGetValue(par.Pointer, out var n) ? n + 1 : 1;
            }
            Transform parent = null;
            var best = 0;
            foreach (var c in candidates)
            {
                if (perParent[c.parent.Pointer] > best)
                {
                    best = perParent[c.parent.Pointer];
                    parent = c.parent;
                }
            }
            var active = new List<(ServantListItem item, int sibling, string name, ServantListItem.Status status)>(candidates.Count);
            TMPro.TextMeshProUGUI fontTemplate = null;
            RectTransform rowTemplate = null;
            foreach (var c in candidates)
            {
                if (parent == null || c.parent != parent)
                    continue;
                if (fontTemplate == null)
                {
                    try { fontTemplate = c.item.ServantName; } catch { }
                    try { rowTemplate = c.item.GetComponent<RectTransform>(); } catch { }
                }
                active.Add((c.item, c.sibling, c.name, c.status));
            }
            if (perParent.Count > 1 && !_loggedMultiParent)
            {
                _loggedMultiParent = true;
                Plugin.Logger?.LogInfo("HuntsAndRifts: servant rows found under " + perParent.Count + " containers; using the one with " + best + " rows.");
            }
            if (active.Count == 0 || parent == null)
            {
                HuntHeaders.HideExcept(null);
                return;
            }
            active.Sort((a, b) => a.sibling.CompareTo(b.sibling));
            var contentWidth = Mathf.Max(60f, MeasureRowWidth(active[0].item) - 14f);

            // Build hunt groups.
            var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
            var others = new List<(ServantListItem item, int sibling, string name, ServantListItem.Status status)>();
            RowHuntPerks.Clear();
            foreach (var a in active)
            {
                if (a.status != ServantListItem.Status.OnAHunt)
                {
                    others.Add(a);
                    continue;
                }
                if (!HuntReader.TryGetHuntDest(a.name, out var dest) || string.IsNullOrEmpty(dest))
                    dest = "Hunt";
                if (!groups.TryGetValue(dest, out var g))
                {
                    g = new Group { Dest = dest };
                    groups[dest] = g;
                }
                HuntReader.TryGetHuntRemaining(a.name, out var rem);
                if (rem < g.Remaining)
                    g.Remaining = rem;
                g.Rows.Add((a.item, a.sibling, a.name));
                if (ServantInfoReader.TryGetInfo(a.name, out var info))
                {
                    if (info.Perk0.GuidHash != 0) g.ServantPerks.Add(info.Perk0.GuidHash);
                    if (info.Perk1.GuidHash != 0) g.ServantPerks.Add(info.Perk1.GuidHash);
                }
            }
            var orderedGroups = new List<Group>(groups.Values);
            orderedGroups.Sort((x, y) =>
            {
                var c = x.Remaining.CompareTo(y.Remaining);
                return c != 0 ? c : string.CompareOrdinal(x.Dest, y.Dest);
            });

            // Final sibling order.
            var order = new List<Transform>(active.Count + orderedGroups.Count);
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (var gi = 0; gi < orderedGroups.Count; gi++)
            {
                var g = orderedGroups[gi];
                var tint = InsetPalette[gi % InsetPalette.Length];
                var headerTint = GroupPalette[gi % GroupPalette.Length];
                g.Rows.Sort((x, y) =>
                {
                    HuntReader.TryGetHuntRemaining(x.name, out var xr);
                    HuntReader.TryGetHuntRemaining(y.name, out var yr);
                    var c = xr.CompareTo(yr);
                    return c != 0 ? c : x.sibling.CompareTo(y.sibling);
                });

                MissionInfoReader.Info info = null;
                var mission = default(Stunlock.Core.PrefabGUID);
                if (HuntReader.TryGetMissionForZoneName(g.Dest, out mission))
                    info = MissionInfoReader.Get(mission);
                HashSet<int> huntPerks = null;
                if (info != null && info.Perks.Count > 0)
                {
                    huntPerks = new HashSet<int>();
                    foreach (var p in info.Perks)
                        huntPerks.Add(p.GuidHash);
                }

                var header = HuntHeaders.Get(g.Dest, parent, fontTemplate, rowTemplate, contentWidth);
                if (header != null)
                {
                    HuntHeaders.Fill(header, g.Remaining, mission, info, g.ServantPerks, headerTint, g.Rows[0].name);
                    order.Add(header.Root.transform);
                    used.Add(g.Dest);
                }
                foreach (var r in g.Rows)
                {
                    RowDecor.SetGroupTint(r.item, tint, contentWidth - 2f * CardInset, 0f);
                    if (huntPerks != null)
                        RowHuntPerks[r.item.Pointer] = huntPerks;
                    RowDecor.Apply(r.item, r.name, huntPerks);
                    order.Add(r.item.transform);
                }
            }
            // Everyone not hunting: Available first (incl. "items in inventory"), then Unavailable
            // (injured, dead), each under a plain section header using the game's own labels.
            var available = new List<(ServantListItem item, int sibling, string name, ServantListItem.Status status)>();
            var unavailable = new List<(ServantListItem item, int sibling, string name, ServantListItem.Status status)>();
            var unresolvedHunts = new List<(ServantListItem item, int sibling, string name, ServantListItem.Status status)>();
            foreach (var o in others)
            {
                if (o.status == ServantListItem.Status.Dead || o.status == ServantListItem.Status.Injured)
                    unavailable.Add(o);
                else if (o.status == ServantListItem.Status.OnAHunt)
                    unresolvedHunts.Add(o);
                else
                    available.Add(o);
            }
            var sectionTint = SectionCard;
            var template = active[0].item;
            if (unresolvedHunts.Count > 0)
            {
                const string section = "__HuntsAndRifts_UnresolvedHunts";
                var h = HuntHeaders.Get(section, parent, fontTemplate, rowTemplate, contentWidth);
                if (h != null)
                {
                    var title = "Away on Hunt";
                    try { title = Stunlock.Localization.Localization.Get(template.OnAHunt, false); } catch { }
                    HuntHeaders.FillSection(h, title, sectionTint);
                    order.Add(h.Root.transform);
                    used.Add(section);
                }
                foreach (var o in unresolvedHunts)
                {
                    RowDecor.SetGroupTint(o.item, SectionInset, contentWidth - 2f * CardInset, 0f);
                    order.Add(o.item.transform);
                }
            }
            if (available.Count > 0)
            {
                var h = HuntHeaders.Get(SectionAvailable, parent, fontTemplate, rowTemplate, contentWidth);
                if (h != null)
                {
                    HuntHeaders.FillSection(h, SectionLabel(template, true), sectionTint);
                    order.Add(h.Root.transform);
                    used.Add(SectionAvailable);
                }
                foreach (var o in available)
                {
                    RowDecor.SetGroupTint(o.item, SectionInset, contentWidth - 2f * CardInset, 0f);
                    order.Add(o.item.transform);
                }
            }
            if (unavailable.Count > 0)
            {
                var h = HuntHeaders.Get(SectionUnavailable, parent, fontTemplate, rowTemplate, contentWidth);
                if (h != null)
                {
                    HuntHeaders.FillSection(h, SectionLabel(template, false), sectionTint);
                    order.Add(h.Root.transform);
                    used.Add(SectionUnavailable);
                }
                foreach (var o in unavailable)
                {
                    RowDecor.SetGroupTint(o.item, SectionInset, contentWidth - 2f * CardInset, 0f);
                    order.Add(o.item.transform);
                }
            }
            HuntHeaders.HideExcept(used);
            HuntHeaders.SweepOrphans(parent, used);

            var changed = false;
            for (var i = 0; i < order.Count; i++)
            {
                if (order[i].GetSiblingIndex() != i)
                {
                    changed = true;
                    break;
                }
            }
            if (changed)
            {
                for (var i = 0; i < order.Count; i++)
                    order[i].SetSiblingIndex(i);
            }
            ManualLayout(parent, order, contentWidth);
            if (!changed)
                return;
            if (!_loggedRegroup)
            {
                _loggedRegroup = true;
                Plugin.Logger?.LogInfo("HuntsAndRifts: grouped " + active.Count + " servant rows into "
                    + orderedGroups.Count + " hunts (+" + others.Count + " other).");
            }
        }
        catch (Exception ex)
        {
            if (!_loggedRegroupError)
            {
                _loggedRegroupError = true;
                Plugin.Logger?.LogInfo("HuntsAndRifts regroup failed: " + ex);
            }
        }
    }

    private const string SectionAvailable = "\u00a7available";
    private const string SectionUnavailable = "\u00a7unavailable";
    private static string _labelAvailable;
    private static string _labelUnavailable;

    // Section labels from the game's own localization keys on the row prefab, so they follow
    // the client language. Falls back to English if a key does not resolve.
    private static string SectionLabel(ServantListItem template, bool available)
    {
        if (available && _labelAvailable != null) return _labelAvailable;
        if (!available && _labelUnavailable != null) return _labelUnavailable;
        string Loc(Stunlock.Localization.LocalizationKey key, string fallback)
        {
            try
            {
                var t = Stunlock.Localization.Localization.Get(key, false);
                if (!string.IsNullOrWhiteSpace(t) && t.IndexOf("not found", StringComparison.OrdinalIgnoreCase) < 0)
                    return t.Trim();
            }
            catch
            {
            }
            return fallback;
        }
        try
        {
            if (available)
                _labelAvailable = template != null ? Loc(template.Available, "Available") : "Available";
            else
            {
                var injured = template != null ? Loc(template.Injured, "Injured") : "Injured";
                var dead = template != null ? Loc(template.Dead, "Dead") : "Dead";
                _labelUnavailable = injured + " / " + dead;
            }
        }
        catch
        {
            if (available) _labelAvailable = "Available"; else _labelUnavailable = "Injured / Dead";
        }
        return available ? _labelAvailable : _labelUnavailable;
    }

    private static float _measuredWidth;
    private static UnityEngine.UI.GridLayoutGroup _grid;
    private static UnityEngine.UI.ContentSizeFitter _fitter;
    private static bool _gridWasEnabled;
    private static bool _fitterWasEnabled;
    private static bool _layoutTaken;
    private static bool _loggedLayout;
    private static bool _loggedViewport;
    private static UnityEngine.UI.LayoutElement _contentElement;
    private static LayoutSnapshot _layoutSnapshot;
    private static RectSnapshot _listRect;
    private static Transform _layoutParent;
    private const float RowHeight = 48f;
    private const float RowSpacing = 2f;
    private const float HeaderSpacingBefore = 6f;

    // The vanilla GridLayoutGroup gives every child the same cell, so a header can never be
    // shorter than a servant row. While the map is open we disable the grid (and the fitter that
    // reads it) and stack rows/headers ourselves: compact headers, tighter rows, and the content
    // height written so a long roster can still scroll. Both components are re-enabled when the
    // map closes.
    private static void ManualLayout(Transform parent, List<Transform> order, float contentWidth)
    {
        try
        {
            var parentRt = parent as RectTransform ?? parent.GetComponent<RectTransform>();
            if (parentRt == null)
                return;
            if (_layoutParent == null || _layoutParent != parent)
            {
                RestoreLayout();
                _layoutParent = parent;
                _listRect = new RectSnapshot(parentRt);
                _layoutSnapshot = new LayoutSnapshot(parent.gameObject);
                _grid = parent.GetComponent<UnityEngine.UI.GridLayoutGroup>();
                _fitter = parent.GetComponent<UnityEngine.UI.ContentSizeFitter>();
                _gridWasEnabled = _grid != null && _grid.enabled;
                _fitterWasEnabled = _fitter != null && _fitter.enabled;
                _layoutTaken = false;
            }
            if (!_layoutTaken)
            {
                if (_grid != null) _grid.enabled = false;
                // Keep the ContentSizeFitter: the panel background is sized from the content's
                // preferred height. With the grid off, a LayoutElement on the content supplies it.
                _layoutTaken = true;
                if (!_loggedLayout)
                {
                    _loggedLayout = true;
                    UnityEngine.UI.ScrollRect scroll = null;
                    try { scroll = parent.GetComponentInParent<UnityEngine.UI.ScrollRect>(); } catch { }
                    var scrollInfo = "none";
                    if (scroll != null)
                        scrollInfo = scroll.name + (scroll.verticalScrollbar != null ? " with scrollbar" : " no scrollbar");
                    Plugin.Logger?.LogInfo("HuntsAndRifts: manual servant layout (grid " + (_grid != null) + ", fitter " + (_fitter != null)
                        + ", scroll view " + scrollInfo + ", content " + parentRt.rect.size + ").");
                }
            }

            var cellW = _grid != null ? _grid.cellSize.x : 100f;
            ServantScroll.Prepare(parentRt, order);

            // The content rect is taller than the visible panel and the vanilla grid centred its
            // cells in it, so start at the visible viewport's top edge (plus the grid's padding)
            // rather than at the content rect's top.
            var x0 = 0f;
            var y = 0f;
            try
            {
                if (_grid != null)
                {
                    x0 = ListMargin;
                    y = Mathf.Max(0f, _grid.padding.top - 14f);
                }
                // Only the grid's own padding: the parent is the whole map panel, not a viewport.
                if (!_loggedViewport)
                {
                    _loggedViewport = true;
                    Plugin.Logger?.LogInfo("HuntsAndRifts: list starts at grid padding " + x0 + "/" + y + ".");
                }
            }
            catch
            {
            }
            HuntHeaders.Header card = null;
            var cardTop = 0f;
            void CloseCard(float bottom)
            {
                if (card != null)
                    HuntHeaders.SetCardHeight(card, bottom - cardTop + CardInset);
                card = null;
            }
            for (var i = 0; i < order.Count; i++)
            {
                var t = order[i];
                var rt = t as RectTransform ?? t.GetComponent<RectTransform>();
                if (rt == null)
                    continue;
                var isHeader = t.name.StartsWith(HuntHeaders.HeaderPrefix, StringComparison.Ordinal);
                var h = isHeader ? HuntHeaders.HeaderHeight : RowHeight;
                if (isHeader)
                {
                    if (i > 0)
                    {
                        CloseCard(y - RowSpacing);
                        y += HeaderSpacingBefore;
                    }
                    card = HuntHeaders.FindByRoot(t);
                    cardTop = y;
                }
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                var size = new Vector2(isHeader ? contentWidth : cellW, h);
                if (rt.sizeDelta != size)
                    rt.sizeDelta = size;
                // Servant rows sit inset inside the card; the row's own content starts at its
                // left edge, so shift the whole row by the inset.
                var pos = new Vector2(isHeader ? x0 : x0 + CardInset, -y);
                if (rt.anchoredPosition != pos)
                    rt.anchoredPosition = pos;
                y += h + RowSpacing;
            }
            CloseCard(y - RowSpacing);
            // Tell the fitter (and therefore the panel) how tall the list is now.
            var total = Mathf.Max(0f, y - RowSpacing) + (_grid != null ? _grid.padding.bottom : 0f) + 8f;
            total = ServantScroll.Size(total);
            var le = _layoutSnapshot.Element;
            _contentElement = le;
            if (!le.enabled) le.enabled = true;
            if (Mathf.Abs(le.preferredHeight - total) > 0.5f)
            {
                le.preferredHeight = total;
                le.minHeight = total;
            }
        }
        catch (Exception ex)
        {
            if (!_loggedLayoutError)
            {
                _loggedLayoutError = true;
                Plugin.Logger?.LogInfo("HuntsAndRifts manual layout failed: " + ex);
            }
        }
    }

    internal static void RestoreLayout()
    {
        ServantScroll.Reset();
        try
        {
            if (_layoutTaken)
            {
                if (_grid != null) _grid.enabled = _gridWasEnabled;
                if (_fitter != null) _fitter.enabled = _fitterWasEnabled;
                _layoutSnapshot?.Restore();
                _listRect?.Restore();
            }
        }
        catch
        {
        }
        _layoutTaken = false;
        _layoutSnapshot = null;
        _listRect = null;
        _layoutParent = null;
        _grid = null;
        _fitter = null;
    }

    // Vanilla rows sit in 100x60 grid cells but their name/status texts extend well past the
    // cell. Measure how far, in the row's local space, so headers and tints can span it.
    private static float MeasureRowWidth(ServantListItem item)
    {
        try
        {
            var rowRt = item.GetComponent<RectTransform>();
            if (rowRt == null)
                return _measuredWidth;
            var corners = new Vector3[4];
            var maxX = float.NegativeInfinity;
            void Acc(RectTransform rt)
            {
                if (rt == null)
                    return;
                rt.GetWorldCorners(corners);
                for (var i = 0; i < 4; i++)
                {
                    var local = rowRt.InverseTransformPoint(corners[i]);
                    if (local.x > maxX)
                        maxX = local.x;
                }
            }
            try { Acc(item.ServantName != null ? item.ServantName.rectTransform : null); } catch { }
            try { Acc(item.CurrentStatus != null && item.CurrentStatus.Text != null ? item.CurrentStatus.Text.rectTransform : null); } catch { }
            try { Acc(item.ServantIcon != null ? item.ServantIcon.rectTransform : null); } catch { }
            // rowRt local x is relative to its pivot; convert to distance from the left edge.
            var leftEdge = -rowRt.pivot.x * rowRt.rect.width;
            var width = float.IsInfinity(maxX) ? 0f : maxX - leftEdge;
            // The row's child rects are small (their text overflows without wrapping), so the
            // reliable span is the grid container itself: use its width when it is wider.
            try
            {
                var gridRt = rowRt.parent as RectTransform;
                if (gridRt == null && rowRt.parent != null)
                    gridRt = rowRt.parent.GetComponent<RectTransform>();
                if (gridRt != null)
                {
                    // The grid rect is the visible panel. Its padding is asymmetric (30 left, ~0
                    // right) and does not match the panel's own margins, so use a symmetric
                    // margin instead of the padding.
                    var gw = gridRt.rect.width - ListMargin - ListMarginRight;
                    if (gw > width)
                        width = gw;
                }
            }
            catch
            {
            }
            // Last resort: the vanilla panel is ~260 px of row content at 1080p.
            if (width < 150f || width >= 2000f)
                width = 240f;
            if (Mathf.Abs(width - _measuredWidth) > 0.5f)
            {
                _measuredWidth = width;
                Plugin.Logger?.LogInfo("HuntsAndRifts: servant row content width " + width.ToString("0")
                    + " (children max x " + (float.IsInfinity(maxX) ? "none" : maxX.ToString("0")) + ").");
            }
            return _measuredWidth;
        }
        catch
        {
            return _measuredWidth;
        }
    }

    private static void ApplyClock(LocalizedText statusText, string servantName, ServantListItem.Status status)
    {
            if (statusText == null)
                return;
            UiTextState.Track(statusText.Text);

        double remaining;
        bool have;
        if (status == ServantListItem.Status.OnAHunt)
            have = HuntReader.TryGetHuntRemaining(servantName, out remaining);
        else if (status == ServantListItem.Status.Injured)
            have = HuntReader.TryGetInjuryRemaining(servantName, out remaining);
        else
            return;

        if (!have)
            return;

        var baseText = statusText.GetText();
        if (string.IsNullOrEmpty(baseText))
            return;
        baseText = ClockSuffix.Replace(baseText, string.Empty).TrimEnd();
        // Status line is the secondary line: smaller and not bold, so the servant name leads.
        var combined = "<size=85%>" + baseText + "  " + HuntReader.FormatRemaining(remaining) + "</size>";
        // The destination is on the hunt header row (1.2.16+), so the servant status line only
        // carries the timer.
        if (statusText.GetText() == combined)
            return;
        try
        {
            if (statusText.Text != null)
            {
                statusText.Text.richText = true;
                if (statusText.Text.fontStyle != TMPro.FontStyles.Normal)
                    statusText.Text.fontStyle = TMPro.FontStyles.Normal;
            }
        }
        catch
        {
        }
        statusText.ForceSet(combined);
    }
}
