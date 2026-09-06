using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace HuntsAndRifts;

/// <summary>
/// Castle switcher on the vanilla hunt map. Does not clone fog, zones, or clicks.
/// Reads Satisvampory thrones-client.json (same-machine dedicated) and arms
/// the next map click via the debug mailbox hunt op.
/// </summary>
internal static class ThroneHuntOverlay
{
    internal const string JsonPath = @"C:\VRisingServer\BepInEx\config\Satisvampory\debug\thrones-client.json";
    internal const string MailboxPath = @"C:\VRisingServer\BepInEx\config\Satisvampory\debug\req.json";

    static bool _huntOpen;
    static GUIStyle _label;
    static GUIStyle _button;
    static GUIStyle _statusStyle;
    static Texture2D _bg;
    static readonly List<PlotRow> Plots = new();
    static float _nextRead;
    static int _selectedPlot = -1;
    static readonly HashSet<int> SelectedServants = new();
    static string _status = "Pick a castle, then who goes, then click a territory.";
    static string _lastArmed = "";

    struct PlotRow
    {
        public int Plot;
        public bool Here;
        public string Label;
        public List<ServantRow> Servants;
    }

    struct ServantRow
    {
        public int Index;
        public string Name;
    }

    internal static void SetHuntOpen(bool open)
    {
        _huntOpen = open;
        if (!open)
        {
            _status = "Pick a castle, then who goes, then click a territory.";
            SelectedServants.Clear();
            _lastArmed = "";
        }
    }

    internal static void DrawImgui()
    {
        if (!_huntOpen)
            return;
        RefreshJson();
        EnsureStyles();
        if (_label == null || _button == null)
            return;

        const float pad = 10f;
        var width = 300f;
        var height = 52f + Plots.Count * 26f + 12f;
        var selected = FindPlot(_selectedPlot);
        if (selected.Servants != null)
            height += 26f + selected.Servants.Count * 24f;
        height += 44f;

        var mapHalf = Mathf.Min(560f, Screen.width * 0.28f);
        var x = Screen.width * 0.5f - mapHalf - width - 16f;
        if (x < 12f)
            x = 12f;
        var y0 = Screen.height * 0.16f;
        var rect = new Rect(x, y0, width, height);
        if (_bg != null)
            GUI.DrawTexture(rect, _bg);

        var y = rect.y + pad;
        GUI.Label(new Rect(rect.x + pad, y, width - pad * 2f, 20f), "1. Castle to send from", _label);
        y += 22f;
        for (var i = 0; i < Plots.Count; i++)
        {
            var p = Plots[i];
            var mark = p.Plot == _selectedPlot ? "  <" : (p.Here ? "  (this chair)" : "");
            var pressed = GUI.Button(new Rect(rect.x + pad, y, width - pad * 2f, 22f), p.Label + mark, _button);
            if (pressed)
            {
                _selectedPlot = p.Plot;
                SelectedServants.Clear();
                _lastArmed = "";
                _status = p.Here
                    ? "This chair — or pick another castle, then tick who goes."
                    : "2. Tick who to send (up to 3), then click a territory.";
            }
            y += 24f;
        }

        selected = FindPlot(_selectedPlot);
        if (selected.Servants != null && selected.Servants.Count > 0)
        {
            GUI.Label(new Rect(rect.x + pad, y, width - pad * 2f, 20f), "2. Who goes (up to 3)", _label);
            y += 22f;
            var changed = false;
            for (var i = 0; i < selected.Servants.Count; i++)
            {
                var s = selected.Servants[i];
                var on = SelectedServants.Contains(s.Index);
                var next = GUI.Toggle(new Rect(rect.x + pad, y, width - pad * 2f, 22f), on, s.Name);
                if (next && !on)
                {
                    if (SelectedServants.Count < 3)
                    {
                        SelectedServants.Add(s.Index);
                        changed = true;
                    }
                }
                else if (!next && on)
                {
                    SelectedServants.Remove(s.Index);
                    changed = true;
                }
                y += 22f;
            }
            if (changed)
                ArmMailbox(selected.Plot);
        }

        if (_statusStyle != null)
            GUI.Label(new Rect(rect.x + pad, y, width - pad * 2f, 40f), _status, _statusStyle);
    }

    static PlotRow FindPlot(int plot)
    {
        for (var i = 0; i < Plots.Count; i++)
            if (Plots[i].Plot == plot)
                return Plots[i];
        return default;
    }

