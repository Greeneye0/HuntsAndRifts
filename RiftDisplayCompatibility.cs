using ProjectM.UI;
using UnityEngine;

namespace HuntsAndRifts;

/// <summary>Hide Progenitor's sample rows while HuntsAndRifts supplies the map's rift card.</summary>
internal static class RiftDisplayCompatibility
{
    // Progenitor's documented attachment is a sibling of WarEventInfo. Match its owner,
    // not its displayed text, so other map widgets and rift tooltips are never affected.
    private const string SampleRows = "progenitor-ui-greeneye.progenitor.sample-MapWarEventInfo";
    private static CanvasGroup _mask;
    private static bool _logged;

    internal static void Apply(MapWarEventInfoEntry info, bool cardHasTimers)
    {
        var parent = info != null ? info.transform.parent : null;
        var sample = cardHasTimers && parent != null ? parent.Find(SampleRows) : null;
        if (_mask != null && (sample == null || _mask.transform.Pointer != sample.Pointer))
            Reset();
        if (sample == null || _mask != null)
            return;

        // Own a separate group rather than disabling the object: the attachment framework
        // controls its active state. Removing our group restores its original presentation.
        _mask = sample.gameObject.AddComponent<CanvasGroup>();
        _mask.alpha = 0f;
        _mask.interactable = false;
        _mask.blocksRaycasts = false;
        if (!_logged)
        {
            _logged = true;
            Plugin.Logger?.LogInfo("HuntsAndRifts: hid duplicate Progenitor sample rift rows beside the map card.");
        }
    }

    internal static void Reset()
    {
        if (_mask != null)
        {
            _mask.alpha = 1f;
            _mask.interactable = true;
            _mask.blocksRaycasts = true;
            Object.Destroy(_mask);
        }
        _mask = null;
    }
}
