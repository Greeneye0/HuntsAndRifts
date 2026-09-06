using System;
using System.Collections.Generic;
using System.Text;
using ProjectM.UI;
using Stunlock.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HuntsAndRifts;

/// <summary>
/// Map zone tooltip additions for hunt zones, applied from the MapMenuMapper.OnUpdate
/// postfix (no Harmony patch on MapTooltip.Show/ShowMission: those take generic buffer
/// structs by value, which the IL2CPP detour can mis-marshal and break the vanilla call).
///  - a two-line block after the description: "Power N  [favoured perk icons]" and the perk
///    names wrapped beneath;
///  - power and perk icons on each servant name line when the hunt is occupied.
/// The block is a new child of the tooltip's section parent. The tooltip's layout does not
/// control child heights, so the block's height is computed here (a fresh RectTransform would
/// otherwise stay at Unity's 100 px default and open a large gap).
/// </summary>
internal static class TooltipDecor
{
    private const string RowName = "HuntsAndRifts_MissionRow";
    private const float IconSize = 20f;
    private const float IconGap = 3f;
    private const float Line1Height = 24f;
    private static Row _row;
    private static IntPtr _rowTooltip;
    private static bool _loggedRow;
    private static string _lastMissTitle;

    private sealed class Row
    {
        public GameObject Root;
        public RectTransform Rect;
        public TextMeshProUGUI Power;
        public readonly List<Image> Icons = new();
        public TextMeshProUGUI Names;
        public int Mission;
        public float Width;
        public bool PerksReady;
        public float RetryAt;
    }

    internal static void Clear()
    {
        if (_row?.Root != null)
        {
            _row.Root.SetActive(false);
            UnityEngine.Object.Destroy(_row.Root);
        }
        _row = null;
        _rowTooltip = IntPtr.Zero;
    }

    internal static void Tick(MapMenuMapper mapper)
    {
        try
        {
            if (mapper == null)
                return;
            var menu = mapper.GetMapMenu();
            if (menu == null)
                return;
            var tooltip = menu.Tooltip;
            if (tooltip == null)
                return;
            bool visible;
            try { visible = tooltip.gameObject.activeInHierarchy; } catch { visible = false; }
            if (!visible)
            {
                HideRow();
                return;
            }

            RiftTooltip.Tick(mapper, menu, tooltip);

            var title = ReadTitle(tooltip);
            var mission = HoveredZoneMission(mapper);
            if (mission.GuidHash == 0 && (string.IsNullOrEmpty(title) || !HuntReader.TryGetMissionForZoneName(title, out mission)))
            {
                if (!string.IsNullOrEmpty(title) && _lastMissTitle != title)
                {
                    _lastMissTitle = title;
                    Plugin.Logger?.LogInfo("HuntsAndRifts tooltip: '" + title + "' is not an indexed hunt zone.");
                }
                HideRow();
                return;
            }

            var row = GetRow(tooltip);
            var info = MissionInfoReader.Get(mission);
            if (row != null)
            {
                if (info == null)
                    HideRow();
                else
                    FillRow(row, tooltip, mission, info);
            }
            DecorateServants(tooltip, info);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts tooltip: " + ex.Message);
        }
    }

    private static bool _zoneTypeReady;
    private static Unity.Entities.TypeIndex _zoneTypeIndex;

    // MapMenuMapper tracks the zone entity under the cursor; read its MapZoneData raw
    // (ServantMissionAsset at 0x48) so the tooltip matches the exact hovered zone, not a name.
    private static unsafe PrefabGUID HoveredZoneMission(MapMenuMapper mapper)
    {
        try
        {
            var entity = mapper._HoveredMapZoneEntity;
            if (entity == Unity.Entities.Entity.Null)
                return default;
            if (!HuntReader.TryGetClientWorld(out var world))
                return default;
            if (!_zoneTypeReady)
            {
                _zoneTypeReady = true;
                try { _zoneTypeIndex = Unity.Entities.TypeManager.GetTypeIndex(Il2CppInterop.Runtime.Il2CppType.Of<ProjectM.Terrain.MapZoneData>()); } catch { }
            }
            if (_zoneTypeIndex.Value == 0)
                return default;
            var em = world.EntityManager;
            if (!em.Exists(entity) || !em.HasComponent(entity, Unity.Entities.ComponentType.ReadOnly(_zoneTypeIndex)))
                return default;
            var raw = (byte*)em.GetComponentDataRawRO(entity, _zoneTypeIndex);
            if (raw == null)
                return default;
            return new PrefabGUID(*(int*)(raw + 0x48));
        }
        catch
        {
            return default;
        }
    }

