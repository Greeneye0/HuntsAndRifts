using System;
using System.Collections.Generic;
using Stunlock.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HuntsAndRifts;

/// <summary>
/// Hunt header rows inserted into the throne Servants grid, one per active hunt:
/// "Hunt name  timer   [favoured perk icons, grey when no assigned servant has them]  Power N".
/// Headers are plain UI objects (background Image, two TMP texts, four icon Images) made
/// children of the same grid as the vanilla rows, so the GridLayoutGroup sizes them like rows.
/// </summary>
internal static class HuntHeaders
{
    private const float IconSize = 20f;
    private const float IconGap = 3f;
    private const float RightTextWidth = 40f;
    private const float Pad = 8f;
    internal const string HeaderPrefix = "HuntsAndRifts_Hunt_";

    internal sealed class Header
    {
        public GameObject Root;
        public RectTransform Rect;
        public Image Bg;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Right;
        public readonly Image[] Icons = new Image[4];
        public readonly Image[] Rings = new Image[4];
        public string Dest;
        public string FirstServant;
        public int Mission;
        public string LastTitle;
        public float Width;
        public int IconCount = 2;
        public float CardHeight;
        public IntPtr EntryPointer;
    }

    private static readonly Dictionary<IntPtr, Header> ByEntry = new();

    internal static bool TryGetByEntry(IntPtr entryPointer, out Header header)
    {
        return ByEntry.TryGetValue(entryPointer, out header) && header != null;
    }

    private static readonly Dictionary<IntPtr, Header> ByRoot = new();

    internal static Header FindByRoot(Transform root)
    {
        if (root == null)
            return null;
        return ByRoot.TryGetValue(root.Pointer, out var h) ? h : null;
    }

