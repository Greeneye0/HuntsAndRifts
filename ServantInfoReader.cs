using System;
using System.Collections.Generic;
using System.Text;
using ProjectM;
using ProjectM.UI;
using Stunlock.Core;
using Stunlock.Localization;
using UnityEngine;

namespace HuntsAndRifts;

/// <summary>
/// Per-servant perks (the two specialisation icons, e.g. Tracker / Dunley Farmlands Hunter)
/// from the client's last ServantInfoEvent response, plus perk icon/name lookups through
/// ManagedDataRegistry. The response FixedList is parsed from raw bytes so no generic
/// FixedList instantiation is requested through interop.
/// </summary>
internal static unsafe class ServantInfoReader
{
    internal struct Info
    {
        public PrefabGUID Perk0;
        public PrefabGUID Perk1;
        public float HuntProficiency;
        public float LootFactor;
        /// <summary>Raw 16-byte NetworkId of the servant entity, as a key ("a:b"); null if empty.</summary>
        public string NetworkKey;
    }

    /// <summary>Network identity key for a servant name from the current throne's response.</summary>
    internal static bool TryGetNetworkKey(string servantName, out string key)
    {
        key = null;
        if (!TryGetInfo(servantName, out var info) || string.IsNullOrEmpty(info.NetworkKey))
            return false;
        key = info.NetworkKey;
        return true;
    }

    private static readonly ServantIdentityIndex<Info> Records = new();
    private static long _worldSequence = -1;
    private static Unity.Entities.Entity _throne;
    private static bool _awaitingResponse;
    private static ulong _lastResponseHash;

    internal static void BeginThrone(Unity.Entities.Entity throne)
    {
        RefreshNow(); // Capture the previous response before the game requests another castle.
        if (_throne == throne) return;
        _throne = throne;
        Records.Clear();
        _awaitingResponse = true;
        _nextRefresh = 0;
    }

    internal static void Clear()
    {
        Records.Clear();
        PerkCache.Clear();
        PerkRetryAt.Clear();
        _nextRefresh = 0;
        _worldSequence = -1;
        _throne = default;
        _awaitingResponse = false;
        _lastResponseHash = 0;
    }
    private static readonly Dictionary<int, (Sprite icon, string name)> PerkCache = new();
    private static readonly Dictionary<int, float> PerkRetryAt = new();
    private static float _nextRefresh;
    private static bool _loggedOnce;
    private static bool _loggedPerk;

    // ServantInfoEvent.Response.Entry layout (client dump.cs): Name FixedString64Bytes @0,
    // NetworkId @0x40, InjuryEndTimeTicks @0x50, HuntProficiency @0x58, LootFactor @0x5C,
    // Perk0 @0x60, Perk1 @0x64, Injury @0x68, State @0x6C; size 0x70.
    private const int EntrySize = 0x70;
    // FixedList4096Bytes<T>: ushort Length @0, then padding so elements are aligned.
    // Unity's PaddingBytes<T>() for a 112-byte element is 6, so data starts at 8.
    private const int DataOffset = 8;

    internal static bool TryGetInfo(string servantName, out Info info)
    {
        RefreshIfNeeded();
        info = default;
        if (string.IsNullOrEmpty(servantName))
            return false;
        return Records.TryGet(servantName, out info);
    }