    private static string ReadTitle(MapTooltip tooltip)
    {
        try
        {
            var t = tooltip.TitleText;
            if (t == null)
                return null;
            string s = null;
            try { s = t.GetText(); } catch { }
            if (string.IsNullOrWhiteSpace(s) && t.Text != null)
                s = t.Text.text;
            return s?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static void HideRow()
    {
        try
        {
            if (_row != null && _row.Root != null && _row.Root.activeSelf)
                _row.Root.SetActive(false);
        }
        catch
        {
        }
    }

    private static void DecorateServants(MapTooltip tooltip, MissionInfoReader.Info info)
    {
        var list = tooltip.ServantList;
        if (list == null)
            return;
        HashSet<int> huntPerks = null;
        if (info != null && info.Perks.Count > 0)
        {
            huntPerks = new HashSet<int>();
            foreach (var p in info.Perks)
                huntPerks.Add(p.GuidHash);
        }
        var count = list.Count;
        for (var i = 0; i < count; i++)
        {
            var tmp = list[i];
            if (tmp == null)
                continue;
            try
            {
                if (!tmp.gameObject.activeSelf)
                    continue;
            }
            catch
            {
                continue;
            }
            var name = RowDecor.BaseName(tmp.text);
            if (string.IsNullOrEmpty(name))
                continue;
            RowDecor.ApplyToText(tmp, name, IconSize, huntPerks);
        }
    }

    private static Row GetRow(MapTooltip tooltip)
    {
        if (_row != null && _row.Root != null && _rowTooltip == tooltip.Pointer)
            return _row;
        try
        {
            var descRect = tooltip.DescriptionRect;
            var descText = tooltip.DescriptionText;
            if (descRect == null || descText == null)
                return null;
            var parent = descRect.parent;
            if (parent == null)
                return null;
            var srcTmp = descText.Text;

            var existing = parent.Find(RowName);
            GameObject root;
            RectTransform rect;
            if (existing != null)
            {
                root = existing.gameObject;
                rect = root.GetComponent<RectTransform>();
            }
            else
            {
                root = new GameObject(RowName);
                rect = root.AddComponent<RectTransform>();
                rect.SetParent(parent, false);
                rect.localScale = Vector3.one;
            }
            // Same anchoring as the description block so the layout treats it alike.
            rect.anchorMin = descRect.anchorMin;
            rect.anchorMax = descRect.anchorMax;
            rect.pivot = descRect.pivot;
            rect.sizeDelta = new Vector2(descRect.sizeDelta.x, Line1Height);
            root.transform.SetSiblingIndex(descRect.GetSiblingIndex() + 1);
            var le = root.GetComponent<LayoutElement>();
            if (le == null)
                le = root.AddComponent<LayoutElement>();
            le.preferredHeight = Line1Height;
            le.minHeight = Line1Height;

            var row = new Row { Root = root, Rect = rect, Mission = 0 };
            row.Power = GetOrMakeText(root.transform, "Power", srcTmp, false);
            for (var i = 0; i < 4; i++)
                row.Icons.Add(GetOrMakeIcon(root.transform, "Icon" + i));
            row.Names = GetOrMakeText(root.transform, "Names", srcTmp, true);
            _row = row;
            _rowTooltip = tooltip.Pointer;
            if (!_loggedRow)
            {
                _loggedRow = true;
                Plugin.Logger?.LogInfo("HuntsAndRifts tooltip: mission block added under " + parent.name
                    + " (description " + descRect.rect.size + ").");
            }
            return row;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts tooltip row: " + ex.Message);
            return null;
        }
    }

    private static TextMeshProUGUI GetOrMakeText(Transform parent, string name, TMP_Text template, bool wrap)
    {
        var existing = parent.Find(name);
        TextMeshProUGUI tmp = null;
        if (existing != null)
            tmp = existing.GetComponent<TextMeshProUGUI>();
        if (tmp == null)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.localScale = Vector3.one;
            tmp = go.AddComponent<TextMeshProUGUI>();
        }
        tmp.richText = true;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = wrap;
        tmp.alignment = wrap ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
        try
        {
            if (template != null)
            {
                tmp.font = template.font;
                tmp.fontSharedMaterial = template.fontSharedMaterial;
                tmp.fontSize = template.fontSize;
                tmp.color = template.color;
            }
        }
        catch
        {
        }
        var r = tmp.rectTransform;
        r.anchorMin = new Vector2(0f, 1f);
        r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 1f);
        return tmp;
    }

