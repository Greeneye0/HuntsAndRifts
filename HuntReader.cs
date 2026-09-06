using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Terrain;
using Stunlock.Core;
using Stunlock.Localization;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace HuntsAndRifts;

internal static class HuntReader
{
    private static float _nextRefresh;
    private static readonly Dictionary<string, double> HuntRemainingByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> HuntDestByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, string> DestByMission = new();
    private static readonly Dictionary<int, Vector2> PosByMission = new();
    private static readonly Dictionary<string, Vector2> HuntPosByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, double> InjuryRemainingByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, float> GearLevelByName = new(StringComparer.Ordinal);
    // Same data keyed by the servant entity's NetworkId ("a:b"), so two castles (or two servants)
    // sharing a name resolve to the right coffin when the throne response identifies them.
    private static readonly Dictionary<string, double> HuntRemainingById = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> HuntDestById = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Vector2> HuntPosById = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, double> InjuryRemainingById = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, float> GearLevelById = new(StringComparer.Ordinal);
    private static bool _netTypeReady;
    private static TypeIndex _networkIdIndex;

    private static bool Lookup<T>(Dictionary<string, T> byName, Dictionary<string, T> byId, string servantName, out T value)
    {
        if (ServantInfoReader.TryGetNetworkKey(servantName, out var key) && byId.TryGetValue(key, out value))
            return true;
        // A name-only fallback can select a coffin from another castle, even after an ID miss.
        value = default;
        return false;
    }

