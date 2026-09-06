using ProjectM.Shared.WarEvents;
using ProjectM.UI;
using UnityEngine;
using UnityEngine.UI;

namespace HuntsAndRifts;

internal static class RiftRowBadge
{
    private static Image _badge;
    private static Image _header;
    private static bool _headerWasActive;

    internal static void Apply(MapWarEventInfoEntry info, RiftTrack? track)
    {
        var card = info.GetComponent<RectTransform>();
        var timer = info.TimerText?.Text;
        if (card == null || timer == null || track == null)
        {
            Reset();
            return;
        }
        if (_badge != null && _badge.transform.parent.Pointer != card.Pointer)
            Reset();
        if (_badge == null)
        {
            var go = new GameObject("HuntsAndRifts_PrimaryRiftBadge");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(card, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(52f, 52f);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            _badge = go.AddComponent<Image>();
            _badge.raycastTarget = false;
            _badge.preserveAspect = true;
            _header = info.WarEventIcon;
            _headerWasActive = _header != null && _header.gameObject.activeSelf;
        }
        // Follow the real timer geometry after layout, including card resizing. The symbol
        // occupies the row's right margin, vertically between its status and timer lines.
        var timerCenter = timer.rectTransform.TransformPoint(timer.rectTransform.rect.center);
        var local = card.InverseTransformPoint(timerCenter);
        _badge.rectTransform.anchoredPosition = new Vector2(-14f,
            local.y - card.rect.yMin + 8f);
        var t = track.Value;
        _badge.sprite = t.Type switch
        {
            WarEventType.Minor => info.WarEvent_Minor_Icon,
            WarEventType.Major => info.WarEvent_Major_Icon,
            WarEventType.Primal => info.WarEvent_Primal_Icon,
            _ => null
        };
        _badge.gameObject.SetActive(t.IsActive && _badge.sprite != null);
        if (_header != null && _header.gameObject.activeSelf)
            _header.gameObject.SetActive(false);
    }

    internal static void Reset()
    {
        if (_badge != null)
        {
            _badge.gameObject.SetActive(false);
            Object.Destroy(_badge.gameObject);
        }
        if (_header != null)
            _header.gameObject.SetActive(_headerWasActive);
        _badge = null;
        _header = null;
    }
}
