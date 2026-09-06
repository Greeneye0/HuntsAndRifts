using System;
using ProjectM.Shared.WarEvents;
using ProjectM.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HuntsAndRifts;

/// <summary>
/// Second timer row under the vanilla Rift Incursions card: label (tier), timer text and a
/// progress bar cloned from the card's own bar, so the card shows both tiers, each with
/// "active: time left" or "next in ...". The card is shifted up by the row height and the
/// My Territories panel above it by the same amount (vanilla positions the card every frame;
/// the shift is applied after). The row is a plain child of the card; nothing vanilla is
/// re-parented and SetData is never called.
/// </summary>
internal static class RiftCardExtension
{
    private const string RowName = "HuntsAndRifts_RiftRow";
    private static GameObject _row;
    private static RectTransform _rowRect;
    private static Image _rowBg;
    private static TextMeshProUGUI _label;
    private static TextMeshProUGUI _timer;
    private static Image _fill;
    private static Image _badge;
    private static float _height;
    private static float _baseHeight;
    private static bool _parentIsLayout;
    private static bool _parentControlsHeight;
    private static RectTransform _territories;
    private static float _territoriesShift;
    private static bool _loggedOnce;
    private static bool _built;
    private static IntPtr _cardPtr;
    private static RectSnapshot _cardRectSnapshot;
    private static LayoutSnapshot _cardLayoutSnapshot;
    private static RectTransform _card;
    private static Vector2 _lastShiftedPosition;

    internal static void Reset()
    {
        _cardLayoutSnapshot?.Restore();
        _cardRectSnapshot?.Restore();
        _cardLayoutSnapshot = null;
        _cardRectSnapshot = null;
        _card = null;
        _built = false;
        try
        {
            if (_territories != null && _territoriesShift != 0f)
                _territories.anchoredPosition -= new Vector2(0f, _territoriesShift);
        }
        catch
        {
        }
        _territories = null;
        _territoriesShift = 0f;
        _retries = 0;
        try
        {
            if (_row != null && _row.activeSelf)
                _row.SetActive(false);
            if (_row != null) UnityEngine.Object.Destroy(_row);
            _row = null;
        }
        catch
        {
        }
    }

    /// <param name="secondary">Track for the second row, or null to hide the row.</param>
    internal static void Apply(MapWarEventInfoEntry info, MapMenu menu, RiftTrack? secondary)
    {
        try
        {
            if (info == null)
                return;
            var card = info.GetComponent<RectTransform>();
            if (card == null)
            {
                Fail("card has no RectTransform");
                return;
            }
            if (secondary == null)
            {
                if (_built) Reset();
                return;
            }
            if (!_built || _row == null || _cardPtr != card.Pointer)
            {
                if (_built) Reset();
                if (!Build(info, card))
                    return;
                _cardPtr = card.Pointer;
                _built = true;
            }
            if (!_row.activeSelf)
                _row.SetActive(true);

            // Make room: the card sits in a vertical layout with the territories panel, so a
            // taller preferred height pushes the panel up. If no layout group controls it, fall
            // back to shifting the card and the panel manually.
            if (_parentIsLayout)
            {
                var le = card.GetComponent<LayoutElement>();
                if (le == null)
                    le = card.gameObject.AddComponent<LayoutElement>();
                var want = _baseHeight + _height;
                if (Mathf.Abs(le.preferredHeight - want) > 0.5f)
                {
                    le.preferredHeight = want;
                    le.minHeight = want;
                }
                if (Mathf.Abs(card.sizeDelta.y - want) > 0.5f && !_parentControlsHeight)
                    card.sizeDelta = new Vector2(card.sizeDelta.x, want);
            }
            else
            {
                if (card.anchoredPosition != _lastShiftedPosition)
                {
                    card.anchoredPosition += new Vector2(0f, _height);
                    _lastShiftedPosition = card.anchoredPosition;
                }
                ShiftTerritories(menu);
            }

            var t = secondary.Value;
            var name = TierName(t.Type) + (t.Level > 0 ? " (" + t.Level + "+)" : "");
            var timerText = t.IsActive
                ? (t.RemainingSeconds < 0 ? name + " active" : name + " active: " + Format(t.RemainingSeconds))
                : t.NextInSeconds < 0 ? name + " awaiting schedule" : name + " next in " + Format(t.NextInSeconds);
            if (_timer != null && _timer.text != timerText)
                _timer.text = timerText;
            if (_label != null && _label.text != "Status")
                _label.text = "Status";
            if (_fill != null)
            {
                var fill = Fill(t);
                if (Mathf.Abs(_fill.fillAmount - fill) > 0.002f)
                    _fill.fillAmount = fill;
            }
            if (_badge != null)
            {
                Sprite sprite = null;
                try
                {
                    sprite = t.Type switch
                    {
                        WarEventType.Minor => info.WarEvent_Minor_Icon,
                        WarEventType.Major => info.WarEvent_Major_Icon,
                        WarEventType.Primal => info.WarEvent_Primal_Icon,
                        _ => null
                    };
                }
                catch
                {
                }
                if (sprite != null && _badge.sprite != sprite)
                    _badge.sprite = sprite;
                var show = t.IsActive && sprite != null;
                if (_badge.gameObject.activeSelf != show)
                    _badge.gameObject.SetActive(show);
            }
        }
        catch (Exception ex)
        {
            if (!_loggedError)
            {
                _loggedError = true;
                Plugin.Logger?.LogInfo("HuntsAndRifts rift row failed: " + ex);
            }
        }
    }

