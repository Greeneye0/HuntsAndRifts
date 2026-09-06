using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime.Injection;
using ProjectM.Shared.WarEvents;
using ProjectM.UI;
using UnityEngine;
using UnityEngine.UI;

namespace HuntsAndRifts;

/// <summary>
/// Own HUD overlay while the world map is open. IMGUI labels only —
/// never Instantiate MapWarEventInfoEntry, never call SetData.
/// Vanilla single WarEventInfo widget is left as-is.
/// </summary>
internal static class MapRiftOverlay
{
    private static GameObject _host;
    private static bool _mapOpen;
    private static GUIStyle _labelStyle;
    private static Texture2D _bg;
    private static string _cachedText = "";
    private static float _nextFormat;
    private static int _cachedLines;
    private static bool _hasWidgetAnchor;
    private static Rect _widgetScreen;
    // True while the map is in the servant throne (hunt) view: the rift overlay is hidden there.
    private static bool _huntMode;

    internal static void Spawn()
    {
        // The IMGUI fallback box is retired: the vanilla Rift Incursions card carries both tiers
        // now, and the box kept surfacing as stray "T1/T2" text off the panel edge.
        return;
#pragma warning disable CS0162
        if (_host != null)
            return;
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<RiftOverlayBehaviour>())
                ClassInjector.RegisterTypeInIl2Cpp<RiftOverlayBehaviour>();
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogWarning("HuntsAndRifts overlay type inject: " + ex.Message);
            return;
        }

        _host = new GameObject("HuntsAndRifts_RiftOverlay");
        UnityEngine.Object.DontDestroyOnLoad(_host);
        _host.hideFlags = HideFlags.HideAndDontSave;
        _host.AddComponent<RiftOverlayBehaviour>();
        Plugin.Logger?.LogInfo("HuntsAndRifts map overlay: IMGUI labels while MapMenu is open (docked to the vanilla rift widget).");
