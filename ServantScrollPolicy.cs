using System;

namespace HuntsAndRifts;
internal static class ServantScrollPolicy
{
    internal static float VisibleHeight(float total, float screenHeight, float scale) =>
        Math.Min(Math.Max(0, total), Math.Max(120f, screenHeight / Math.Max(0.1f, scale) * 0.6f));
}
