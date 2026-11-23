using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Relics;
using StS2AP.Utils.Combat;

namespace StS2AP.Patches;

/// <summary>
/// The Hidden Relics challenge's counters. Relics are unknown (see
/// <see cref="Patches_UnknownRelics"/>) and the player works out what they are by watching them:
/// flashes, sounds, the ready glow and the used grey all stay, but a counter shows "?". Telling
/// the count would give most counter relics away, since few share a threshold.
/// </summary>
public static class Patches_HiddenRelics
{
    private const string HiddenText = "?";

    /// <summary>
    /// The holder rewrites its counter when the relic changes it and when the holder gets a new
    /// relic. Counter relics show "?" instead, the way Hidden Piles shows its pile counts, and
    /// relics without a counter stay blank.
    /// </summary>
    [HarmonyPatch(typeof(NRelicInventoryHolder), nameof(NRelicInventoryHolder.RefreshAmount))]
    public static class HideCounter
    {
        [HarmonyPostfix]
        public static void Postfix(NRelicInventoryHolder __instance)
        {
            RelicModel relic = __instance._relic.Model;
            if (__instance._amountLabel.Visible && RelicSight.Hides(relic) && !Patches_UnknownRelics.ShowsApState(relic))
                __instance._amountLabel.SetTextAutoSize(HiddenText);
        }
    }
}
