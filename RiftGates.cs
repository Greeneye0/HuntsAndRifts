using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using ProjectM.Shared.WarEvents;
using Unity.Collections;
using Unity.Entities;

namespace HuntsAndRifts;

/// <summary>
/// Which rift tiers have an open gate right now, read from the client's networked gates.
/// The game networks only one WarEvent_NetworkedData (the "next/active" event), yet T1 and
/// T2 can run at the same time; the open gates are the only client-side evidence of the other
/// tier. Tier per gate: WarEvent_NetworkedGate.VariantIndex indexes the WarEvent_MapNode blob's
/// EventVariants (WarEvent_GateVariant.WarEventType @0x84) for the node with the same chunk.
/// All reads are raw (TypeIndex + GetComponentDataRawRO) with bounds checks; a bad pointer
/// aborts the scan instead of dereferencing.
/// </summary>
internal static unsafe class RiftGates
{
    private static bool _typesReady;
    private static TypeIndex _gateIndex;
    private static TypeIndex _nodeIndex;
    private static bool _loggedOnce;
    private static string _lastLog;

    // WarEvent_NetworkedGate: Progress @0, TotalProgress @4, int2 Coordinates @8, byte VariantIndex @0x10, bool IsOpen @0x11
    // WarEvent_MapNode: TerrainChunk ChunkCoordinate @0 (sbyte X, sbyte Y), BlobAssetReference NodeData @8 (byte* to root)
    // WarEvent_MapNodeBlob root: BlobArray<WarEvent_GateVariant> EventVariants @0 (int offset, int length)
    // WarEvent_GateVariant stride 0xB8; WarEventType @0x84
    private const int VariantStride = 0xB8;
    private const int VariantTypeOffset = 0x84;

    internal readonly struct OpenGate
    {
        public readonly WarEventType Type;
        public readonly float Progress;
        public readonly float TotalProgress;
        public OpenGate(WarEventType type, float progress, float total)
        {
            Type = type;
            Progress = progress;
            TotalProgress = total;
        }
    }

    internal static List<OpenGate> Scan(EntityManager em)
    {
        var result = new List<OpenGate>();
        try
        {
            if (!_typesReady)
            {
                _typesReady = true;
                try { _gateIndex = TypeManager.GetTypeIndex(Il2CppType.Of<WarEvent_NetworkedGate>()); } catch { }
                try { _nodeIndex = TypeManager.GetTypeIndex(Il2CppType.Of<WarEvent_MapNode>()); } catch { }
            }
            if (_gateIndex.Value == 0 || _nodeIndex.Value == 0)
                return result;

            // Map nodes: chunk -> (blob root pointer)
            var nodes = new Dictionary<int, IntPtr>();
            NativeArray<Entity> nodeEntities = default;
            NativeArray<Entity> gateEntities = default;
            try
            {
                var nq = em.CreateEntityQuery(ComponentType.ReadOnly(_nodeIndex));
                if (!nq.IsEmptyIgnoreFilter)
                {
                    nodeEntities = nq.ToEntityArray(Allocator.Temp);
                    for (var i = 0; i < nodeEntities.Length; i++)
                    {
                        var raw = (byte*)em.GetComponentDataRawRO(nodeEntities[i], _nodeIndex);
                        if (raw == null)
                            continue;
                        int cx = (sbyte)raw[0];
                        int cy = (sbyte)raw[1];
                        var blob = *(byte**)(raw + 8);
                        if (blob == null)
                            continue;
                        nodes[(cx << 16) ^ (cy & 0xFFFF)] = (IntPtr)blob;
                    }
                }

                var gq = em.CreateEntityQuery(ComponentType.ReadOnly(_gateIndex));
                if (gq.IsEmptyIgnoreFilter)
                    return result;
                gateEntities = gq.ToEntityArray(Allocator.Temp);
                var open = 0;
                for (var i = 0; i < gateEntities.Length; i++)
                {
                    var raw = (byte*)em.GetComponentDataRawRO(gateEntities[i], _gateIndex);
                    if (raw == null)
                        continue;
                    var isOpen = raw[0x11] != 0;
                    if (!isOpen)
                        continue;
                    open++;
                    var progress = *(float*)raw;
                    var total = *(float*)(raw + 4);
                    var cx = *(int*)(raw + 8);
                    var cy = *(int*)(raw + 12);
                    var variant = raw[0x10];
                    if (!nodes.TryGetValue((cx << 16) ^ (cy & 0xFFFF), out var blobPtr))
                        continue;
                    var root = (byte*)blobPtr;
                    // BlobArray { int m_OffsetPtr; int m_Length; } relative to its own address.
                    var offset = *(int*)root;
                    var length = *(int*)(root + 4);
                    if (length <= 0 || length > 16 || variant >= length)
                        continue;
                    if (offset <= 0 || offset > 1 << 20)
                        continue;
                    var variants = root + offset;
                    var type = (WarEventType)(*(int*)(variants + variant * VariantStride + VariantTypeOffset));
                    if (type != WarEventType.Minor && type != WarEventType.Major && type != WarEventType.Primal)
                        continue;
                    result.Add(new OpenGate(type, progress, total));
                }
                var summary = open + " open gates, " + result.Count + " resolved";
                foreach (var g in result)
                    summary += " " + g.Type;
                if (!_loggedOnce || summary != _lastLog)
                {
                    _loggedOnce = true;
                    _lastLog = summary;
                    Plugin.Logger?.LogInfo("HuntsAndRifts gates: " + summary + " (" + nodes.Count + " map nodes).");
                }
            }
            finally
            {
                if (nodeEntities.IsCreated) nodeEntities.Dispose();
                if (gateEntities.IsCreated) gateEntities.Dispose();
            }
        }
        catch (Exception ex)
        {
            if (!_loggedOnce)
            {
                _loggedOnce = true;
                Plugin.Logger?.LogInfo("HuntsAndRifts gates: scan failed: " + ex.Message);
            }
        }
        return result;
    }
}
