using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HuntsAndRifts;

internal static class ServantScroll
{
    private static RectTransform _owner, _root, _content;
    private static ScrollRect _scroll;
    private static Scrollbar _bar;
    private static readonly Dictionary<IntPtr, RectSnapshot> Rows = new();

    internal static Transform OriginalParent(Transform parent) =>
        _content != null && parent == _content ? _owner : parent;

    private static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    internal static void Prepare(RectTransform owner, List<Transform> rows)
    {
        if (_owner != null && _owner != owner) Reset();
        if (_root == null)
        {
            _owner = owner;
            _root = Rect("HuntsAndRifts_Scroll", owner);
            _root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var hit = _root.gameObject.AddComponent<Image>();
            hit.color = Color.clear; hit.raycastTarget = true;
            var viewport = Rect("Viewport", _root);
            viewport.offsetMax = new Vector2(-14f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _content = Rect("Content", viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = Vector2.one;
            _content.pivot = new Vector2(0f, 1f);
            var rail = Rect("Scrollbar", _root);
            rail.anchorMin = new Vector2(1f, 0f);
            rail.offsetMin = new Vector2(-10f, 8f);
            var grid = owner.GetComponent<GridLayoutGroup>();
            rail.offsetMax = new Vector2(0f, -Mathf.Max(0f, grid != null ? grid.padding.top - 14f : 0f));
            var railImage = rail.gameObject.AddComponent<Image>();
            railImage.color = new Color(0.05f, 0.06f, 0.08f, 0.9f);
            var handle = Rect("Handle", rail);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = new Color(0.55f, 0.51f, 0.43f, 1f);
            _bar = rail.gameObject.AddComponent<Scrollbar>();
            _bar.handleRect = handle; _bar.targetGraphic = handleImage;
            _bar.direction = Scrollbar.Direction.BottomToTop;
            _scroll = _root.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport; _scroll.content = _content;
            _scroll.horizontal = false; _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30f;
            _scroll.verticalScrollbar = _bar;
            // ScrollRect owns visibility. Toggling activeSelf in Size fought its
            // LateUpdate (Permanent defaults to showing it), flashing on short lists.
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            _scroll.verticalNormalizedPosition = 1f;
        }
        foreach (var row in rows)
        {
            var rt = row.GetComponent<RectTransform>();
            if (rt == null) continue;
            if (!Rows.ContainsKey(row.Pointer)) Rows[row.Pointer] = new RectSnapshot(rt);
            if (row.parent != _content) row.SetParent(_content, false);
        }
        // Unity draws later siblings on top. Reparenting headers appends them after
        // existing servants, so order only AFTER every row shares the content parent.
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].GetSiblingIndex() != i) rows[i].SetSiblingIndex(i);
    }

    internal static float Size(float total)
    {
        var canvas = _owner.GetComponentInParent<Canvas>();
        var scale = canvas != null ? Mathf.Max(0.1f, canvas.scaleFactor) : 1f;
        var visible = ServantScrollPolicy.VisibleHeight(total, Screen.height, scale);
        _content.sizeDelta = new Vector2(0f, total);
        if (total <= visible + 1f) _content.anchoredPosition = Vector2.zero;
        return visible;
    }

    internal static void Reset()
    {
        if (_scroll != null) _scroll.StopMovement();
        foreach (var row in Rows.Values) row.Restore();
        Rows.Clear();
        if (_root != null)
        {
            _root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(_root.gameObject);
        }
        _owner = _root = _content = null; _scroll = null; _bar = null;
    }
}
