using System;
using HarmonyLib;
using ProjectM;
using ProjectM.UI;
using Stunlock.Core;
using Unity.Collections;

namespace HuntsAndRifts;

[HarmonyPatch]
internal static class LootPopupPatches
{
    [HarmonyPatch(typeof(GameDataSystem), nameof(GameDataSystem.RegisterItems))]
    [HarmonyPostfix]
    private static void RegisterItemsPostfix(GameDataSystem __instance)
    {
        try
        {
            LootReader.RebuildLut();
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts RegisterItems postfix: " + ex.Message);
        }
    }

    [HarmonyPatch(typeof(ScrollingCombatTextParentMapper), "_InitializeUI_b__5_1")]
    [HarmonyPostfix]
    private static void SctEntryPostfix(SCTText element, ScrollingCombatTextParentMapper.EntryData data)
    {
        try
        {
            AppendBagTotal(element, data);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug("HuntsAndRifts SCT postfix: " + ex.Message);
        }
    }

    private static void AppendBagTotal(SCTText text, ScrollingCombatTextParentMapper.EntryData entry)
    {
        if (text == null)
            return;
        if (!LootReader.IsResourceGain(entry.Type))
            return;

        var name = entry.SourceTypeText.ToString();
        if (string.IsNullOrEmpty(name))
            return;
        if (name.IndexOf('(') >= 0 && name.IndexOf(')') > name.IndexOf('('))
            return;

        if (!LootReader.TryResolveItem(name, out var prefab))
        {
            LootReader.NoteMissing(name);
            return;
        }
        if (!LootReader.TryGetBagTotal(prefab, out var total))
            return;

        var withTotal = name + " (" + total + ")";
        try
        {
            entry.SourceTypeText = new FixedString128Bytes(withTotal);
        }
        catch
        {
            // EntryData is a struct copy; UI text still gets the replace below.
        }

        var tmp = text.Text;
        if (tmp == null)
            return;
        var current = tmp.text;
        if (string.IsNullOrEmpty(current))
            current = tmp.m_text;
        if (string.IsNullOrEmpty(current) || current.IndexOf(withTotal, StringComparison.Ordinal) >= 0)
            return;
        var replaced = current.Replace(name, withTotal);
        tmp.text = replaced;
        tmp.m_text = replaced;
    }
}
