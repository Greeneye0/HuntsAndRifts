using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ProjectM.UI;
using Stunlock.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HuntsAndRifts;

/// <summary>
/// Decorates a servant name text (throne Servants row, or a name line in the map hunt
/// tooltip): appends the gear score to the text and adds the servant's two perk
/// (specialisation) icons right after the rendered name. Icons are plain UI Images parented
/// under the vanilla text; nothing vanilla is replaced.
/// </summary>
internal static class RowDecor
{
    private const string HolderName = "HuntsAndRifts_Perks";
    private const float IconGap = 3f;
    private const float NameGap = 8f;
    internal const string GearColor = "#C9B37E";
    internal static readonly Color MatchRing = new Color(0.45f, 0.95f, 0.50f, 0.95f);
    private static readonly Regex GearSuffix = new(
        @"\s*<size=[^>]*><color=[^>]*>(?:GS )?\d+</color></size>",
        RegexOptions.Compiled);
    private static readonly Regex NameTags = new(@"</?(?:b|u|size)[^>]*>", RegexOptions.Compiled);
    // Name emphasis: bold (default). Set to "<u>"/"</u>" for underline instead.
    private const string NameOpen = "<b><size=112%>";
    private const string NameClose = "</size></b>";
    private static readonly Dictionary<IntPtr, State> States = new();
    private static bool _loggedOnce;

    private sealed class State
    {
        public RectTransform Holder;
        public Image[] Icons = new Image[2];
        public Image[] Glows = new Image[2];
        public int Perk0;
        public int Perk1;
        public float IconSize;
        public string MeasuredText;
        public TextMeshProUGUI Text;
        public float MeasuredWidth;
    }

    internal static void Clear()
    {
        foreach (var state in States.Values)
        {
            if (state.Text != null) state.Text.text = BaseName(state.Text.text);
            if (state.Holder != null)
            {
                state.Holder.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(state.Holder.gameObject);
            }
        }
        foreach (var bg in Bgs.Values)
            if (bg != null) { bg.gameObject.SetActive(false); UnityEngine.Object.Destroy(bg.gameObject); }
        States.Clear();
        Bgs.Clear();
    }

    internal static void Apply(ServantListItem item, string servantName, HashSet<int> huntPerks = null)
    {
        if (item == null)
            return;
        try
        {
            ApplyToText(item.ServantName, servantName, 22f, huntPerks);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts row decor: " + ex.Message);
        }
    }

    private static bool _loggedPower;

    /// <summary>The servant's power as shown in Choose a Servant (ServantInfoEvent HuntProficiency).</summary>
    internal static bool TryGetPower(string servantName, out float power)
    {
        power = 0f;
        if (ServantInfoReader.TryGetInfo(servantName, out var info) && info.HuntProficiency > 0f)
        {
            power = info.HuntProficiency;
            if (!_loggedPower)
            {
                _loggedPower = true;
                HuntReader.TryGetGearLevel(servantName, out var gear);
                Plugin.Logger?.LogInfo("HuntsAndRifts power: " + servantName + " HuntProficiency=" + info.HuntProficiency
                    + " LootFactor=" + info.LootFactor + " coffin gear=" + gear);
            }
            return true;
        }
        return false;
    }

    private const string BgName = "HuntsAndRifts_Bg";
    private static readonly Dictionary<IntPtr, Image> Bgs = new();