    static void ArmMailbox(int plot)
    {
        if (SelectedServants.Count == 0)
        {
            _lastArmed = "";
            _status = "Tick who to send, then click a territory on the map.";
            return;
        }
        var names = new StringBuilder();
        var numbers = new StringBuilder();
        var row = FindPlot(plot);
        foreach (var i in SelectedServants)
        {
            if (numbers.Length > 0)
                numbers.Append(' ');
            numbers.Append(i);
            if (row.Servants != null)
                for (var s = 0; s < row.Servants.Count; s++)
                    if (row.Servants[s].Index == i)
                    {
                        if (names.Length > 0)
                            names.Append(", ");
                        names.Append(row.Servants[s].Name);
                    }
        }
        var sig = plot + ":" + numbers;
        if (sig == _lastArmed)
            return;
        try
        {
            var dir = Path.GetDirectoryName(MailboxPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var id = "hc-" + DateTime.UtcNow.Ticks;
            var json = "{\"id\":\"" + id + "\",\"op\":\"hunt\",\"plot\":" + plot
                + ",\"name\":\"" + numbers + "\"}";
            File.WriteAllText(MailboxPath, json);
            _lastArmed = sig;
            _status = "3. Click a colored territory to send " + names + ".";
        }
        catch (Exception ex)
        {
            _status = "Could not talk to the server.";
            Plugin.Logger?.LogDebug("HuntsAndRifts hunt mailbox: " + ex.Message);
        }
    }

    static void RefreshJson()
    {
        var now = Time.unscaledTime;
        if (now < _nextRead)
            return;
        _nextRead = now + 0.5f;
        try
        {
            if (!File.Exists(JsonPath))
                return;
            var text = File.ReadAllText(JsonPath);
            ParsePlots(text);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts throne json: " + ex.Message);
        }
    }

    static void ParsePlots(string text)
    {
        Plots.Clear();
        if (string.IsNullOrEmpty(text))
            return;
        var idx = 0;
        while (true)
        {
            var plotAt = text.IndexOf("\"plot\":", idx, StringComparison.Ordinal);
            if (plotAt < 0)
                break;
            var numStart = plotAt + 7;
            var numEnd = numStart;
            while (numEnd < text.Length && (char.IsDigit(text[numEnd]) || text[numEnd] == '-'))
                numEnd++;
            if (!int.TryParse(text.Substring(numStart, numEnd - numStart), out var plot))
            {
                idx = numEnd;
                continue;
            }
            var here = text.IndexOf("\"here\":true", plotAt, StringComparison.Ordinal) >= 0
                && text.IndexOf("\"here\":true", plotAt, StringComparison.Ordinal) < plotAt + 80;
            var label = SliceString(text, "\"label\":", plotAt);
            var servants = new List<ServantRow>();
            var servAt = text.IndexOf("\"servants\":[", plotAt, StringComparison.Ordinal);
            var nextPlot = text.IndexOf("\"plot\":", numEnd, StringComparison.Ordinal);
            var blockEnd = nextPlot < 0 ? text.Length : nextPlot;
            if (servAt >= 0 && servAt < blockEnd)
            {
                var cursor = servAt;
                while (cursor < blockEnd)
                {
                    var iAt = text.IndexOf("\"i\":", cursor, StringComparison.Ordinal);
                    if (iAt < 0 || iAt >= blockEnd)
                        break;
                    var iStart = iAt + 4;
                    var iEnd = iStart;
                    while (iEnd < text.Length && char.IsDigit(text[iEnd]))
                        iEnd++;
                    int.TryParse(text.Substring(iStart, iEnd - iStart), out var si);
                    var name = SliceString(text, "\"name\":", iAt);
                    servants.Add(new ServantRow { Index = si, Name = name });
                    cursor = iEnd + 1;
                }
            }
            Plots.Add(new PlotRow { Plot = plot, Here = here, Label = string.IsNullOrEmpty(label) ? "plot " + plot : label, Servants = servants });
            idx = numEnd + 1;
        }
    }

    static string SliceString(string text, string key, int from)
    {
        var at = text.IndexOf(key, from, StringComparison.Ordinal);
        if (at < 0)
            return "";
        var q1 = text.IndexOf('"', at + key.Length);
        if (q1 < 0)
            return "";
        var q2 = text.IndexOf('"', q1 + 1);
        if (q2 < 0)
            return "";
        return text.Substring(q1 + 1, q2 - q1 - 1);
    }

    static void EnsureStyles()
    {
        if (_label != null)
            return;
        try
        {
            _label = new GUIStyle
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
                richText = false,
                normal = { textColor = new Color(0.96f, 0.90f, 0.72f, 1f) }
            };
            _button = new GUIStyle
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.96f, 0.90f, 0.72f, 1f) }
            };
            _statusStyle = new GUIStyle
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                richText = false,
                normal = { textColor = new Color(0.85f, 0.78f, 0.55f, 1f) }
            };
            _bg = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _bg.SetPixel(0, 0, new Color(0.04f, 0.05f, 0.07f, 0.82f));
            _bg.Apply();
            _bg.hideFlags = HideFlags.HideAndDontSave;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts throne style: " + ex.Message);
        }
    }
}