    internal static bool TryGetPerk(PrefabGUID perk, out Sprite icon, out string name)
    {
        RefreshIfNeeded();
        icon = null;
        name = "";
        if (perk.GuidHash == 0)
            return false;
        if (PerkCache.TryGetValue(perk.GuidHash, out var cached))
        {
            if (cached.icon != null)
            {
                icon = cached.icon;
                name = cached.name;
                return true;
            }
            PerkCache.Remove(perk.GuidHash);
        }
        if (PerkRetryAt.TryGetValue(perk.GuidHash, out var retryAt) && Time.unscaledTime < retryAt)
            return false;
        PerkRetryAt[perk.GuidHash] = Time.unscaledTime + 0.5f;
        try
        {
            if (!HuntReader.TryGetClientWorld(out var world))
                return false;
            var gds = world.GetExistingSystemManaged<GameDataSystem>();
            if (gds == null)
                return false;
            var registry = gds.ManagedDataRegistry;
            if (registry.TryGetWithoutLogging<ManagedPerkData>(perk, out var data) && data != null)
            {
                icon = data.Icon;
                try { name = Localization.Get(data.Name, false); } catch { name = ""; }
                if (string.IsNullOrWhiteSpace(name) || name.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                    name = "";
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts perk lookup: " + ex.Message);
        }
        if (icon != null)
        {
            PerkCache[perk.GuidHash] = (icon, name);
            PerkRetryAt.Remove(perk.GuidHash);
        }
        if (!_loggedPerk && icon != null)
        {
            _loggedPerk = true;
            Plugin.Logger?.LogInfo("HuntsAndRifts perk icon: " + perk.GuidHash + " -> '" + name + "' (" + icon.name + ")");
        }
        return icon != null;
    }

    private static void RefreshIfNeeded()
    {
        var now = Time.unscaledTime;
        if (now < _nextRefresh)
            return;
        _nextRefresh = now + 0.25f;
        try
        {
            RefreshNow();
        }
        catch (Exception ex)
        {
            Records.Clear();
            Plugin.Logger?.LogDebug("HuntsAndRifts servant info: " + ex.Message);
        }
    }

    private static void RefreshNow()
    {
        Records.Clear();
        if (!HuntReader.TryGetClientWorld(out var world))
        {
            PerkCache.Clear();
            PerkRetryAt.Clear();
            _worldSequence = -1;
            return;
        }
        if (_worldSequence != (long)world.SequenceNumber)
        {
            PerkCache.Clear();
            PerkRetryAt.Clear();
            _worldSequence = (long)world.SequenceNumber;
            _throne = default;
            _lastResponseHash = 0;
            _awaitingResponse = false;
        }
        var sys = world.GetExistingSystemManaged<ServantInfoEventSystem_Client>();
        if (sys == null)
            return;
        var response = sys.LastResponse;
        var result = response.Result;
        var basePtr = (byte*)&result;
        var length = *(ushort*)basePtr;
        if (length == 0 || length > 4094 / EntrySize)
            return;
        ulong hash = 14695981039346656037UL;
        for (var b = 0; b < DataOffset + length * EntrySize; b++)
            hash = unchecked((hash ^ basePtr[b]) * 1099511628211UL);
        if (_awaitingResponse && hash == _lastResponseHash) return;
        _awaitingResponse = false;
        _lastResponseHash = hash;
        var nameBuf = new byte[62];
        for (var i = 0; i < length; i++)
        {
            var e = basePtr + DataOffset + i * EntrySize;
            var nameLen = *(ushort*)e;
            if (nameLen == 0 || nameLen > 62)
                continue;
            for (var b = 0; b < nameLen; b++)
                nameBuf[b] = e[2 + b];
            var name = Encoding.UTF8.GetString(nameBuf, 0, nameLen);
            if (string.IsNullOrEmpty(name))
                continue;
            var idA = *(long*)(e + 0x40);
            // NetworkId is 12 bytes (index, generation, type); mask the 4 padding bytes.
            var idB = *(uint*)(e + 0x48);
            var key = (idA != 0 || idB != 0) ? idA + ":" + idB : null;
            Records.Add(name, key, new Info
            {
                HuntProficiency = *(float*)(e + 0x58),
                LootFactor = *(float*)(e + 0x5C),
                Perk0 = new PrefabGUID(*(int*)(e + 0x60)),
                Perk1 = new PrefabGUID(*(int*)(e + 0x64)),
                NetworkKey = key
            });
        }
        if (!_loggedOnce)
        {
            _loggedOnce = true;
            Plugin.Logger?.LogInfo("HuntsAndRifts servant info: " + length + " records indexed by network ID.");
        }
    }
}