    /// <summary>Tint a servant row's background for hunt grouping; null clears it.</summary>
    internal static void SetGroupTint(ServantListItem item, Color? color, float contentWidth = 0f, float inset = 0f)
    {
        if (item == null)
            return;
        try
        {
            var key = item.Pointer;
            if (!Bgs.TryGetValue(key, out var bg) || bg == null)
            {
                var root = item.transform;
                var existing = root.Find(BgName);
                if (existing != null)
                    bg = existing.GetComponent<Image>();
                if (bg == null)
                {
                    if (color == null)
                        return;
                    var go = new GameObject(BgName);
                    var rt = go.AddComponent<RectTransform>();
                    rt.SetParent(root, false);
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    rt.localScale = Vector3.one;
                    bg = go.AddComponent<Image>();
                    // Raycast target: the panel swallows the click (it bubbles to the row's own
                    // click handler) instead of falling through to the map behind the list.
                    bg.raycastTarget = true;
                    // The vanilla row is a layout group; keep the tint out of its flow.
                    var ignore = go.AddComponent<LayoutElement>();
                    ignore.ignoreLayout = true;
                }
                bg.transform.SetAsFirstSibling();
                Bgs[key] = bg;
            }
            if (color == null)
            {
                if (bg.gameObject.activeSelf)
                    bg.gameObject.SetActive(false);
                return;
            }
            // The row's grid cell is narrower than its content; stretch the tint to the content.
            if (contentWidth >= 60f)
            {
                var brt = bg.rectTransform;
                var w = contentWidth - 2f * inset;
                if (brt.anchorMax.x != 0f || Mathf.Abs(brt.sizeDelta.x - w) > 0.5f || Mathf.Abs(brt.anchoredPosition.x - inset) > 0.5f)
                {
                    brt.anchorMin = new Vector2(0f, 0f);
                    brt.anchorMax = new Vector2(0f, 1f);
                    brt.pivot = new Vector2(0f, 0.5f);
                    brt.sizeDelta = new Vector2(w, 0f);
                    brt.anchoredPosition = new Vector2(inset, 0f);
                }
            }
            if (bg.color != color.Value)
                bg.color = color.Value;
            if (!bg.gameObject.activeSelf)
                bg.gameObject.SetActive(true);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts group tint: " + ex.Message);
        }
    }

    /// <summary>Strip this mod's power suffix from a decorated name text.</summary>
    internal static string BaseName(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var t = GearSuffix.Replace(text, "");
        t = NameTags.Replace(t, "");
        return t.Trim();
    }

    /// <param name="huntPerks">Perk GUID hashes the servant's hunt favours; matching icons get a
    /// green glow. Null for servants not on a hunt (no highlight).</param>
    internal static void ApplyToText(TextMeshProUGUI nameText, string servantName, float iconSize, HashSet<int> huntPerks = null)
    {
        if (nameText == null || string.IsNullOrEmpty(servantName))
            return;
        UiTextState.Track(nameText);
        try
        {
            // Power (the sword number from Choose a Servant = hunt proficiency) in the name.
            if (TryGetPower(servantName, out var power))
            {
                var baseName = BaseName(nameText.text);
                if (baseName.Length == 0)
                    baseName = servantName;
                var wanted = NameOpen + baseName + NameClose + " <size=75%><color=" + GearColor + ">" + Mathf.RoundToInt(power) + "</color></size>";
                if (nameText.text != wanted)
                {
                    nameText.richText = true;
                    nameText.text = wanted;
                }
            }

            // Perk icons.
            if (!ServantInfoReader.TryGetInfo(servantName, out var info))
            {
                nameText.text = BaseName(nameText.text);
                if (States.TryGetValue(nameText.Pointer, out var stale) && stale.Holder != null)
                    stale.Holder.gameObject.SetActive(false);
                return;
            }
            var state = GetState(nameText, iconSize);
            if (state == null)
                return;
            state.Text = nameText;
            state.Holder.gameObject.SetActive(true);
            if (state.Perk0 != info.Perk0.GuidHash || state.Perk1 != info.Perk1.GuidHash
                || (info.Perk0.GuidHash != 0 && (state.Icons[0] == null || state.Icons[0].sprite == null))
                || (info.Perk1.GuidHash != 0 && (state.Icons[1] == null || state.Icons[1].sprite == null)))
            {
                SetIcon(state.Icons[0], info.Perk0);
                SetIcon(state.Icons[1], info.Perk1);
                state.Perk0 = info.Perk0.GuidHash;
                state.Perk1 = info.Perk1.GuidHash;
                if (!_loggedOnce)
                {
                    _loggedOnce = true;
                    Plugin.Logger?.LogInfo("HuntsAndRifts row decor: perk icons on '" + servantName + "'.");
                }
            }
            // Highlight: a green ring behind icons that match the hunt's favoured perks. Icons
            // themselves always stay full colour.
            for (var i = 0; i < 2; i++)
            {
                var perk = i == 0 ? info.Perk0.GuidHash : info.Perk1.GuidHash;
                var icon = state.Icons[i];
                if (icon != null && icon.color != Color.white)
                    icon.color = Color.white;
                var glow = state.Glows[i];
                if (glow == null)
                    continue;
                var on = huntPerks != null && perk != 0 && huntPerks.Contains(perk)
                         && icon != null && icon.gameObject.activeSelf;
                if (glow.gameObject.activeSelf != on)
                    glow.gameObject.SetActive(on);
            }
            // Keep the holder just right of the rendered name. Measure the current string
            // directly: preferredWidth lags a frame behind text changes and made the icons jitter.
            // GetPreferredValues is a full text layout pass; the game refreshes every row ~11x a
            // frame, so only re-measure when the string actually changed.
            var current = nameText.text ?? "";
            float width;
            if (state.MeasuredText == current)
            {
                width = state.MeasuredWidth;
            }
            else
            {
                try { width = nameText.GetPreferredValues(current).x; }
                catch { width = nameText.preferredWidth; }
                state.MeasuredText = current;
                state.MeasuredWidth = width;
            }
            var x = width + NameGap;
            if (float.IsNaN(x) || float.IsInfinity(x))
                x = NameGap;
            if (Mathf.Abs(state.Holder.anchoredPosition.x - x) > 0.5f)
                state.Holder.anchoredPosition = new Vector2(x, 0f);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts text decor: " + ex.Message);
        }
    }