    private static bool _loggedError;
    private static bool _loggedFail;
    private static int _retries;

    private static void Fail(string why)
    {
        if (_loggedFail)
            return;
        _loggedFail = true;
        Plugin.Logger?.LogInfo("HuntsAndRifts rift row not built: " + why + ".");
    }

    // Observed wait per tier: when a tier goes inactive, the countdown it reports then is the
    // full wait; the bar fills as that countdown runs down. Settings-derived gaps did not match
    // the real schedule (T2 had just ended yet its bar showed half).
    private static readonly System.Collections.Generic.Dictionary<WarEventType, (bool wasActive, double period)> Waits = new();

    internal static void ResetTiming() => Waits.Clear();

    internal static float Fill(RiftTrack t)
    {
        Waits.TryGetValue(t.Type, out var w);
        if (t.IsActive)
        {
            Waits[t.Type] = (true, 0);
            if (t.RemainingSeconds < 0)
                return 1f; // end time unknown (tier detected from open gates)
            if (RiftReader.TryGetSchedule(t.Type, out var duration, out _) && duration > 0)
                return Mathf.Clamp01((float)(t.RemainingSeconds / duration));
            return 1f;
        }
        if (t.NextInSeconds < 0)
            return 0f;
        var next = t.NextInSeconds;
        // Normalize from an observed countdown, never an old inferred/persisted interval.
        // On joining mid-cycle the bar starts at zero and tracks the remaining wait.
        var period = w.wasActive || w.period <= 0 ? next : Math.Max(w.period, next);
        Waits[t.Type] = (false, period);
        if (period <= 0)
            return 0f;
        return Mathf.Clamp01((float)(1.0 - next / period));
    }

    private static string Format(double seconds)
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

    private static string TierName(WarEventType t) => t switch
    {
        WarEventType.Minor => "T1",
        WarEventType.Major => "T2",
        WarEventType.Primal => "Primal",
        _ => t.ToString()
    };

