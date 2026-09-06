using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace HuntsAndRifts;

[BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
[BepInProcess("VRising.exe")]
public class Plugin : BasePlugin
{
    internal const string GUID = PluginInfo.GUID;
    internal const string NAME = PluginInfo.Name;
    internal const string VERSION = PluginInfo.Version;

    internal static Plugin Instance { get; private set; }
    internal static ManualLogSource Logger { get; private set; }
    internal static ConfigEntry<bool> IncludeTestTiers { get; private set; }
    internal static ConfigEntry<bool> NoMapTip { get; private set; }
    internal static ConfigEntry<int> WaitMinorSeconds { get; private set; }
    internal static ConfigEntry<int> WaitMajorSeconds { get; private set; }

    private Harmony _harmony;

    public override void Load()
    {
        Instance = this;
        Logger = Log;

        if (UnityEngine.Application.productName == "VRisingServer")
        {
            Log.LogInfo("HuntsAndRifts is a client-only plugin; not loading on VRisingServer.");
            return;
        }

        IncludeTestTiers = Config.Bind(
            "Rifts",
            "IncludeTestTiers",
            false,
            "When false (default), overlay shows only live rift tiers the client actually has. When true, also fill missing T1/T2 rows with TEST countdowns.");

        NoMapTip = Config.Bind(
            "Map",
            "no_map_tip",
            true,
            "When true (default), hide the 'Servant Hunts' help box (\"Maximize resource gathering...\") in the map's servant view.");

        WaitMinorSeconds = Config.Bind("Rifts", "observed_wait_minor_seconds", 6000,
            "Full wait between T1 rifts as last observed (drives the T1 countdown bar). Updated automatically.");
        WaitMajorSeconds = Config.Bind("Rifts", "observed_wait_major_seconds", 6000,
            "Full wait between T2 rifts as last observed (drives the T2 countdown bar). Updated automatically.");

        MapRiftOverlay.Spawn();
        _harmony = new Harmony(GUID);
        _harmony.PatchAll(Assembly.GetExecutingAssembly());
        Log.LogInfo("HuntsAndRifts " + VERSION + " loaded (client). Hunt remaining on the throne Servants list; pickup floaters show bag total; map overlay shows T1 and T2 Mortium rifts under the rift card.");
    }

    public override bool Unload()
    {
        ServantListPatches.MapMenuStopPostfix();
        ServantSelectPatches.MapMenuStopPostfix();
        ServantInfoReader.Clear();
        MapWarEventPatches.RestoreTip();
        HuntMapFocus.Clear();
        RiftCardExtension.Reset();
        MapRiftOverlay.DestroyHost();
        _harmony?.UnpatchSelf();
        return true;
    }
}

internal static class PluginInfo
{
    public const string GUID = "fangly.HuntsAndRifts";
    public const string Name = "HuntsAndRifts";
    public const string Version = "1.0.0";
}
