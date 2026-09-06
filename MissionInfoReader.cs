using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace HuntsAndRifts;

/// <summary>
/// Per-hunt (servant mission prefab) data: MissionData.MissionDifficulty (the hunt's power
/// level) and the PerksBuffer of perks the hunt favours. Read raw from the prefab entity
/// through non-generic accessors; cached per mission GUID.
/// </summary>
internal static unsafe class MissionInfoReader
{
    internal sealed class Info
    {
        public int Difficulty;
        public int ServantSlots;
        public readonly List<PrefabGUID> Perks = new();
    }

    private static readonly Dictionary<int, Info> Cache = new();
    private static bool _typesReady;
    private static TypeIndex _missionDataIndex;
    private static TypeIndex _perksIndex;
    private static bool _loggedOnce;

    private static void EnsureTypes()
    {
        if (_typesReady)
            return;
        _typesReady = true;
        try { _missionDataIndex = TypeManager.GetTypeIndex(Il2CppType.Of<MissionData>()); } catch { }
        try { _perksIndex = TypeManager.GetTypeIndex(Il2CppType.Of<PerksBuffer>()); } catch { }
    }

    internal static Info Get(PrefabGUID mission)
    {
        if (mission.GuidHash == 0)
            return null;
        if (Cache.TryGetValue(mission.GuidHash, out var cached))
            return cached;
        Info info = null;
        try
        {
            info = Read(mission);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts mission info: " + ex.Message);
        }
        Cache[mission.GuidHash] = info;
        return info;
    }

    private static Info Read(PrefabGUID mission)
    {
        EnsureTypes();
        if (!HuntReader.TryGetClientWorld(out var world))
            return null;
        var gds = world.GetExistingSystemManaged<GameDataSystem>();
        if (gds == null)
            return null;
        var lookup = gds.ManagedDataRegistry._PrefabLookupMap;
        if (!lookup.TryGetValueWithoutLogging(mission, out var entity) || entity == Entity.Null)
        {
            Plugin.Logger?.LogInfo("HuntsAndRifts mission info: prefab " + mission.GuidHash + " not in PrefabLookupMap.");
            return null;
        }
        var em = world.EntityManager;
        var info = new Info();
        var any = false;

        if (_missionDataIndex.Value != 0 && em.HasComponent(entity, ComponentType.ReadOnly(_missionDataIndex)))
        {
            var raw = (int*)em.GetComponentDataRawRO(entity, _missionDataIndex);
            if (raw != null)
            {
                // MissionData { PrefabGUID @0; int MissionDifficulty @4; int ServantSlots @8; bool AlwaysUnlocked @0xC }
                info.Difficulty = raw[1];
                info.ServantSlots = raw[2];
                any = true;
            }
        }

        if (_perksIndex.Value != 0 && em.HasComponent(entity, ComponentType.ReadOnly(_perksIndex)))
        {
            var len = em.GetBufferLength(entity, _perksIndex);
            if (len > 0 && len <= 16)
            {
                var raw = (int*)em.GetBufferRawRO(entity, _perksIndex);
                if (raw != null)
                {
                    for (var i = 0; i < len; i++)
                    {
                        if (raw[i] != 0)
                            info.Perks.Add(new PrefabGUID(raw[i]));
                    }
                    any = true;
                }
            }
        }

        if (!any)
        {
            Plugin.Logger?.LogInfo("HuntsAndRifts mission info: prefab " + mission.GuidHash + " has neither MissionData nor PerksBuffer (typeIndex "
                + _missionDataIndex.Value + "/" + _perksIndex.Value + ").");
            return null;
        }
        if (!_loggedOnce)
        {
            _loggedOnce = true;
            Plugin.Logger?.LogInfo("HuntsAndRifts mission info: " + mission.GuidHash + " difficulty " + info.Difficulty
                + " slots " + info.ServantSlots + " perks " + info.Perks.Count);
        }
        return info;
    }
}
