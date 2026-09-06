using System;
using System.Text.RegularExpressions;

namespace HuntsAndRifts;

internal static class RiftTiming
{
    internal static double SecondsUntil(long targetTicks, long nowTicks) =>
        targetTicks <= 0 ? -1 : (targetTicks - nowTicks) / (double)TimeSpan.TicksPerSecond;
    // Do not interpret a rendered HuntsAndRifts countdown as a fresh vanilla reading.
    internal static bool IsOwnText(string text) => Regex.IsMatch(text ?? "",
        @"\bT[12]\b|\bPrimal\b", RegexOptions.CultureInvariant);

    internal static double Age(double seconds, double capturedAt, double now) =>
        seconds < 0 ? -1 : Math.Max(0, seconds - Math.Max(0, now - capturedAt));
}

internal sealed class RiftTimerSample
{
    private string _event;
    private double _seconds = -1;
    private double _at;

    internal void Reset() { _event = null; _seconds = -1; }

    internal double Read(string eventKey, double freshSeconds, double now)
    {
        if (_event != eventKey)
        {
            Reset();
            _event = eventKey;
        }
        if (freshSeconds >= 0)
        {
            _seconds = freshSeconds;
            _at = now;
        }
        return RiftTiming.Age(_seconds, _at, now);
    }
}