    private static unsafe string ServantNetworkKey(EntityManager em, Entity servant)
    {
        try
        {
            if (servant == Entity.Null)
                return null;
            if (!_netTypeReady)
            {
                _netTypeReady = true;
                try { _networkIdIndex = TypeManager.GetTypeIndex(Il2CppInterop.Runtime.Il2CppType.Of<ProjectM.Network.NetworkId>()); } catch { }
            }
            if (_networkIdIndex.Value == 0)
                return null;
            if (!em.HasComponent(servant, ComponentType.ReadOnly(_networkIdIndex)))
                return null;
            var raw = (long*)em.GetComponentDataRawRO(servant, _networkIdIndex);
            if (raw == null)
                return null;
            var a = raw[0];
            var b = *(uint*)((byte*)raw + 8); // Read exactly the 12-byte NetworkId.
            return (a != 0 || b != 0) ? a + ":" + b : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>ServantCoffinstation.ServantGearLevel for a servant name (the coffin's gear score).</summary>
    internal static bool TryGetGearLevel(string servantName, out float gearLevel)
    {
        RefreshIfNeeded();
        gearLevel = 0f;
        if (string.IsNullOrEmpty(servantName))
            return false;
        return Lookup(GearLevelByName, GearLevelById, servantName, out gearLevel);
    }
    private static bool _loggedMissingWorld;
    private static bool _loggedClockSanity;
    private static bool _loggedDest;

    internal static void RefreshIfNeeded()
    {
        var now = Time.unscaledTime;
        if (now < _nextRefresh)
            return;
        _nextRefresh = now + 1f;
        RefreshNow();
    }

    internal static bool TryGetHuntRemaining(string servantName, out double seconds)
    {
        RefreshIfNeeded();
        if (string.IsNullOrEmpty(servantName))
        {
            seconds = 0;
            return false;
        }
        return Lookup(HuntRemainingByName, HuntRemainingById, servantName, out seconds);
    }

    internal static bool TryGetHuntDest(string servantName, out string dest)
    {
        RefreshIfNeeded();
        dest = "";
        if (string.IsNullOrEmpty(servantName))
            return false;
        return Lookup(HuntDestByName, HuntDestById, servantName, out dest) && !string.IsNullOrEmpty(dest);
    }

    /// <summary>World-space (x, z) centre of the zone the servant is hunting in.</summary>
    internal static bool TryGetHuntPos(string servantName, out Vector2 worldXZ)
    {
        RefreshIfNeeded();
        worldXZ = default;
        if (string.IsNullOrEmpty(servantName))
            return false;
        return Lookup(HuntPosByName, HuntPosById, servantName, out worldXZ);
    }

    internal static bool TryGetInjuryRemaining(string servantName, out double seconds)
    {
        RefreshIfNeeded();
        if (string.IsNullOrEmpty(servantName))
        {
            seconds = 0;
            return false;
        }
        return Lookup(InjuryRemainingByName, InjuryRemainingById, servantName, out seconds);
    }

    internal static string FormatRemaining(double seconds)
    {
        if (seconds < 0)
            seconds = 0;
        var total = (int)Math.Floor(seconds);
        if (total >= 3600)
        {
            var h = total / 3600;
            var m = (total % 3600) / 60;
            return h + "h " + m + "m";
        }
        if (total >= 60)
            return (total / 60) + "m";
        return total + "s";
    }

    private static void RefreshNow()
    {
        HuntRemainingByName.Clear();
        HuntDestByName.Clear();
        HuntPosByName.Clear();
        InjuryRemainingByName.Clear();
        GearLevelByName.Clear();
        HuntRemainingById.Clear();
        HuntDestById.Clear();
        HuntPosById.Clear();
        InjuryRemainingById.Clear();
        GearLevelById.Clear();

        if (!TryGetClientWorld(out var world))
            return;

        var em = world.EntityManager;
        var nowTicks = DateTime.UtcNow.Ticks;

        NativeArray<Entity> missionOwners = default;
        NativeArray<Entity> coffins = default;
        try
        {
            var missionQuery = em.CreateEntityQuery(ComponentType.ReadOnly<ActiveServantMission>());
            missionOwners = missionQuery.ToEntityArray(Allocator.Temp);
            var missions = new List<ActiveServantMission>(8);
            for (var i = 0; i < missionOwners.Length; i++)
            {
                if (!em.HasBuffer<ActiveServantMission>(missionOwners[i]))
                    continue;
                var buf = em.GetBuffer<ActiveServantMission>(missionOwners[i]);
                for (var j = 0; j < buf.Length; j++)
                    missions.Add(buf[j]);
            }

            var coffinQuery = em.CreateEntityQuery(ComponentType.ReadOnly<ServantCoffinstation>());
            coffins = coffinQuery.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < coffins.Length; i++)
            {
                var station = em.GetComponentData<ServantCoffinstation>(coffins[i]);
                var name = station.ServantName.ToString();
                if (string.IsNullOrEmpty(name))
                    continue;

                var idKey = ServantNetworkKey(em, station.ConnectedServant._Entity);

                if (station.ServantGearLevel > 0f && !float.IsNaN(station.ServantGearLevel))
                {
                    GearLevelByName[name] = station.ServantGearLevel;
                    if (idKey != null) GearLevelById[idKey] = station.ServantGearLevel;
                }

                if (station.InjuryEndTimeTicks > nowTicks)
                {
                    var injurySec = (station.InjuryEndTimeTicks - nowTicks) / (double)TimeSpan.TicksPerSecond;
                    if (injurySec > 0 && injurySec < 48 * 3600)
                    {
                        InjuryRemainingByName[name] = injurySec;
                        if (idKey != null) InjuryRemainingById[idKey] = injurySec;
                    }
                }

                for (var m = 0; m < missions.Count; m++)
                {
                    var mission = missions[m];
                    if (!ServantOnMission(ref mission, station))
                        continue;
                    var remaining = ServantHelper.ActiveMissionExtensions.GetSecondsUntilCompletion(ref mission, nowTicks);
                    if (!IsSaneRemaining(remaining, mission.MissionLengthSeconds))
                    {
                        remaining = (mission.MissionStartTimeTicks
                                     + (long)(mission.MissionLengthSeconds * TimeSpan.TicksPerSecond)
                                     - nowTicks) / (double)TimeSpan.TicksPerSecond;
                    }
                    if (!IsSaneRemaining(remaining, mission.MissionLengthSeconds))
                    {
                        LogClockOnce(remaining, mission.MissionStartTimeTicks, mission.MissionLengthSeconds);
                        continue;
                    }
                    if (remaining < 0)
                        remaining = 0;
                    HuntRemainingByName[name] = remaining;
                    if (idKey != null) HuntRemainingById[idKey] = remaining;
                    var dest = DestForMission(em, world, mission.MissionID);
                    if (!string.IsNullOrEmpty(dest))
                    {
                        HuntDestByName[name] = dest;
                        if (idKey != null) HuntDestById[idKey] = dest;
                        if (PosByMission.TryGetValue(mission.MissionID.GuidHash, out var pos))
                        {
                            HuntPosByName[name] = pos;
                            if (idKey != null) HuntPosById[idKey] = pos;
                        }
                        if (!_loggedDest)
                        {
                            _loggedDest = true;
                            Plugin.Logger?.LogInfo("HuntsAndRifts hunt dest: " + name + " -> " + dest
                                + (HuntPosByName.ContainsKey(name) ? " @ " + HuntPosByName[name] : " (no zone position)"));
                        }
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts refresh skipped: " + ex.Message);
        }
        finally
        {
            if (missionOwners.IsCreated)
                missionOwners.Dispose();
            if (coffins.IsCreated)
                coffins.Dispose();
        }
    }

    private static bool ServantOnMission(ref ActiveServantMission mission, ServantCoffinstation station)
    {
        var servant = station.ConnectedServant.GetSyncedEntityOrNull();
        if (servant != Entity.Null && ServantHelper.ActiveMissionExtensions.ContainsServant(ref mission, servant))
            return true;

        var connected = station.ConnectedServant._Entity;
        if (connected == Entity.Null)
            return false;
        return mission.Servant1._Entity == connected
               || mission.Servant2._Entity == connected
               || mission.Servant3._Entity == connected;
    }

    private static bool IsSaneRemaining(double remaining, float missionLengthSeconds)
    {
        if (double.IsNaN(remaining) || double.IsInfinity(remaining))
            return false;
        var cap = Math.Max(missionLengthSeconds, 0) + 120;
        if (cap < 120)
            cap = 48 * 3600;
        return remaining > -5 && remaining <= cap + 1;
    }

    private static void LogClockOnce(double remaining, long startTicks, float lengthSeconds)
    {
        if (_loggedClockSanity)
            return;
        _loggedClockSanity = true;
        Plugin.Logger?.LogWarning(
            $"HuntsAndRifts could not map remaining time (got {remaining:0}s, startTicks={startTicks}, length={lengthSeconds}s). Leaving vanilla status text.");
    }

    private static readonly Dictionary<string, PrefabGUID> MissionByZoneName = new(StringComparer.Ordinal);
    private static bool _zoneNamesBuilt;

    /// <summary>Servant mission prefab for a zone by its localized name (map tooltip title).</summary>
    internal static bool TryGetMissionForZoneName(string zoneName, out PrefabGUID mission)
    {
        mission = default;
        if (string.IsNullOrEmpty(zoneName))
            return false;
        if (!_zoneNamesBuilt)
            BuildZoneNames();
        if (MissionByZoneName.TryGetValue(zoneName.Trim(), out mission))
            return true;
        // Tooltip titles can differ slightly from the hunt zone's name ("Hallowed Mountain" vs
        // "Hallowed Mountains"): compare normalised (letters only, lower case, trailing s dropped).
        var want = NormalizeZone(zoneName);
        if (want.Length == 0)
            return false;
        foreach (var kv in MissionByZoneName)
        {
            if (NormalizeZone(kv.Key) == want)
            {
                mission = kv.Value;
                return true;
            }
        }
        return false;
    }

    private static string NormalizeZone(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        var n = sb.ToString();
        if (n.EndsWith("s") && n.Length > 3)
            n = n.Substring(0, n.Length - 1);
        return n;
    }

    private static void BuildZoneNames()
    {
        if (!TryGetClientWorld(out var world))
            return;
        var em = world.EntityManager;
        NativeArray<MapZoneData> zones = default;
        try
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<MapZoneData>());
            if (q.IsEmptyIgnoreFilter)
                return;
            zones = q.ToComponentDataArray<MapZoneData>(Allocator.Temp);
            for (var i = 0; i < zones.Length; i++)
            {
                var data = zones[i];
                if (data.ServantMissionAsset.GuidHash == 0)
                    continue;
                string loc = null;
                try { loc = Localization.Get(data.Name, false); } catch { }
                if (string.IsNullOrWhiteSpace(loc) || loc.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                MissionByZoneName[loc.Trim()] = data.ServantMissionAsset;
            }
            _zoneNamesBuilt = MissionByZoneName.Count > 0;
            Plugin.Logger?.LogInfo("HuntsAndRifts zone names: " + MissionByZoneName.Count + " hunt zones indexed: "
                + string.Join(", ", MissionByZoneName.Keys));
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts zone names: " + ex.Message);
        }
        finally
        {
            if (zones.IsCreated)
                zones.Dispose();
        }
    }

    private static string DestForMission(EntityManager em, World world, PrefabGUID mission)
    {
        if (mission.GuidHash == 0)
            return "";
        if (DestByMission.TryGetValue(mission.GuidHash, out var cached))
            return cached;
        var dest = LookupZoneName(em, mission);
        if (string.IsNullOrEmpty(dest))
            dest = PrettyMission(world, mission);
        DestByMission[mission.GuidHash] = dest ?? "";
        return dest ?? "";
    }

    private static string LookupZoneName(EntityManager em, PrefabGUID mission)
    {
        NativeArray<MapZoneData> zones = default;
        NativeArray<Entity> entities = default;
        try
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<MapZoneData>());
            if (q.IsEmptyIgnoreFilter)
                return "";
            zones = q.ToComponentDataArray<MapZoneData>(Allocator.Temp);
            entities = q.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < zones.Length; i++)
            {
                var data = zones[i];
                if (data.ServantMissionAsset.GuidHash != mission.GuidHash)
                    continue;
                string name = null;
                try
                {
                    var loc = Localization.Get(data.Name, false);
                    if (!string.IsNullOrWhiteSpace(loc) && loc.IndexOf("not found", StringComparison.OrdinalIgnoreCase) < 0)
                        name = loc;
                }
                catch
                {
                    try
                    {
                        var loc = Localization.Get(data.Name);
                        if (!string.IsNullOrWhiteSpace(loc) && loc.IndexOf("not found", StringComparison.OrdinalIgnoreCase) < 0)
                            name = loc;
                    }
                    catch
                    {
                    }
                }
                if (name == null)
                    name = PrettyMissionName(data.ServantMissionAsset, null);
                var zoneEntity = i < entities.Length ? entities[i] : Entity.Null;
                if (ZoneLocator.TryGetCenter(em, zoneEntity, ref data, name, out var center))
                    PosByMission[mission.GuidHash] = center;
                return name;
            }
        }
        catch
        {
        }
        finally
        {
            if (zones.IsCreated)
                zones.Dispose();
            if (entities.IsCreated)
                entities.Dispose();
        }
        return "";
    }

    private static string PrettyMission(World world, PrefabGUID guid)
    {
        string raw = null;
        try
        {
            var data = world.GetExistingSystemManaged<GameDataSystem>();
            if (data != null && data.ManagedDataRegistry._PrefabLookupMap.TryGetFixedName(guid, out var fixedName))
                raw = fixedName.ToString();
        }
        catch
        {
        }
        return PrettyMissionName(guid, raw);
    }

    private static string PrettyMissionName(PrefabGUID guid, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var cut = raw.IndexOf(' ');
        if (cut > 0)
            raw = raw.Substring(0, cut);
        const string prefix = "ServantMission_";
        if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            raw = raw.Substring(prefix.Length);
        return raw.Replace('_', ' ');
    }

    internal static bool TryGetClientWorld(out World world)
    {
        world = null;
        var worlds = World.s_AllWorlds;
        if (worlds == null)
            return false;
        for (var i = 0; i < worlds.Count; i++)
        {
            var w = worlds[i];
            if (w == null || !w.IsCreated)
                continue;
            if (w.Name == "Client_0")
            {
                world = w;
                return true;
            }
        }
        if (!_loggedMissingWorld)
        {
            _loggedMissingWorld = true;
            Plugin.Logger?.LogDebug("HuntsAndRifts: Client_0 world not ready yet.");
        }
        return false;
    }
}

