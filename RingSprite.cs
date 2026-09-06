using System;
using UnityEngine;

namespace HuntsAndRifts;

/// <summary>Runtime-generated anti-aliased ring sprite for "specialisation matched" markers.</summary>
internal static class RingSprite
{
    private static Sprite _sprite;
    private static bool _tried;

    internal static Sprite Get()
    {
        if (_sprite != null || _tried)
            return _sprite;
        _tried = true;
        try
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var c = (size - 1) * 0.5f;
            const float outer = 30f;
            const float thickness = 5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    // 1 inside the band, soft 1px edges.
                    var a = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - (outer - thickness));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _sprite.hideFlags = HideFlags.HideAndDontSave;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts ring sprite: " + ex.Message);
            _sprite = null;
        }
        return _sprite;
    }
}