    private static bool Build(MapWarEventInfoEntry info, RectTransform card)
    {
        var fillImg = info.FillImage;
        var timerLoc = info.TimerText;
        if (fillImg == null || timerLoc == null || timerLoc.Text == null)
        {
            Fail("FillImage " + (fillImg != null) + ", TimerText " + (timerLoc != null) + ", TimerText.Text " + (timerLoc != null && timerLoc.Text != null));
            return false;
        }
        var barRoot = fillImg.transform.parent as RectTransform;
        if (barRoot == null)
            barRoot = fillImg.transform.parent != null ? fillImg.transform.parent.GetComponent<RectTransform>() : null;
        if (barRoot == null)
        {
            Fail("bar parent has no RectTransform");
            return false;
        }
        var timerRt = timerLoc.Text.rectTransform;

        // Measure the vanilla status section through transform positions (these marshal
        // correctly, unlike the corner array). Everything below is in card-local space.
        var cardLeft = -card.pivot.x * card.rect.width;
        Rect LocalRect(RectTransform rt)
        {
            var pos = card.InverseTransformPoint(rt.position);
            var w = rt.rect.width;
            var h = rt.rect.height;
            var left = pos.x - rt.pivot.x * w;
            var bottom = pos.y - rt.pivot.y * h;
            return new Rect(left - cardLeft, bottom, w, h);
        }
        // SubHeaderText is not the visible "Status" label (it measures 0 wide, below the timer),
        // so the label is derived from the timer: same left edge, one small line above it.
        TMP_Text labelSrc = null;
        var barL = LocalRect(barRoot);
        var timerL = LocalRect(timerRt);
        var labelL = new Rect(timerL.x, timerL.yMax + 2f, 200f, 16f);
        var cardW = card.rect.width > 50f ? card.rect.width : 400f;
        var sane = barL.width > 10f && timerL.height > 4f
                   && Mathf.Abs(barL.y) < 2000f && Mathf.Abs(timerL.y) < 2000f;
        if (_retries == 0)
            Plugin.Logger?.LogInfo("HuntsAndRifts rift row: measured bar " + barL + " timer " + timerL + " label " + labelL + " card " + card.rect.size + " sane " + sane);
        if (!sane)
        {
            _retries++;
            if (_retries < 30)
                return false;
            // Fallback geometry.
            labelL = new Rect(34f, 60f, 200f, 16f);
            timerL = new Rect(24f, 36f, cardW - 48f, 24f);
            barL = new Rect(0f, 8f, cardW, 20f);
        }
        // Vertical stack from the vanilla offsets: label top -> bar bottom, plus padding.
        const float pad = 6f;
        var top = Mathf.Max(labelL.yMax, timerL.yMax, barL.yMax);
        var bottom = Mathf.Min(labelL.y, timerL.y, barL.y);
        _height = (top - bottom) + 2f * pad;
        var labelR = new Rect(labelL.x, pad + (labelL.y - bottom), labelL.width, labelL.height);
        var timerR = new Rect(timerL.x, pad + (timerL.y - bottom), Mathf.Max(timerL.width, 240f), timerL.height);
        var barR = new Rect(barL.x, pad + (barL.y - bottom), barL.width, barL.height);
        _baseHeight = card.rect.height;
        _card = card;
        _cardRectSnapshot = new RectSnapshot(card);
        _cardLayoutSnapshot = new LayoutSnapshot(card.gameObject);
        _lastShiftedPosition = new Vector2(float.NaN, float.NaN);

        var existing = card.Find(RowName);
        if (existing != null)
            UnityEngine.Object.Destroy(existing.gameObject);

        _row = new GameObject(RowName);
        _rowRect = _row.AddComponent<RectTransform>();
        _rowRect.SetParent(card, false);
        // Occupies the bottom `_height` of the (now taller) card.
        _rowRect.anchorMin = new Vector2(0f, 0f);
        _rowRect.anchorMax = new Vector2(1f, 0f);
        _rowRect.pivot = new Vector2(0.5f, 0f);
        _rowRect.sizeDelta = new Vector2(0f, _height);
        _rowRect.anchoredPosition = Vector2.zero;
        _rowRect.localScale = Vector3.one;

        // Background: same sprite/colour as the card's own background when available.
        _rowBg = _row.AddComponent<Image>();
        _rowBg.raycastTarget = false;
        var cardImg = card.GetComponent<Image>();
        if (cardImg != null && cardImg.sprite != null)
        {
            _rowBg.sprite = cardImg.sprite;
            _rowBg.type = cardImg.type;
            _rowBg.color = cardImg.color;
            _rowBg.material = cardImg.material;
        }
        else
        {
            _rowBg.color = new Color(0.06f, 0.07f, 0.09f, 0.92f);
        }

        // Label ("Status"): small text above the timer, aligned with the timer's left edge.
        _label = MakeText("Label", timerLoc.Text, 0.72f);
        var lr = _label.rectTransform;
        lr.anchorMin = new Vector2(0f, 1f);
        lr.anchorMax = new Vector2(0f, 1f);
        lr.pivot = new Vector2(0f, 1f);
        lr.sizeDelta = new Vector2(Mathf.Max(labelR.width, 120f), labelR.height);
        lr.anchoredPosition = new Vector2(labelR.xMin, -(_height - labelR.yMax));
        _label.color = new Color(0.62f, 0.62f, 0.62f, 1f);
        _label.fontStyle = TMPro.FontStyles.Normal;

        // Timer text at the same place as the vanilla timer.
        _timer = MakeText("Timer", timerLoc.Text, 1f);
        var tr = _timer.rectTransform;
        tr.anchorMin = new Vector2(0f, 1f);
        tr.anchorMax = new Vector2(0f, 1f);
        tr.pivot = new Vector2(0f, 1f);
        tr.sizeDelta = new Vector2(timerR.width, timerR.height);
        tr.anchoredPosition = new Vector2(timerR.xMin, -(_height - timerR.yMax));

        // Bar: clone of the vanilla bar hierarchy (plain UI images).
        var barClone = UnityEngine.Object.Instantiate(barRoot.gameObject, _row.transform, false);
        barClone.name = "Bar";
        var br = barClone.GetComponent<RectTransform>();
        br.anchorMin = new Vector2(0f, 1f);
        br.anchorMax = new Vector2(0f, 1f);
        br.pivot = new Vector2(0f, 1f);
        br.sizeDelta = new Vector2(barR.width, barR.height);
        br.anchoredPosition = new Vector2(barR.xMin, -(_height - barR.yMax));
        br.localScale = Vector3.one;
        _fill = null;
        var fillName = fillImg.name;
        foreach (var img in barClone.GetComponentsInChildren<Image>(true))
        {
            if (img.name == fillName)
            {
                _fill = img;
                break;
            }
        }
        if (_fill == null)
        {
            foreach (var img in barClone.GetComponentsInChildren<Image>(true))
            {
                if (img.type == Image.Type.Filled)
                {
                    _fill = img;
                    break;
                }
            }
        }
        foreach (var g in barClone.GetComponentsInChildren<Graphic>(true))
            g.raycastTarget = false;

        // Tier badge for this row (rifts can overlap, so each row carries its own symbol).
        try
        {
            var bgo = new GameObject("Badge");
            var brt2 = bgo.AddComponent<RectTransform>();
            brt2.SetParent(_row.transform, false);
            brt2.anchorMin = new Vector2(1f, 1f);
            brt2.anchorMax = new Vector2(1f, 1f);
            brt2.pivot = new Vector2(1f, 1f);
            var badgeSize = Mathf.Clamp(_height - 8f, 28f, 52f);
            brt2.sizeDelta = new Vector2(badgeSize, badgeSize);
            brt2.anchoredPosition = new Vector2(-14f, -4f);
            brt2.localScale = Vector3.one;
            _badge = bgo.AddComponent<Image>();
            _badge.raycastTarget = false;
            _badge.preserveAspect = true;
            bgo.SetActive(false);
        }
        catch
        {
            _badge = null;
        }

        _parentIsLayout = false;
        _parentControlsHeight = false;
        string parentInfo = "none";
        try
        {
            var parent = card.parent;
            if (parent != null)
            {
                var vlg = parent.GetComponent<VerticalLayoutGroup>();
                var hlg = parent.GetComponent<HorizontalOrVerticalLayoutGroup>();
                var lg = (HorizontalOrVerticalLayoutGroup)vlg ?? hlg;
                _parentIsLayout = lg != null;
                _parentControlsHeight = lg != null && lg.childControlHeight;
                parentInfo = parent.name + (lg != null ? " (layout, controlHeight " + lg.childControlHeight + ")" : " (no layout)");
            }
        }
        catch
        {
        }
        // Reset any stale preferred height from a previous build.
        try
        {
            var le0 = card.GetComponent<LayoutElement>();
            if (le0 != null && _parentIsLayout)
            {
                le0.preferredHeight = _baseHeight + _height;
                le0.minHeight = _baseHeight + _height;
            }
        }
        catch
        {
        }
        if (!_loggedOnce)
        {
            _loggedOnce = true;
            Plugin.Logger?.LogInfo("HuntsAndRifts rift row: height " + _height.ToString("0") + " (card base " + _baseHeight.ToString("0") + "), bar " + barR
                + ", timer " + timerR + ", fill " + (_fill != null ? _fill.name : "none") + ", parent " + parentInfo + ".");
        }
        return true;
    }

