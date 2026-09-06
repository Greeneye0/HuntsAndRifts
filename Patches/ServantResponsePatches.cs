using HarmonyLib;
using ProjectM.UI;
using Unity.Entities;

namespace HuntsAndRifts;

[HarmonyPatch(typeof(ServantInfoEventSystem_Client), nameof(ServantInfoEventSystem_Client.Refresh))]
internal static class ServantResponsePatches
{
    [HarmonyPrefix]
    private static void Prefix(Entity __0)
    {
        try { ServantInfoReader.BeginThrone(__0); }
        catch (System.Exception ex)
        {
            ServantInfoReader.Clear();
            Plugin.Logger?.LogWarning("HuntsAndRifts servant response reset: " + ex.Message);
        }
    }
}
