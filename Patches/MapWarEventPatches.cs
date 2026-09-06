using HarmonyLib;
using ProjectM.UI;

namespace HuntsAndRifts;

// 1.1.2: SAFE both-tiers view. Do NOT clone MapWarEventInfoEntry. Do NOT call SetData.
// 1.1.0 Harmony postfix on MapMenuMapper.UpdateWarEventInfo cloned the vanilla widget
// then SetData(..., ref TimeLocalizationKeys, ...) and crashed map open:
// MarshalDirectiveException cannot marshal parameter 2 (non-blittable generic).
// Burst LambdaJob_1 was never patched and MUST stay unpatched (LookDown lesson).
[HarmonyPatch]
internal static class MapWarEventPatches
{
    [HarmonyPatch(typeof(MapMenuMapper), nameof(MapMenuMapper.OnStartRunning))]
    [HarmonyPostfix]
    private static void OnStartRunningPostfix()
    {
        MapRiftOverlay.SetMapOpen(true);
    }

    [HarmonyPatch(typeof(MapMenuMapper), nameof(MapMenuMapper.OnUpdate))]
    [HarmonyPostfix]
    private static void OnUpdatePostfix(MapMenuMapper __instance)
    {
        // Managed system OnUpdate only — not LambdaJob_1 / Burst.
        MapRiftOverlay.SetMapOpen(true);
        MapRiftOverlay.CaptureWidget(__instance);
        HuntMapFocus.SetMapper(__instance);
        RiftReader.RefreshIfNeeded();
        ServantListPatches.Tick();
        TooltipDecor.Tick(__instance);
        HideMapTipIfConfigured(__instance);
    }

    private static bool _loggedTip;
    private static UnityEngine.GameObject _hiddenTip;
    internal static void RestoreTip()
    {
        if (_hiddenTip != null) _hiddenTip.SetActive(true);
        _hiddenTip = null;
    }

    // no_map_tip: the "Servant Hunts" help box is MapMenu.Parent_ServantThroneHelper; vanilla
    // re-activates it while in the servant view, so it is hidden again after every update.
    private static void HideMapTipIfConfigured(MapMenuMapper mapper)
    {
        try
        {
            if (Plugin.NoMapTip == null || !Plugin.NoMapTip.Value)
            {
                RestoreTip();
                return;
            }
            var menu = mapper.GetMapMenu();
            if (menu == null)
                return;
            var tip = menu.Parent_ServantThroneHelper;
            if (tip == null)
                return;
            if (tip.activeSelf)
            {
                _hiddenTip = tip;
                tip.SetActive(false);
                if (!_loggedTip)
                {
                    _loggedTip = true;
                    Plugin.Logger?.LogInfo("HuntsAndRifts: no_map_tip hides '" + tip.name + "'.");
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts map tip: " + ex.Message);
        }
    }

    [HarmonyPatch(typeof(MapMenuMapper), nameof(MapMenuMapper.OnStopRunning))]
    [HarmonyPostfix]
    private static void OnStopRunningPostfix()
    {
        RestoreTip();
        MapRiftOverlay.SetMapOpen(false);
        HuntMapFocus.Clear();
    }
}
