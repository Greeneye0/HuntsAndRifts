using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using ProjectM.Terrain;
using ProjectM.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace HuntsAndRifts;

/// <summary>
/// World-space (x, z) centre for a map zone. MapZoneData.CenterPosWS is zero on the
/// client for hunt zones (1.2.13 log), so this tries, in order:
///   1. MapZoneData.CenterPosWS when non-zero.
///   2. UiPolygonMesh.Aabb on the zone entity (min/max float3, world space).
///   3. A MapRegionNameComponent entity whose NameKey equals the zone Name (its Translation
///      is where vanilla draws the region label on the map).
///   4. Mean of the zone MapZonePolygonVertexElement buffer.
/// Component reads go through the non-generic raw accessors
/// (GetComponentDataRawRO / GetBufferRawRO with a TypeIndex), so no generic
/// instantiation that the client build might lack is ever requested.
/// </summary>
internal static unsafe class ZoneLocator
{
    private static bool _typesReady;
    private static TypeIndex _uiPolygonMeshIndex;
    private static TypeIndex _regionNameIndex;
    private static TypeIndex _translationIndex;
    private static TypeIndex _vertexIndex;
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);

    private static void EnsureTypes()
    {
        if (_typesReady)
            return;
        _typesReady = true;
        try { _uiPolygonMeshIndex = TypeManager.GetTypeIndex(Il2CppType.Of<UiPolygonMesh>()); } catch { }
        try { _regionNameIndex = TypeManager.GetTypeIndex(Il2CppType.Of<MapRegionNameComponent>()); } catch { }
        try { _translationIndex = TypeManager.GetTypeIndex(Il2CppType.Of<Translation>()); } catch { }
        try { _vertexIndex = TypeManager.GetTypeIndex(Il2CppType.Of<MapZonePolygonVertexElement>()); } catch { }
    }

    internal static bool TryGetCenter(EntityManager em, Entity zoneEntity, ref MapZoneData zone, string zoneLabel, out Vector2 worldXZ)
    {
        worldXZ = default;
        EnsureTypes();
        string source = null;

        if (zone.CenterPosWS.x != 0f || zone.CenterPosWS.y != 0f)
        {
            worldXZ = new Vector2(zone.CenterPosWS.x, zone.CenterPosWS.y);
            source = "CenterPosWS";
        }
        else if (zoneEntity != Entity.Null && TryAabbCenter(em, zoneEntity, out worldXZ))
            source = "UiPolygonMesh.Aabb";
        else if (TryRegionName(em, ref zone, out worldXZ))
            source = "MapRegionName Translation";
        else if (zoneEntity != Entity.Null && TryVertexMean(em, zoneEntity, out worldXZ))
            source = "polygon vertex mean";

        if (source == null)
        {
            LogOnce(zoneLabel, "HuntsAndRifts zone '" + zoneLabel + "': no position source (CenterPosWS zero, no Aabb, no region label, no vertices).");
            return false;
        }
        LogOnce(zoneLabel, "HuntsAndRifts zone '" + zoneLabel + "' centre " + worldXZ + " from " + source + ".");
        return true;
    }

    private static bool TryAabbCenter(EntityManager em, Entity entity, out Vector2 xz)
    {
        xz = default;
        if (_uiPolygonMeshIndex.Value == 0)
            return false;
        try
        {
            if (!em.HasComponent(entity, ComponentType.ReadOnly(_uiPolygonMeshIndex)))
                return false;
            var raw = (float*)em.GetComponentDataRawRO(entity, _uiPolygonMeshIndex);
            if (raw == null)
                return false;
            // Aabb { float3 Min; float3 Max; }
            var minX = raw[0];
            var minZ = raw[2];
            var maxX = raw[3];
            var maxZ = raw[5];
            if (!Finite(minX) || !Finite(minZ) || !Finite(maxX) || !Finite(maxZ))
                return false;
            if (maxX - minX <= 0f && maxZ - minZ <= 0f)
                return false;
            xz = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
            return xz.x != 0f || xz.y != 0f;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryRegionName(EntityManager em, ref MapZoneData zone, out Vector2 xz)
    {
        xz = default;
        if (_regionNameIndex.Value == 0 || _translationIndex.Value == 0)
            return false;
        NativeArray<Entity> entities = default;
        try
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly(_regionNameIndex), ComponentType.ReadOnly(_translationIndex));
            if (q.IsEmptyIgnoreFilter)
                return false;
            entities = q.ToEntityArray(Allocator.Temp);
            fixed (MapZoneData* zp = &zone)
            {
                var want = (long*)&zp->Name; // LocalizationKey = AssetGuid = 16 bytes
                for (var i = 0; i < entities.Length; i++)
                {
                    var key = (long*)em.GetComponentDataRawRO(entities[i], _regionNameIndex);
                    if (key == null || key[0] != want[0] || key[1] != want[1])
                        continue;
                    var t = (float*)em.GetComponentDataRawRO(entities[i], _translationIndex);
                    if (t == null)
                        continue;
                    xz = new Vector2(t[0], t[2]);
                    return Finite(xz.x) && Finite(xz.y);
                }
            }
        }
        catch
        {
        }
        finally
        {
            if (entities.IsCreated)
                entities.Dispose();
        }
        return false;
    }

    private static bool TryVertexMean(EntityManager em, Entity entity, out Vector2 xz)
    {
        xz = default;
        if (_vertexIndex.Value == 0)
            return false;
        try
        {
            if (!em.HasComponent(entity, ComponentType.ReadOnly(_vertexIndex)))
                return false;
            var len = em.GetBufferLength(entity, _vertexIndex);
            if (len <= 0 || len > 4096)
                return false;
            var raw = (float*)em.GetBufferRawRO(entity, _vertexIndex);
            if (raw == null)
                return false;
            double sx = 0, sy = 0;
            for (var i = 0; i < len; i++)
            {
                sx += raw[i * 2];
                sy += raw[i * 2 + 1];
            }
            xz = new Vector2((float)(sx / len), (float)(sy / len));
            return Finite(xz.x) && Finite(xz.y) && (xz.x != 0f || xz.y != 0f);
        }
        catch
        {
            return false;
        }
    }

    private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

    private static void LogOnce(string key, string msg)
    {
        if (!Logged.Add(key ?? ""))
            return;
        Plugin.Logger?.LogInfo(msg);
    }
}
