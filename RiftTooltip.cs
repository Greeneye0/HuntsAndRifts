using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ProjectM.Shared.WarEvents;
using ProjectM.UI;
using UnityEngine;

namespace HuntsAndRifts;

/// <summary>
/// Rift (war event) zone tooltip: append tier timing to the vanilla "Status: ..." line.
///   Active plot, level shown:   "Status: Active  T2 (80+) 12m left"
///   Inactive plot, level shown: "Status: Inactive  T1 (57+) next in 23m"
///   Level line hidden:          "Status: Inactive  T1 (57+) next in 23m · T2 (80+) next in 1h 5m"
/// Only reads the tooltip's own texts (the vanilla level line gives the tier) and the mod's
/// rift tracks. No native zone lookups: the first attempt at that crashed the client.
/// </summary>
internal static class RiftTooltip
{
    private static readonly Regex Suffix = new(@"\s*<color=#6CE38A>.*?</color>\s*$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex LevelRx = new(@"(\d+)", RegexOptions.Compiled);
    private static bool _loggedOnce;

    internal static void Tick(MapMenuMapper mapper, MapMenu menu, MapTooltip tooltip)
    {
        try
        {
            var status = tooltip.WarEventStatusText;
            if (status == null)
                return;
            UiTextState.Track(status.Text);
            bool statusVisible;
            try { statusVisible = status.gameObject.activeInHierarchy; } catch { statusVisible = false; }
            // The tooltip hides by fading (MainAlpha), not by deactivating: a faded tooltip still
            // counts as active and would keep receiving our lines.
            try
            {
                var grp = tooltip.MainAlpha;
                if (grp != null && grp.alpha < 0.05f)
                    statusVisible = false;
            }
            catch
            {
            }
            if (!statusVisible)
            {
                RestoreVanilla(status);
                return;
            }
            DiagnoseStray();

            var tracks = RiftReader.GetTracks();
            if (tracks == null || tracks.Count == 0)
                return;

            int? level = ReadLevel(tooltip);
            var line = Describe(tracks, level);
            if (string.IsNullOrEmpty(line))
                return;

            var baseText = status.GetText();
            if (string.IsNullOrEmpty(baseText))
                return;
            baseText = Suffix.Replace(baseText, "").TrimEnd();
            // One tier per line under the status; a single tier stays inline.
            var combined = line.Contains('\n')
                ? baseText + "\n<color=#6CE38A>" + line + "</color>"
                : baseText + "  <color=#6CE38A>" + line + "</color>";
            if (status.GetText() == combined)
                return;
            try
            {
                if (status.Text != null)
                    status.Text.richText = true;
            }
            catch
            {
            }
            status.ForceSet(combined);
            GrowToFit(status, combined);
            if (!_loggedOnce)
            {
                _loggedOnce = true;
                Plugin.Logger?.LogInfo("HuntsAndRifts rift tooltip: '" + combined + "' (level " + (level.HasValue ? level.Value.ToString() : "n/a") + ").");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts rift tooltip: " + ex.Message);
        }
    }

    private static void RestoreVanilla(LocalizedText status)
    {
        try
        {
            var txt = status.GetText();
            if (string.IsNullOrEmpty(txt) || txt.IndexOf("<color=#6CE38A>", StringComparison.Ordinal) < 0)
                return;
            var stripped = Suffix.Replace(txt, "").TrimEnd();
            if (stripped != txt)
                status.ForceSet(stripped);
        }
        catch
        {
        }
    }

    private static float _nextDiag = 0f;
    private static int _diagCount;

    // One-off: name any text object on screen whose text starts with "T1 (" that is NOT inside
    // the rift card or the tooltip, to pin down the stray "T1/T2" text beside the panel.
    private static void DiagnoseStray()
    {
        try
        {
            var now = UnityEngine.Time.unscaledTime;
            if (now < _nextDiag || _diagCount >= 3)
                return;
            _nextDiag = now + 5f;
            var all = UnityEngine.Object.FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (all == null)
                return;
            foreach (var tmp in all)
            {
                if (tmp == null)
                    continue;
                string t = null;
                try { t = tmp.text; } catch { }
                if (string.IsNullOrEmpty(t) || !(t.StartsWith("T1 (") || t.StartsWith("T2 (") || t.Contains("\nT2 (")))
                    continue;
                var chain = new System.Text.StringBuilder();
                var tr = tmp.transform;
                for (var i = 0; i < 8 && tr != null; i++)
                {
                    chain.Append(tr.name).Append(" < ");
                    tr = tr.parent;
                }
                _diagCount++;
                Plugin.Logger?.LogInfo("HuntsAndRifts stray-diag: '" + t.Replace("\n", "|") + "' at " + tmp.transform.position + " chain " + chain);
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts stray-diag: " + ex.Message);
        }
    }

    // The status text is a fixed one-line element inside the tooltip's vertical layout; give it
    // (and its layout element) the height the multi-line text needs so the tooltip grows.
    private static void GrowToFit(LocalizedText status, string text)
    {
        try
        {
            var tmp = status.Text;
            if (tmp == null)
                return;
            var rt = tmp.rectTransform;
            var width = rt.rect.width;
            if (width < 40f)
                width = 300f;
            var h = tmp.GetPreferredValues(text, width, 0f).y + 4f;
            if (h < 10f || h > 400f)
                return;
            if (Mathf.Abs(rt.sizeDelta.y - h) > 0.5f)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
            var le = tmp.GetComponent<UnityEngine.UI.LayoutElement>();
            if (le == null)
                le = tmp.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            if (Mathf.Abs(le.preferredHeight - h) > 0.5f)
            {
                le.preferredHeight = h;
                le.minHeight = h;
            }
            if (!_loggedGrow)
            {
                _loggedGrow = true;
                Plugin.Logger?.LogInfo("HuntsAndRifts rift tooltip: status text height " + h.ToString("0") + " (width " + width.ToString("0") + ").");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts rift tooltip grow: " + ex.Message);
        }
    }

    private static bool _loggedGrow;

    private static int? ReadLevel(MapTooltip tooltip)
    {
        try
        {
            var lt = tooltip.WarEventLevelText;
            if (lt == null)
                return null;
            if (!lt.gameObject.activeInHierarchy)
                return null;
            string s = null;
            try { s = lt.GetText(); } catch { }
            if (string.IsNullOrWhiteSpace(s) && lt.Text != null)
                s = lt.Text.text;
            if (string.IsNullOrWhiteSpace(s))
                return null;
            var m = LevelRx.Match(s);
            if (!m.Success)
                return null;
            return int.Parse(m.Groups[1].Value);
        }
        catch
        {
            return null;
        }
    }

    private static string TierName(WarEventType t) => t switch
    {
        WarEventType.Minor => "T1",
        WarEventType.Major => "T2",
        WarEventType.Primal => "Primal",
        _ => t.ToString()
    };

    private static string Line(RiftTrack t, bool thisPlot = true)
    {
        var name = TierName(t.Type) + (t.Level > 0 ? " (" + t.Level + "+)" : "");
        if (t.IsActive)
        {
            // On an inactive plot the running tier is somewhere else: say so.
            if (t.RemainingSeconds < 0)
                return thisPlot ? name + " active" : name + " active elsewhere";
            return thisPlot
                ? name + " " + HuntReader.FormatRemaining(t.RemainingSeconds) + " left"
                : name + " active elsewhere, " + HuntReader.FormatRemaining(t.RemainingSeconds) + " left";
        }
        var until = t.NextInSeconds;
        if (until < 0)
            return name + " awaiting schedule";
        return name + " next in " + HuntReader.FormatRemaining(until);
    }

    private static string Describe(IReadOnlyList<RiftTrack> tracks, int? level)
    {
        var live = new List<RiftTrack>();
        foreach (var t in tracks)
            if (!t.IsTest && (t.Type == WarEventType.Minor || t.Type == WarEventType.Major || t.Type == WarEventType.Primal))
                live.Add(t);
        if (live.Count == 0)
            return null;

        if (level.HasValue)
        {
            foreach (var t in live)
                if (t.Level == level.Value)
                    return Line(t);
        }

        live.Sort((a, b) =>
        {
            if (a.IsActive != b.IsActive)
                return a.IsActive ? -1 : 1;
            return (a.NextInSeconds < 0 ? double.MaxValue : a.NextInSeconds).CompareTo(b.NextInSeconds < 0 ? double.MaxValue : b.NextInSeconds);
        });
        var parts = new List<string>();
        foreach (var t in live)
            parts.Add(Line(t, thisPlot: false));
        return string.Join("\n", parts);
    }
}