    /// <summary>Set the group card height (header line + servant panels + padding).</summary>
    internal static void SetCardHeight(Header h, float height)
    {
        if (h == null || h.Bg == null)
            return;
        if (Mathf.Abs(h.CardHeight - height) < 0.5f)
            return;
        h.CardHeight = height;
        var rt = h.Bg.rectTransform;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, Mathf.Max(HeaderHeight, height));
    }

    private static readonly Dictionary<string, Header> ByDest = new(StringComparer.Ordinal);
    private static bool _loggedOnce;

    internal static void Clear()
    {
        // Hide the objects too: the list container keeps them across map opens (and across
        // thrones); a later Get() re-finds by name only the ones it needs, so anything not
        // hidden here would linger as a ghost card at its old position.
        foreach (var kv in ByDest)
        {
            try
            {
                if (kv.Value.Root != null && kv.Value.Root.activeSelf)
                    kv.Value.Root.SetActive(false);
                if (kv.Value.Root != null) UnityEngine.Object.Destroy(kv.Value.Root);
            }
            catch
            {
            }
        }
        ByDest.Clear();
        ByRoot.Clear();
        ByEntry.Clear();
    }

    internal static IEnumerable<Header> All => ByDest.Values;

    internal static Header Get(string dest, Transform gridParent, TextMeshProUGUI fontTemplate, RectTransform rowTemplate, float contentWidth)
    {
        if (string.IsNullOrEmpty(dest) || gridParent == null)
            return null;
        if (ByDest.TryGetValue(dest, out var h) && h.Root != null)
        {
            if (ServantScroll.OriginalParent(h.Root.transform.parent) != gridParent)
                h.Root.transform.SetParent(gridParent, false);
            MatchRow(h, rowTemplate);
            Layout(h, contentWidth);
            return h;
        }
        try
        {
            h = Build(dest, gridParent, fontTemplate);
            MatchRow(h, rowTemplate);
            Layout(h, contentWidth);
            ByDest[dest] = h;
            ByRoot[h.Root.transform.Pointer] = h;
            if (!_loggedOnce)
            {
                _loggedOnce = true;
                var rowSize = rowTemplate != null ? rowTemplate.rect.size.ToString() : "?";
                string layout = "";
                try
                {
                    var g = gridParent.GetComponent<GridLayoutGroup>();
                    if (g != null) layout = "GridLayoutGroup cell " + g.cellSize;
                    else if (gridParent.GetComponent<VerticalLayoutGroup>() != null) layout = "VerticalLayoutGroup";
                }
                catch { }
                Plugin.Logger?.LogInfo("HuntsAndRifts: hunt header rows added to " + gridParent.name + " (row " + rowSize + ", " + layout
                    + ", content width " + contentWidth.ToString("0") + ").");
            }
            return h;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts header build: " + ex.Message);
            return null;
        }
    }

    /// <summary>Copy a vanilla row's rect/anchors/layout sizing so the grid treats the header like a row.</summary>
    private static void MatchRow(Header h, RectTransform row)
    {
        if (h == null || h.Rect == null || row == null)
            return;
        try
        {
            var rt = h.Rect;
            if (rt.anchorMin != row.anchorMin) rt.anchorMin = row.anchorMin;
            if (rt.anchorMax != row.anchorMax) rt.anchorMax = row.anchorMax;
            if (rt.pivot != row.pivot) rt.pivot = row.pivot;
            if (rt.sizeDelta != row.sizeDelta) rt.sizeDelta = row.sizeDelta;
            var size = row.rect.size;
            if (size.x > 1f && size.y > 1f)
            {
                var le = h.Root.GetComponent<LayoutElement>();
                if (le == null)
                    le = h.Root.AddComponent<LayoutElement>();
                LayoutElement src = null;
                try { src = row.GetComponent<LayoutElement>(); } catch { }
                var w = src != null && src.preferredWidth > 0f ? src.preferredWidth : size.x;
                var hgt = src != null && src.preferredHeight > 0f ? src.preferredHeight : size.y;
                if (le.preferredWidth != w) le.preferredWidth = w;
                if (le.preferredHeight != hgt) le.preferredHeight = hgt;
                if (le.minWidth != w) le.minWidth = w;
                if (le.minHeight != hgt) le.minHeight = hgt;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts header size: " + ex.Message);
        }
    }

    /// <summary>
    /// The vanilla rows overflow their 100x60 grid cell (their texts run far past the cell's
    /// right edge), so the header's children are anchored to the cell's left edge and laid out
    /// across the measured content width rather than the cell width.
    /// </summary>
    internal const float HeaderHeight = 24f;

    private static void Layout(Header h, float width)
    {
        if (h == null || width < 60f)
            return;
        try
        {
            if (Mathf.Abs(h.Width - width) < 0.5f)
                return;
            h.Width = width;
            // One compact line: "Name  timer" left, icons + Power right.
            if (h.Bg != null)
            {
                // Card: anchored to the header's top-left, height set by SetCardHeight so it
                // wraps the servant panels below the header line.
                var rt = h.Bg.rectTransform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(width, Mathf.Max(HeaderHeight, h.CardHeight));
                rt.anchoredPosition = Vector2.zero;
            }
            var rightW = RightTextWidth;
            var iconsW = Mathf.Max(1, h.IconCount) * (IconSize + IconGap);
            if (h.Title != null)
            {
                var rt = h.Title.rectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(Mathf.Max(40f, width - 2f * Pad - rightW - iconsW - 6f), 0f);
                rt.anchoredPosition = new Vector2(Pad, 0f);
            }
            if (h.Right != null)
            {
                var rt = h.Right.rectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(rightW, 0f);
                rt.anchoredPosition = new Vector2(width - Pad, 0f);
            }
            for (var i = 0; i < h.Icons.Length; i++)
            {
                var img = h.Icons[i];
                if (img == null)
                    continue;
                var x = width - Pad - rightW - 6f - i * (IconSize + IconGap);
                var rt = img.rectTransform;
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(IconSize, IconSize);
                rt.anchoredPosition = new Vector2(x, 0f);
                var ring = h.Rings[i];
                if (ring != null)
                {
                    var rr = ring.rectTransform;
                    rr.anchorMin = new Vector2(0f, 0.5f);
                    rr.anchorMax = new Vector2(0f, 0.5f);
                    rr.pivot = new Vector2(1f, 0.5f);
                    rr.sizeDelta = new Vector2(IconSize + 6f, IconSize + 6f);
                    rr.anchoredPosition = new Vector2(x + 3f, 0f);
                    ring.transform.SetSiblingIndex(img.transform.GetSiblingIndex());
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts header layout: " + ex.Message);
        }
    }

    private static Header Build(string dest, Transform gridParent, TextMeshProUGUI template)
    {
        var name = HeaderPrefix + dest;
        GameObject root = null;
        var existing = gridParent.Find(name);
        if (existing != null)
            root = existing.gameObject;
        if (root == null)
        {
            root = new GameObject(name);
            var rt = root.AddComponent<RectTransform>();
            rt.SetParent(gridParent, false);
            rt.localScale = Vector3.one;
        }
        var h = new Header { Root = root, Rect = root.GetComponent<RectTransform>(), Dest = dest };

        h.Bg = GetOrMake<Image>(root.transform, "Bg", go =>
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.raycastTarget = true; // swallow clicks; the header root's GridSelectionEntry gets them
            img.color = new Color(0f, 0f, 0f, 0.35f);
            return img;
        });

        // The vanilla rows receive clicks through GridSelectionEntry.OnPointerClick, which this mod
        // already postfixes. Give the header the same component so header clicks arrive the same
        // way (the mouse-poll path never sees a press in this game's update order).
        try
        {
            var entry = root.GetComponent<GridSelectionEntry>();
            if (entry == null)
                entry = root.AddComponent<GridSelectionEntry>();
            h.EntryPointer = entry.Pointer;
            ByEntry[h.EntryPointer] = h;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts header entry: " + ex.Message);
        }

        h.Title = GetOrMake<TextMeshProUGUI>(root.transform, "Title", go =>
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(Pad, 0f);
            rt.offsetMax = new Vector2(-(Pad + RightTextWidth + 4 * (IconSize + IconGap)), 0f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.richText = true;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            CopyFont(tmp, template);
            try { tmp.fontSize = tmp.fontSize * 0.95f; } catch { }
            return tmp;
        });

        h.Right = GetOrMake<TextMeshProUGUI>(root.transform, "Right", go =>
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.offsetMin = new Vector2(-(Pad + RightTextWidth), 0f);
            rt.offsetMax = new Vector2(-Pad, 0f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.richText = true;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.alignment = TextAlignmentOptions.MidlineRight;
            CopyFont(tmp, template);
            try { tmp.fontSize = tmp.fontSize * 0.95f; } catch { }
            return tmp;
        });

        for (var i = 0; i < h.Rings.Length; i++)
        {
            h.Rings[i] = GetOrMake<Image>(root.transform, "Ring" + i, go =>
            {
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(IconSize + 6f, IconSize + 6f);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                img.sprite = RingSprite.Get();
                img.preserveAspect = true;
                img.color = RowDecor.MatchRing;
                go.SetActive(false);
                return img;
            });
        }
        for (var i = 0; i < h.Icons.Length; i++)
        {
            var idx = i;
            h.Icons[i] = GetOrMake<Image>(root.transform, "Icon" + i, go =>
            {
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(IconSize, IconSize);
                rt.anchoredPosition = new Vector2(-(Pad + RightTextWidth + 6f + idx * (IconSize + IconGap)), 0f);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                img.preserveAspect = true;
                go.SetActive(false);
                return img;
            });
        }
        return h;
    }

    private static T GetOrMake<T>(Transform parent, string name, Func<GameObject, T> make) where T : Component
    {
        var existing = parent.Find(name);
        if (existing != null)
        {
            var have = existing.GetComponent<T>();
            if (have != null)
                return have;
        }
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.localScale = Vector3.one;
        return make(go);
    }

    private static void CopyFont(TextMeshProUGUI tmp, TMP_Text template)
    {
        try
        {
            if (template == null)
                return;
            tmp.font = template.font;
            tmp.fontSharedMaterial = template.fontSharedMaterial;
            tmp.fontSize = template.fontSize;
            tmp.color = template.color;
        }
        catch
        {
        }
    }

    /// <summary>Fill a header: title with timer, favoured perk icons (grey when unmet), difficulty.</summary>
    internal static void Fill(Header h, double remainingSeconds, PrefabGUID mission, MissionInfoReader.Info info,
        HashSet<int> groupPerks, Color tint, string firstServant)
    {
        if (h == null || h.Root == null)
            return;
        h.FirstServant = firstServant;
        if (!h.Root.activeSelf)
            h.Root.SetActive(true);
        if (h.Bg != null && h.Bg.color != tint)
            h.Bg.color = tint;

        var title = h.Dest;
        if (h.Title != null && h.LastTitle != title)
        {
            h.LastTitle = title;
            h.Title.text = title;
        }

        var perks = info?.Perks;
        // Reserve the actual perk slots, including icons still loading. Counting only
        // successful lookups squeezed the second slot into the power column.
        var count = perks?.Count ?? 0;
        if (count < 1) count = 1;
        if (count > h.Icons.Length) count = h.Icons.Length;
        if (count != h.IconCount)
        {
            h.IconCount = count;
            var w = h.Width;
            h.Width = -1f;
            Layout(h, w);
        }
        for (var i = 0; i < h.Icons.Length; i++)
        {
            var img = h.Icons[i];
            if (img == null)
                continue;
            if (perks != null && i < perks.Count && ServantInfoReader.TryGetPerk(perks[i], out var sprite, out _) && sprite != null)
            {
                if (img.sprite != sprite)
                    img.sprite = sprite;
                var met = groupPerks != null && groupPerks.Contains(perks[i].GuidHash);
                if (img.color != Color.white)
                    img.color = Color.white;
                if (!img.gameObject.activeSelf)
                    img.gameObject.SetActive(true);
                var ring = h.Rings[i];
                if (ring != null && ring.gameObject.activeSelf != met)
                    ring.gameObject.SetActive(met);
            }
            else
            {
                if (img.gameObject.activeSelf)
                    img.gameObject.SetActive(false);
                var ring = h.Rings[i];
                if (ring != null && ring.gameObject.activeSelf)
                    ring.gameObject.SetActive(false);
            }
        }

        if (h.Right != null)
        {
            var right = info != null ? "<color=#E6CF8F>" + info.Difficulty + "</color>" : "";
            if (h.Right.text != right)
                h.Right.text = right;
        }
        h.Mission = mission.GuidHash;
    }

    /// <summary>Plain section header: title only, no icons/power, not clickable.</summary>
    internal static void FillSection(Header h, string title, Color tint)
    {
        if (h == null || h.Root == null)
            return;
        h.FirstServant = null;
        h.Mission = 0;
        if (!h.Root.activeSelf)
            h.Root.SetActive(true);
        if (h.Bg != null && h.Bg.color != tint)
            h.Bg.color = tint;
        if (h.Title != null && h.LastTitle != title)
        {
            h.LastTitle = title;
            h.Title.text = title;
        }
        for (var i = 0; i < h.Icons.Length; i++)
        {
            var img = h.Icons[i];
            if (img != null && img.gameObject.activeSelf)
                img.gameObject.SetActive(false);
            var ring = h.Rings[i];
            if (ring != null && ring.gameObject.activeSelf)
                ring.gameObject.SetActive(false);
        }
        if (h.Right != null && h.Right.text != "")
            h.Right.text = "";
    }

    internal static void HideExcept(HashSet<string> used)
    {
        foreach (var kv in ByDest)
        {
            if (used != null && used.Contains(kv.Key))
                continue;
            try
            {
                if (kv.Value.Root != null && kv.Value.Root.activeSelf)
                    kv.Value.Root.SetActive(false);
            }
            catch
            {
            }
        }
    }

    /// <summary>Sweep the list container for header objects this registry does not know about
    /// (left over from a previous throne) and hide any that are not in use.</summary>
    internal static void SweepOrphans(Transform gridParent, HashSet<string> used)
    {
        if (gridParent == null)
            return;
        try
        {
            var count = gridParent.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = gridParent.GetChild(i);
                if (child == null)
                    continue;
                var name = child.name;
                if (!name.StartsWith(HeaderPrefix, StringComparison.Ordinal))
                    continue;
                var dest = name.Substring(HeaderPrefix.Length);
                if (used != null && used.Contains(dest))
                    continue;
                if (child.gameObject.activeSelf)
                    child.gameObject.SetActive(false);
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts header sweep: " + ex.Message);
        }
    }
}
