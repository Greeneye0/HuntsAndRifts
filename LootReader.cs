using System;
using System.Collections.Generic;
using ProjectM;
using Stunlock.Core;
using Stunlock.Localization;
using Unity.Collections;
using Unity.Entities;

namespace HuntsAndRifts;

internal static class LootReader
{
    private static readonly Dictionary<string, PrefabGUID> NameToPrefab = new(StringComparer.Ordinal);
    private static readonly HashSet<string> MissingNames = new(StringComparer.Ordinal);
    private static PrefabGUID _resourceGainType;
    private static bool _resolvedResourceGain;
    private static bool _loggedLut;

    internal static bool IsResourceGain(PrefabGUID type)
    {
        EnsureResourceGainType();
        if (_resourceGainType.IsEmpty())
            return type.GuidHash == 1876501183;
        return type.GuidHash == _resourceGainType.GuidHash || type.GuidHash == 1876501183;
    }

    internal static bool TryGetBagTotal(PrefabGUID item, out int total)
    {
        total = 0;
        if (item.IsEmpty())
            return false;
        if (!HuntReader.TryGetClientWorld(out var world))
            return false;
        if (!ConsoleShared.TryGetLocalCharacterInCurrentWorld(out var character, world))
            return false;

        try
        {
            total = InventoryUtilities.GetItemAmountInInventories(world.EntityManager, character, item);
            return true;
        }
        catch
        {
            try
            {
                total = InventoryUtilities.GetItemAmount(world.EntityManager, character, item);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogDebug("HuntsAndRifts bag total skipped: " + ex.Message);
                return false;
            }
        }
    }

    internal static bool TryResolveItem(string localizedName, out PrefabGUID prefab)
    {
        prefab = default;
        if (string.IsNullOrEmpty(localizedName))
            return false;
        EnsureLut();
        if (NameToPrefab.TryGetValue(localizedName, out prefab) && !prefab.IsEmpty())
            return true;
        RebuildLut();
        return NameToPrefab.TryGetValue(localizedName, out prefab) && !prefab.IsEmpty();
    }

    internal static void RebuildLut()
    {
        NameToPrefab.Clear();
        if (!HuntReader.TryGetClientWorld(out var world))
            return;

        GameDataSystem data;
        try
        {
            data = world.GetExistingSystemManaged<GameDataSystem>();
        }
        catch
        {
            return;
        }
        if (data == null)
            return;

        NativeParallelHashMap<PrefabGUID, ItemData> map;
        try
        {
            map = data.ItemHashLookupMap;
        }
        catch
        {
            return;
        }

        NativeArray<PrefabGUID> keys = default;
        try
        {
            if (map.Count() == 0)
                return;
            keys = map.GetKeyArray(Allocator.Temp);
            var managed = data.ManagedDataRegistry;
            var prefabMap = managed._PrefabLookupMap;
            for (var i = 0; i < keys.Length; i++)
            {
                var guid = keys[i];
                ManagedItemData managedData = null;
                try
                {
                    managedData = managed.GetOrDefault<ManagedItemData>(guid);
                }
                catch
                {
                    continue;
                }
                if (managedData == null)
                    continue;

                string prefabName = null;
                try
                {
                    if (prefabMap.TryGetFixedName(guid, out var fixedName))
                        prefabName = fixedName.ToString();
                }
                catch
                {
                    // ignore
                }

                if (prefabName == "Item_Ingredient_Kit_Base" || prefabName == "Item_Ingredient_Gem_Base")
                    continue;

                string itemName = null;
                try
                {
                    itemName = Localization.Get(managedData.Name, false);
                }
                catch
                {
                    try
                    {
                        itemName = Localization.Get(managedData.Name);
                    }
                    catch
                    {
                        continue;
                    }
                }
                if (string.IsNullOrEmpty(itemName))
                    continue;
                NameToPrefab[itemName] = guid;
            }

            if (!_loggedLut)
            {
                _loggedLut = true;
                Plugin.Logger?.LogInfo("HuntsAndRifts loot LUT: " + NameToPrefab.Count + " item names.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts loot LUT rebuild: " + ex.Message);
        }
        finally
        {
            if (keys.IsCreated)
                keys.Dispose();
        }
    }

    internal static void NoteMissing(string name)
    {
        if (string.IsNullOrEmpty(name) || MissingNames.Contains(name))
            return;
        MissingNames.Add(name);
        Plugin.Logger?.LogDebug("HuntsAndRifts: no prefab for pickup name '" + name + "'.");
    }

    private static void EnsureLut()
    {
        if (NameToPrefab.Count == 0)
            RebuildLut();
    }

    private static void EnsureResourceGainType()
    {
        if (_resolvedResourceGain)
            return;
        _resolvedResourceGain = true;
        if (!HuntReader.TryGetClientWorld(out var world))
            return;
        try
        {
            var prefabs = world.GetExistingSystemManaged<PrefabCollectionSystem>();
            if (prefabs == null)
                return;
            var dict = prefabs.SpawnableNameToPrefabGuidDictionary;
            if (dict == null)
                return;
            foreach (var kv in dict)
            {
                var name = kv.Key;
                if (string.IsNullOrEmpty(name))
                    continue;
                if (name.IndexOf("ResourceGain", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("ResouceGain", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _resourceGainType = kv.Value;
                    Plugin.Logger?.LogInfo("HuntsAndRifts ResourceGain SCT prefab: " + name + " " + kv.Value);
                    if (name.IndexOf("SCT", StringComparison.OrdinalIgnoreCase) >= 0)
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts ResourceGain resolve: " + ex.Message);
        }
    }
}
