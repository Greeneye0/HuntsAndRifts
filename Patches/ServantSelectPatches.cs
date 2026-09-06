using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using ProjectM.UI;

namespace HuntsAndRifts;

/// <summary>
/// "Choose a Servant" dialog: append the time left to the red issue line on Away on Hunt
/// entries ("Away on Hunt  1h 30m") and on recuperating entries when the coffin knows it.
/// Same clock as the throne Servants list; the vanilla text is re-applied each LateUpdate.
/// </summary>
[HarmonyPatch]
internal static class ServantSelectPatches
{
    private static readonly Regex ClockSuffix = new(
        @"\s+\d+h(?: \d+m)?$|\s+\d+m$|\s+\d+s$",
        RegexOptions.Compiled);
    // IssueText pointer -> (servant name, on hunt?)
    private static readonly Dictionary<IntPtr, (string name, bool hunt)> Tracked = new();

    [HarmonyPatch(typeof(ServantSelectEntry), nameof(ServantSelectEntry.RefreshData))]
    [HarmonyPostfix]
    private static void RefreshDataPostfix(ServantSelectEntry __0, ServantSelectEntry.Data __1)
    {
        try
        {
            var entry = __0;
            if (entry == null)
                return;
            var issue = entry.IssueText;
            if (issue == null)
                return;
            UiTextState.Track(issue.Text);
            var data = __1;
            var state = data.State;
            var name = data.Name;
            if (state == ServantSelectEntry.State.AwayOnHunt || state == ServantSelectEntry.State.IsRecuperating)
            {
                Tracked[issue.Pointer] = (name, state == ServantSelectEntry.State.AwayOnHunt);
                Apply(issue, name, state == ServantSelectEntry.State.AwayOnHunt);
            }
            else
            {
                Tracked.Remove(issue.Pointer);
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts select refresh: " + ex.Message);
        }
    }

    [HarmonyPatch(typeof(LocalizedText), nameof(LocalizedText.LateUpdate))]
    [HarmonyPostfix]
    private static void LateUpdatePostfix(LocalizedText __instance)
    {
        try
        {
            if (Tracked.Count == 0 || __instance == null)
                return;
            if (!Tracked.TryGetValue(__instance.Pointer, out var t))
                return;
            Apply(__instance, t.name, t.hunt);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts select late: " + ex.Message);
        }
    }

    [HarmonyPatch(typeof(MapMenuMapper), "OnStopRunning")]
    [HarmonyPostfix]
    internal static void MapMenuStopPostfix()
    {
        Tracked.Clear();
    }

    private static void Apply(LocalizedText issue, string name, bool hunt)
    {
        if (issue == null || string.IsNullOrEmpty(name))
            return;
        double remaining;
        var have = hunt
            ? HuntReader.TryGetHuntRemaining(name, out remaining)
            : HuntReader.TryGetInjuryRemaining(name, out remaining);
        if (!have)
            return;
        var baseText = issue.GetText();
        if (string.IsNullOrEmpty(baseText))
            return;
        baseText = ClockSuffix.Replace(baseText, string.Empty).TrimEnd();
        var combined = baseText + "  " + HuntReader.FormatRemaining(remaining);
        if (issue.GetText() == combined)
            return;
        issue.ForceSet(combined);
    }
}
