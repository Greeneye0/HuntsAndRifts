using UnityEngine;

namespace HuntsAndRifts;

internal sealed class RectSnapshot
{
    private readonly RectTransform _rect;
    private readonly Transform _parent;
    private readonly int _sibling;
    private readonly Vector2 _min, _max, _pivot, _size, _position;
    private readonly Vector3 _scale;
    internal RectSnapshot(RectTransform rect)
    {
        _rect = rect; _parent = rect.parent; _sibling = rect.GetSiblingIndex();
        _min = rect.anchorMin; _max = rect.anchorMax; _pivot = rect.pivot;
        _size = rect.sizeDelta; _position = rect.anchoredPosition; _scale = rect.localScale;
    }
    internal void Restore()
    {
        if (_rect == null) return;
        if (_parent != null) _rect.SetParent(_parent, false);
        _rect.SetSiblingIndex(_sibling);
        _rect.anchorMin = _min; _rect.anchorMax = _max; _rect.pivot = _pivot;
        _rect.sizeDelta = _size; _rect.anchoredPosition = _position; _rect.localScale = _scale;
    }
}
