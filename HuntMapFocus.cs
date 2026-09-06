using System;
using ProjectM.UI;
using UnityEngine;

namespace HuntsAndRifts;

/// <summary>
/// Centres the open world map on a world-space point. Same maths as the vanilla
/// "centre on player" input: push the point through MapMenuMapper._WorldToAnchoredSpace
/// and store the negated anchored position as the zone offset. AdjustPan (vanilla,
/// next OnUpdate) clamps it and applies it to the map RectTransform. Zoom is kept.
/// </summary>
internal static class HuntMapFocus
{
    private static MapMenuMapper _mapper;
    /// <summary>Minimum map scale used when focusing a hunt (1 = native map texture size).</summary>
    private const float FocusScale = 1f;

    private static int _verifyFrame = -1;
    private static Vector2 _verifyOffset;
    private static ProjectM.Terrain.MapType _verifyType;

    internal static void SetMapper(MapMenuMapper mapper)
    {
        _mapper = mapper;
        if (_verifyFrame >= 0 && Time.frameCount >= _verifyFrame && mapper != null)
        {
            _verifyFrame = -1;
            try
            {
                var now = mapper.GetZoneOffsetAndScale(_verifyType, 1f);
                string rectInfo = "?";
                try
                {
                    var menu = mapper.GetMapMenu();
                    if (menu != null && menu.MapTexture != null)
                        rectInfo = menu.MapTexture.rectTransform.anchoredPosition + " size " + menu.MapTexture.rectTransform.rect.size;
                }
                catch
                {
                }
                Plugin.Logger?.LogInfo("HuntsAndRifts map focus check: wanted offset " + _verifyOffset + ", zone offset now " + now.Offset
                    + " scale " + now.Scale.ToString("0.###") + ", map rect " + rectInfo + ", pan " + mapper._PanAdjust);
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogDebug("HuntsAndRifts map focus check: " + ex.Message);
            }
        }
    }

    internal static void Clear()
    {
        _mapper = null;
    }

    internal static bool CenterOn(Vector2 worldXZ)
    {
        var mapper = _mapper;
        if (mapper == null)
            return false;
        try
        {
            var m = mapper._WorldToAnchoredSpace;
            if (IsZero(m))
                return false;
            var anchored = m.MultiplyPoint(new Vector3(worldXZ.x, 0f, worldXZ.y));
            if (float.IsNaN(anchored.x) || float.IsNaN(anchored.y)
                || float.IsInfinity(anchored.x) || float.IsInfinity(anchored.y))
                return false;

            var mapType = mapper._LastPlayerMapType;
            var current = mapper.GetZoneOffsetAndScale(mapType, 1f);
            // Vanilla AdjustPan clamps the offset so the map never leaves the window. When the
            // map is zoomed out to fit, that clamp is ~0 and centring is impossible (same as the
            // vanilla centre-on-player key). Zoom in to FocusScale first; the anchored position
            // scales with the texture, so scale the offset by the same factor.
            var targetScale = current.Scale;
            if (targetScale < FocusScale)
                targetScale = FocusScale;
            try
            {
                var menu = mapper.GetMapMenu();
                if (menu != null && menu.MaxZoomScale > 0f && targetScale > menu.MaxZoomScale)
                    targetScale = menu.MaxZoomScale;
            }
            catch
            {
            }
            var k = current.Scale > 0f ? targetScale / current.Scale : 1f;
            var offset = new Vector2(-anchored.x * k, -anchored.y * k);
            mapper.SetZoneOffsetAndScale(mapType, new OffsetAndScale
            {
                Offset = offset,
                Scale = targetScale
            });
            mapper._PanAdjust = Vector2.zero;
            _verifyFrame = Time.frameCount + 3;
            _verifyOffset = offset;
            _verifyType = mapType;
            Plugin.Logger?.LogInfo("HuntsAndRifts map focus: world " + worldXZ + " -> anchored " + anchored
                + " scale " + current.Scale.ToString("0.###") + " -> " + targetScale.ToString("0.###")
                + " offset " + offset + " (mapType " + mapType + ")");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts map focus: " + ex.Message);
            return false;
        }
    }

    private static bool IsZero(Matrix4x4 m)
    {
        return m.m00 == 0f && m.m01 == 0f && m.m02 == 0f && m.m03 == 0f
               && m.m10 == 0f && m.m11 == 0f && m.m12 == 0f && m.m13 == 0f;
    }
}