    private static Image GetOrMakeIcon(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null)
        {
            var have = existing.GetComponent<Image>();
            if (have != null)
                return have;
        }
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.localScale = Vector3.one;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(IconSize, IconSize);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.preserveAspect = true;
        go.SetActive(false);
        return img;
    }

    private static void FillRow(Row row, MapTooltip tooltip, PrefabGUID mission, MissionInfoReader.Info info)
    {
        if (row.Root == null)
            return;
        if (!row.Root.activeSelf)
            row.Root.SetActive(true);

        float width = 0f;
        try { width = tooltip.DescriptionRect != null ? tooltip.DescriptionRect.rect.width : 0f; } catch { }
        if (width < 40f)
            width = row.Rect.rect.width;
        if (width < 40f)
            width = 300f;

        if (row.Mission == mission.GuidHash && Mathf.Abs(row.Width - width) < 0.5f
            && (row.PerksReady || Time.unscaledTime < row.RetryAt))
            return;
        row.Mission = mission.GuidHash;
        row.Width = width;
        row.PerksReady = true;
        row.RetryAt = Time.unscaledTime + 0.5f;

        // Line 1: "Power N" then the favoured perk icons.
        var powerText = "<color=#6CE38A>Power " + info.Difficulty + "</color>";
        row.Power.text = powerText;
        var powerRt = row.Power.rectTransform;
        var powerW = row.Power.GetPreferredValues(powerText).x + 2f;
        powerRt.sizeDelta = new Vector2(powerW, Line1Height);
        powerRt.anchoredPosition = new Vector2(0f, 0f);

        var x = powerW + 8f;
        var names = new StringBuilder();
        for (var i = 0; i < row.Icons.Count; i++)
        {
            var img = row.Icons[i];
            if (img == null)
                continue;
            if (i < info.Perks.Count && ServantInfoReader.TryGetPerk(info.Perks[i], out var sprite, out var perkName) && sprite != null)
            {
                img.sprite = sprite;
                img.color = Color.white;
                img.rectTransform.anchoredPosition = new Vector2(x, -(Line1Height - IconSize) * 0.5f);
                img.gameObject.SetActive(true);
                x += IconSize + IconGap;
                if (!string.IsNullOrEmpty(perkName))
                {
                    if (names.Length > 0)
                        names.Append(", ");
                    names.Append(perkName);
                }
            }
            else
            {
                if (i < info.Perks.Count) row.PerksReady = false;
                img.gameObject.SetActive(false);
            }
        }

        // Line 2: perk names, wrapped to the tooltip width.
        var total = Line1Height;
        if (names.Length > 0)
        {
            var nameText = "<size=85%><color=#C9C4B4>" + names + "</color></size>";
            row.Names.text = nameText;
            var h = row.Names.GetPreferredValues(nameText, width, 0f).y + 2f;
            var nrt = row.Names.rectTransform;
            nrt.sizeDelta = new Vector2(width, h);
            nrt.anchoredPosition = new Vector2(0f, -Line1Height);
            row.Names.gameObject.SetActive(true);
            total += h;
        }
        else
        {
            row.Names.gameObject.SetActive(false);
        }

        row.Rect.sizeDelta = new Vector2(row.Rect.sizeDelta.x, total);
        var le = row.Root.GetComponent<LayoutElement>();
        if (le != null)
        {
            le.preferredHeight = total;
            le.minHeight = total;
        }
    }
}