    private static State GetState(TextMeshProUGUI nameText, float iconSize)
    {
        var key = nameText.Pointer;
        if (States.TryGetValue(key, out var state) && state.Holder != null)
            return state;

        var nameRt = nameText.rectTransform;
        RectTransform holder = null;
        var existing = nameRt.Find(HolderName);
        if (existing != null)
            holder = existing.GetComponent<RectTransform>();
        if (holder == null)
        {
            var go = new GameObject(HolderName);
            holder = go.AddComponent<RectTransform>();
            holder.SetParent(nameRt, false);
            holder.anchorMin = new Vector2(0f, 0.5f);
            holder.anchorMax = new Vector2(0f, 0.5f);
            holder.pivot = new Vector2(0f, 0.5f);
            holder.sizeDelta = new Vector2(iconSize * 2f + IconGap, iconSize);
            holder.anchoredPosition = new Vector2(NameGap, 0f);
            holder.localScale = Vector3.one;
        }

        state = new State { Holder = holder, Perk0 = int.MinValue, Perk1 = int.MinValue, IconSize = iconSize };
        for (var i = 0; i < 2; i++)
        {
            var iconName = "Icon" + i;
            Image img = null;
            var child = holder.Find(iconName);
            if (child != null)
                img = child.GetComponent<Image>();
            if (img == null)
            {
                var go = new GameObject(iconName);
                var rt = go.AddComponent<RectTransform>();
                rt.SetParent(holder, false);
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(iconSize, iconSize);
                rt.anchoredPosition = new Vector2(i * (iconSize + IconGap), 0f);
                rt.localScale = Vector3.one;
                img = go.AddComponent<Image>();
                img.raycastTarget = false;
                img.preserveAspect = true;
                go.SetActive(false);
            }
            state.Icons[i] = img;

            // Green ring behind the icon, shown only when the perk matches the hunt. It is a
            // sibling placed before the icon in the holder so it draws underneath.
            Image glow = null;
            var glowName = "Ring" + i;
            var glowT = holder.Find(glowName);
            if (glowT != null)
                glow = glowT.GetComponent<Image>();
            if (glow == null)
            {
                var g = new GameObject(glowName);
                var grt = g.AddComponent<RectTransform>();
                grt.SetParent(holder, false);
                grt.anchorMin = new Vector2(0f, 0.5f);
                grt.anchorMax = new Vector2(0f, 0.5f);
                grt.pivot = new Vector2(0f, 0.5f);
                grt.sizeDelta = new Vector2(iconSize + 6f, iconSize + 6f);
                grt.anchoredPosition = new Vector2(i * (iconSize + IconGap) - 3f, 0f);
                grt.localScale = Vector3.one;
                glow = g.AddComponent<Image>();
                glow.raycastTarget = false;
                glow.sprite = RingSprite.Get();
                glow.preserveAspect = true;
                glow.color = MatchRing;
                g.SetActive(false);
            }
            glow.transform.SetAsFirstSibling();
            state.Glows[i] = glow;
        }
        States[key] = state;
        return state;
    }

    internal static void SetIcon(Image img, PrefabGUID perk)
    {
        if (img == null)
            return;
        if (ServantInfoReader.TryGetPerk(perk, out var sprite, out _) && sprite != null)
        {
            img.sprite = sprite;
            img.color = Color.white;
            img.gameObject.SetActive(true);
        }
        else
        {
            img.sprite = null;
            img.gameObject.SetActive(false);
        }
    }
}