#pragma warning restore CS0162
    }

    internal static void DestroyHost()
    {
        _mapOpen = false;
        RiftDisplayCompatibility.Reset();
        RiftRowBadge.Reset();
        if (_host == null)
            return;
        UnityEngine.Object.Destroy(_host);
        _host = null;
    }

    internal static void SetMapOpen(bool open)
    {
        _mapOpen = open;
        if (!open)
        {
            _huntMode = false;
            _widgetTakenOver = false;
            RiftCardExtension.Reset();
            RiftDisplayCompatibility.Reset();
            RiftRowBadge.Reset();
            _cachedText = "";
            _cachedLines = 0;
            _hasWidgetAnchor = false;
        }
    }

    internal static void CaptureWidget(MapMenuMapper mapper)
    {
        _hasWidgetAnchor = false;
        if (mapper == null)
            return;
        try { _huntMode = mapper._MenuMode == MapMenuMode.ServantThrone; } catch { _huntMode = false; }
        try
        {
            var menu = mapper.GetMapMenu();
            if (menu == null)
                return;
            var info = menu.WarEventInfo;
            if (info == null)
                return;
            _lastInfoSeen = Time.unscaledTime;
            UiTextState.Track(info.TimerText?.Text);
            UiTextState.Track(info.LevelText?.Text);
            // Timing comes from event timestamps, independently of the UI language.
            // Vanilla re-sets the widget texts every update (SetData), so after parsing them we
            // overwrite: TimerText = the active tier (or the soonest one), SubHeaderText (the
            // small "Status" label) = the other tier. The IMGUI box is then not drawn.
            try
            {
                // Strings are rebuilt 4x/s; the texts themselves are re-applied every frame
                // because vanilla rewrites them every frame (throttling the write made the
                // timer flicker between "Ending: ..." and our line).
                var nowT = Time.unscaledTime;
                if (nowT - _lastApply >= 0.25f || !_widgetTakenOver)
                {
                    _lastApply = nowT;
                    _widgetTakenOver = ApplyToWidget(info);
                }
                else
                {
                    ReapplyCached(info);
                }
            }
            catch (Exception ex)
            {
                _widgetTakenOver = false;
                Plugin.Logger?.LogDebug("HuntsAndRifts widget takeover: " + ex.Message);
            }
            // Second timer bar for the other tier, below the card.
            RiftCardExtension.Apply(info, menu, _widgetTakenOver ? _secondaryTrack : null);
            RiftRowBadge.Apply(info, _widgetTakenOver ? _primaryTrack : null);
            RiftDisplayCompatibility.Apply(info, _widgetTakenOver && !_huntMode);
            // Bounding box of the whole rift card in IMGUI space (y-down).
            var imgui = default(Rect);
            var any = false;
            void Acc(RectTransform rt)
            {
                if (rt == null)
                    return;
                if (!TryImguiRect(rt, out var r))
                    return;
                imgui = any ? Union(imgui, r) : r;
                any = true;
            }
            try { Acc(info.GetComponent<RectTransform>()); } catch { }
            try { Acc(info.PositionRect); } catch { }
            try { if (info.FillImage != null) Acc(info.FillImage.rectTransform); } catch { }
            try { if (info.TextGroup != null) Acc(info.TextGroup.GetComponent<RectTransform>()); } catch { }
            try { if (info.TimerText != null) Acc(info.TimerText.GetComponent<RectTransform>()); } catch { }
            try { if (info.WarEventIcon != null) Acc(info.WarEventIcon.rectTransform); } catch { }
            if (!any)
                return;
            _widgetScreen = imgui;
            _hasWidgetAnchor = true;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts overlay widget anchor: " + ex.Message);
        }
    }

    private static bool _widgetTakenOver;
    private static bool _loggedTakeover;
    private static float _lastApply = -1f;
    private static string _cachedPrimary;
    private static string _cachedLevel;
    private static float _cachedFill;

    /// <summary>Cheap per-frame re-apply of the last computed texts (vanilla resets them).</summary>
    private static void ReapplyCached(MapWarEventInfoEntry info)
    {
        if (info == null)
            return;
        try
        {
            if (info.FillImage != null)
                info.FillImage.fillAmount = _cachedFill;
            var timer = info.TimerText;
            if (timer != null && _cachedPrimary != null && timer.GetText() != _cachedPrimary)
                timer.ForceSet(_cachedPrimary);
            var level = info.LevelText;
            if (level != null && _cachedLevel != null && level.GetText() != _cachedLevel)
                level.ForceSet(_cachedLevel);
        }
        catch
        {
        }
    }
    private static RiftTrack? _secondaryTrack;
    private static RiftTrack? _primaryTrack;

    private static string TierName(WarEventType t) => t switch
    {
        WarEventType.Minor => "T1",
        WarEventType.Major => "T2",
        WarEventType.Primal => "Primal",
        _ => t.ToString()
    };

    private static string TierLine(RiftTrack track, bool primary)
    {
        var name = TierName(track.Type) + (track.Level > 0 ? " (" + track.Level + "+)" : "");
        if (track.IsActive)
        {
            if (track.RemainingSeconds < 0)
                return name + " active";
            // Use the game's own phase word for the networked event ("active", "ending", ...),
            // so the countdown reads as what it is when a phase changes.
            var phase = "active";
            return name + " " + phase + ": " + FormatRift(track.RemainingSeconds);
        }
        var until = track.NextInSeconds;
        if (until < 0)
            return name + " awaiting schedule";
        return name + " next in " + FormatRift(until);
    }

    /// <summary>Write both tiers into the vanilla Rift Incursions card. Returns true if written.</summary>
    private static bool ApplyToWidget(MapWarEventInfoEntry info)
    {
        if (info == null)
            return false;
        var tracks = RiftReader.GetTracks();
        if (tracks == null || tracks.Count == 0)
            return false;
        var list = new System.Collections.Generic.List<RiftTrack>();
        foreach (var t in tracks)
            if (!t.IsTest && (t.Type == WarEventType.Minor || t.Type == WarEventType.Major || t.Type == WarEventType.Primal))
                list.Add(t);
        if (list.Count == 0)
            return false;
        list.Sort((a, b) =>
        {
            if (a.IsActive != b.IsActive)
                return a.IsActive ? -1 : 1;
            return (a.NextInSeconds < 0 ? double.MaxValue : a.NextInSeconds).CompareTo(b.NextInSeconds < 0 ? double.MaxValue : b.NextInSeconds);
        });

        // Tier badge (I / II) only while a rift is actually running; vanilla shows it for the
        // upcoming tier too, which reads as "active".
        try
        {
            var icon = info.WarEventIcon;
            if (icon != null)
            {
                var anyActive = false;
                foreach (var t in list)
                    if (t.IsActive) { anyActive = true; break; }
                if (icon.gameObject.activeSelf != anyActive)
                    icon.gameObject.SetActive(anyActive);
            }
        }
        catch
        {
        }

        var primary = TierLine(list[0], true);
        var secondary = list.Count > 1 ? TierLine(list[1], false) : "";
        _secondaryTrack = list.Count > 1 ? list[1] : (RiftTrack?)null;
        _primaryTrack = list[0];
        _cachedPrimary = primary;
        _cachedFill = RiftCardExtension.Fill(list[0]);
        if (info.FillImage != null)
            info.FillImage.fillAmount = _cachedFill;
        var wrote = false;
        var timer = info.TimerText;
        if (timer != null)
        {
            if (timer.GetText() != primary)
                timer.ForceSet(primary);
            wrote = true;
        }
        // The visible "Status" label is not SubHeaderText (writing there showed nothing), so the
        // second tier goes on the level line under the header: "Level: 80+  ·  T1 (57+) next in 1h".
        // "World Event, Level: 57+" duplicates the tier rows; keep just the event name.
        var level = info.LevelText;
        if (level != null)
        {
            var baseLevel = level.GetText() ?? "";
            var cut = baseLevel.IndexOf(',');
            if (cut > 0)
            {
                var trimmed = baseLevel.Substring(0, cut).Trim();
                if (trimmed.Length > 0)
                {
                    _cachedLevel = trimmed;
                    if (level.GetText() != trimmed)
                        level.ForceSet(trimmed);
                }
            }
        }
        if (wrote && !_loggedTakeover)
        {
            _loggedTakeover = true;
            Plugin.Logger?.LogInfo("HuntsAndRifts: rift card shows '" + primary + "' / '" + secondary + "' (IMGUI overlay off).");
        }
        return wrote;
    }

    private static float _lastInfoSeen = -100f;

    internal static void DrawImgui()
    {
        if (!_mapOpen || _huntMode || _widgetTakenOver)
            return;
        // The IMGUI box is only a fallback for a client without the vanilla rift card. While
        // the card exists (seen within the last 2 s) never draw it: it flashed as stray "T1/T2"
        // text off the panel edge before the card takeover ran.
        if (Time.unscaledTime - _lastInfoSeen < 2f)
            return;
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return;

        FormatIfNeeded();
        if (_cachedLines <= 0 || string.IsNullOrEmpty(_cachedText))
            return;

        EnsureStyles();
        if (_labelStyle == null)
            return;

        const float pad = 10f;
        const float lineH = 22f;
        var width = 280f;
        var height = pad * 2f + lineH * _cachedLines;
        var rect = PlaceRect(width, height);

        var oldColor = GUI.color;
        GUI.color = Color.white;
        if (_bg != null)
            GUI.DrawTexture(rect, _bg);
        var textRect = new Rect(rect.x + pad, rect.y + 6f, rect.width - pad * 2f, rect.height - 8f);
        GUI.Label(textRect, _cachedText, _labelStyle);
        GUI.color = oldColor;
    }

    private static void FormatIfNeeded()
    {
        var now = Time.unscaledTime;
        if (now < _nextFormat && _cachedLines > 0)
            return;
        _nextFormat = now + 0.25f;

        var tracks = RiftReader.GetTracks();
        if (tracks == null || tracks.Count == 0)
        {
            _cachedText = "";
            _cachedLines = 0;
            return;
        }

        var sb = new StringBuilder(192);
        var lines = 0;
        for (var i = 0; i < tracks.Count; i++)
        {
            var line = FormatLine(tracks[i]);
            if (string.IsNullOrEmpty(line))
                continue;
            if (lines > 0)
                sb.Append('\n');
            sb.Append(line);
            lines++;
        }

        _cachedText = sb.ToString();
        _cachedLines = lines;
    }

    private static string FormatLine(RiftTrack track)
    {
        var name = track.Type switch
        {
            WarEventType.Minor => "T1",
            WarEventType.Major => "T2",
            WarEventType.Primal => "Primal",
            _ => track.Type.ToString()
        };
        var level = track.Level > 0 ? " (" + track.Level + "+)" : "";
        var test = track.IsTest ? " TEST" : "";
        if (track.IsActive)
            return track.RemainingSeconds < 0 ? name + level + " active" + test : name + level + " remaining " + FormatRift(track.RemainingSeconds) + test;
        var until = track.NextInSeconds;
        if (until < 0)
            return name + " awaiting schedule";
        return name + level + " next in " + FormatRift(until) + test;
    }

    private static string FormatRift(double seconds)
    {
        if (seconds < 0)
            seconds = 0;
        var total = (int)Math.Floor(seconds);
        var h = total / 3600;
        var m = (total % 3600) / 60;
        var s = total % 60;
        if (h > 0)
            return h + "h " + m + "m";
        if (m > 0)
            return m + "m " + s + "s";
        return s + "s";
    }

    static readonly Regex HourMinRx = new(@"(\d+)\s*h\s*(\d+)\s*m(?:\s*(\d+)\s*s)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex MinSecRx = new(@"(\d+)\s*m\s*(\d+)\s*s", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex MinOnlyRx = new(@"(\d+)\s*m\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex SecOnlyRx = new(@"(\d+)\s*s\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Phase word from the vanilla timer line ("Active", "Ending", "Starting", "Next"), lower-cased.</summary>
    internal static string VanillaPhase { get; private set; } = "active";

    private static bool TryReadVanillaTimer(MapWarEventInfoEntry info, out double seconds)
    {
        seconds = 0;
        if (info == null)
            return false;
        // Never parse HuntsAndRifts's own overwritten text (it would freeze the clock).
        var timer = ReadLocalized(info.TimerText);
        if (!RiftTiming.IsOwnText(timer) && TryParseTimerText(timer, out seconds))
        {
            var colon = timer.IndexOf(':');
            if (colon > 0)
            {
                var word = timer.Substring(0, colon).Trim().ToLowerInvariant();
                if (word.Length > 0 && word.Length < 16)
                    VanillaPhase = word;
            }
            return true;
        }
        var sub = ReadLocalized(info.SubHeaderText);
        return !RiftTiming.IsOwnText(sub) && TryParseTimerText(sub, out seconds);
    }

    private static string ReadLocalized(LocalizedText loc)
    {
        if (loc == null)
            return "";
        try
        {
            var s = loc.GetText();
            if (!string.IsNullOrWhiteSpace(s))
                return s;
        }
        catch
        {
        }
        try
        {
            var tmp = loc.Text;
            if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text))
                return tmp.text;
        }
        catch
        {
        }
        return "";
    }

    internal static bool TryParseTimerText(string text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var t = text.Trim();
        var m = HourMinRx.Match(t);
        if (m.Success)
        {
            seconds = IntPart(m, 1) * 3600 + IntPart(m, 2) * 60 + IntPart(m, 3);
            return true;
        }
        m = MinSecRx.Match(t);
        if (m.Success)
        {
            seconds = IntPart(m, 1) * 60 + IntPart(m, 2);
            return true;
        }
        m = MinOnlyRx.Match(t);
        if (m.Success)
        {
            seconds = IntPart(m, 1) * 60;
            return true;
        }
        m = SecOnlyRx.Match(t);
        if (m.Success)
        {
            seconds = IntPart(m, 1);
            return true;
        }
        return false;
    }

    private static int IntPart(Match m, int i)
    {
        if (i >= m.Groups.Count || !m.Groups[i].Success || string.IsNullOrEmpty(m.Groups[i].Value))
            return 0;
        return int.TryParse(m.Groups[i].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    private static Rect Union(Rect a, Rect b)
    {
        var x = Math.Min(a.x, b.x);
        var y = Math.Min(a.y, b.y);
        var right = Math.Max(a.xMax, b.xMax);
        var bottom = Math.Max(a.yMax, b.yMax);
        return new Rect(x, y, right - x, bottom - y);
    }

    /// <summary>
    /// IMGUI rect (y-down, y is the top edge) from a RectTransform.
    /// Always goes through WorldToScreenPoint so overlay/camera canvases match OnGUI.
    /// </summary>
    private static bool TryImguiRect(RectTransform rt, out Rect imgui)
    {
        imgui = default;
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Canvas canvas = null;
        try { canvas = rt.GetComponentInParent<Canvas>(); } catch { }
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        var xMin = float.PositiveInfinity;
        var xMax = float.NegativeInfinity;
        var yMin = float.PositiveInfinity;
        var yMax = float.NegativeInfinity;
        for (var i = 0; i < 4; i++)
        {
            var sp = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            if (sp.x < xMin) xMin = sp.x;
            if (sp.x > xMax) xMax = sp.x;
            if (sp.y < yMin) yMin = sp.y;
            if (sp.y > yMax) yMax = sp.y;
        }
        if (xMax - xMin < 8f || yMax - yMin < 8f)
            return false;
        imgui = new Rect(xMin, Screen.height - yMax, xMax - xMin, yMax - yMin);
        return true;
    }

    private static Rect PlaceRect(float width, float height)
    {
        const float gap = 10f;
        const float margin = 16f;
        float x;
        float y;

        if (_hasWidgetAnchor)
        {
            // _widgetScreen is IMGUI (y-down). Sit directly under the card.
            x = _widgetScreen.x;
            y = _widgetScreen.yMax + gap;
        }
        else
        {
            ContentRect(out var originX, out var originY, out var contentW, out var contentH);
            x = originX + contentW - width - gap;
            y = originY + contentH - height - 88f;
        }

        if (x < margin)
            x = margin;
        if (y < margin)
            y = margin;
        if (x + width > Screen.width - margin)
            x = Screen.width - width - margin;
        if (y + height > Screen.height - margin)
            y = Screen.height - height - margin;
        return new Rect(x, y, width, height);
    }

    private static void ContentRect(out float x, out float y, out float w, out float h)
    {
        const float aspect = 16f / 9f;
        var screenW = (float)Screen.width;
        var screenH = (float)Screen.height;
        var screenAspect = screenW / Math.Max(1f, screenH);
        if (screenAspect > aspect)
        {
            h = screenH;
            w = h * aspect;
            x = (screenW - w) * 0.5f;
            y = 0f;
        }
        else
        {
            w = screenW;
            h = w / aspect;
            x = 0f;
            y = (screenH - h) * 0.5f;
        }
    }

    private static void EnsureStyles()
    {
        if (_labelStyle != null)
            return;
        try
        {
            _labelStyle = new GUIStyle();
            _labelStyle.fontSize = 16;
            _labelStyle.fontStyle = FontStyle.Bold;
            _labelStyle.alignment = TextAnchor.UpperLeft;
            _labelStyle.wordWrap = false;
            _labelStyle.richText = false;
            var state = new GUIStyleState();
            state.textColor = new Color(0.96f, 0.90f, 0.72f, 1f);
            _labelStyle.normal = state;

            _bg = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _bg.SetPixel(0, 0, new Color(0.04f, 0.05f, 0.07f, 0.72f));
            _bg.Apply();
            _bg.hideFlags = HideFlags.HideAndDontSave;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts overlay style: " + ex.Message);
        }
    }
}

internal sealed class RiftOverlayBehaviour : MonoBehaviour
{
    public RiftOverlayBehaviour(IntPtr ptr) : base(ptr)
    {
    }

    private void OnGUI()
    {
        try
        {
            MapRiftOverlay.DrawImgui();
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts overlay OnGUI: " + ex.Message);
        }
    }
}
