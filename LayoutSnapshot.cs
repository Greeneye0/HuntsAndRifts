using UnityEngine;
using UnityEngine.UI;

namespace HuntsAndRifts;

internal sealed class LayoutSnapshot
{
    private readonly LayoutElement _element;
    private readonly bool _owned, _enabled;
    private readonly float _preferred, _minimum;
    internal LayoutElement Element => _element;
    internal LayoutSnapshot(GameObject go)
    {
        _element = go.GetComponent<LayoutElement>();
        _owned = _element == null;
        if (_owned) _element = go.AddComponent<LayoutElement>();
        _enabled = _element.enabled; _preferred = _element.preferredHeight; _minimum = _element.minHeight;
    }
    internal void Restore()
    {
        if (_element == null) return;
        _element.enabled = _enabled; _element.preferredHeight = _preferred; _element.minHeight = _minimum;
        if (_owned) { _element.enabled = false; Object.Destroy(_element); }
    }
}