    private static TextMeshProUGUI MakeText(string name, TMP_Text template, float scale)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(_row.transform, false);
        rt.localScale = Vector3.one;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.richText = true;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        try
        {
            tmp.font = template.font;
            tmp.fontSharedMaterial = template.fontSharedMaterial;
            tmp.fontSize = template.fontSize * scale;
            tmp.color = template.color;
            tmp.fontStyle = template.fontStyle;
            tmp.characterSpacing = template.characterSpacing;
            tmp.alignment = template.alignment;
        }
        catch
        {
        }
        return tmp;
    }

    private static void ShiftTerritories(MapMenu menu)
    {
        try
        {
            if (_territories == null)
            {
                var grid = menu != null ? menu.Grid_TerritoryList : null;
                if (grid == null)
                    return;
                // Walk up to the panel: the first ancestor that is panel-sized (not the whole
                // map parent, which is what a plain "has an Image" test picked).
                Transform t = grid.transform.parent;
                RectTransform panel = null;
                var chain = new System.Text.StringBuilder();
                for (var i = 0; i < 8 && t != null; i++)
                {
                    var rt = t as RectTransform ?? t.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        chain.Append(t.name).Append(' ').Append(rt.rect.size).Append(" > ");
                        var h = rt.rect.height;
                        var w = rt.rect.width;
                        if (panel == null && h > 120f && h < 900f && w > 150f && w < 700f && t.GetComponent<Image>() != null)
                            panel = rt;
                    }
                    t = t.parent;
                }
                Plugin.Logger?.LogInfo("HuntsAndRifts rift row: territory list ancestors: " + chain);
                if (panel == null)
                {
                    Plugin.Logger?.LogInfo("HuntsAndRifts rift row: no panel-sized ancestor found; territories not shifted.");
                    _territories = null;
                    return;
                }
                _territories = panel;
                _territoriesShift = 0f;
                Plugin.Logger?.LogInfo("HuntsAndRifts rift row: shifting territories panel '" + panel.name + "' up by " + _height.ToString("0") + ".");
            }
            if (Mathf.Abs(_territoriesShift - _height) > 0.5f)
            {
                _territories.anchoredPosition += new Vector2(0f, _height - _territoriesShift);
                _territoriesShift = _height;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts rift row territories: " + ex.Message);
        }
    }
}
