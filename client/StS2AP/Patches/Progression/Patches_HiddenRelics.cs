using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils.Combat;

namespace StS2AP.Patches;

/// <summary>
/// The Hidden Relics challenge. Every relic draws its feedback through the same few places, so
/// no relic needs its own patch: the top bar holder (counter, icon flash, grey when used), the
/// relic's texture (pulse when ready) and the player (activation sound and the flash on the
/// creature). Known leaks left on purpose: the effects themselves, relic text that names a live
/// number, the flash when a relic is picked up, and wax relics melting.
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
            if (__instance._amountLabel.Visible && RelicSight.Hides(__instance._relic.Model))
                __instance._amountLabel.SetTextAutoSize(HiddenText);
        }
    }

    /// <summary>
    /// A used relic turns grey here. Its texture's pulse and used looks are reset by
    /// <see cref="HideStatusLook"/>, which this method calls first.
    /// </summary>
    [HarmonyPatch(typeof(NRelicInventoryHolder), nameof(NRelicInventoryHolder.RefreshStatus))]
    public static class HideUsedGrey
    {
        [HarmonyPostfix]
        public static void Postfix(NRelicInventoryHolder __instance)
        {
            if (RunManager.Instance.IsInProgress && RelicSight.Hides(__instance._relic.Model))
                __instance._relic.Icon.Modulate = Colors.White;
        }
    }

    /// <summary>
    /// The relic shader pulses while a relic is ready and darkens it once used. The top bar holder
    /// and NRelic both set it through this method.
    /// </summary>
    [HarmonyPatch(typeof(RelicModel), nameof(RelicModel.UpdateTexture))]
    public static class HideStatusLook
    {
        [HarmonyPostfix]
        public static void Postfix(RelicModel __instance, TextureRect texture)
        {
            if (!RunManager.Instance.IsInProgress || !RelicSight.Hides(__instance))
                return;
            if (texture.Material is not ShaderMaterial shader)
                return;
            shader.SetShaderParameter(RelicModel._pulse, 0);
            shader.SetShaderParameter(RelicModel._isUsed, 0);
        }
    }

    /// <summary>
    /// The holder's handler for the relic's Flashed event. Its pickup animation calls DoFlash on
    /// its own, so relics still flash once when picked up.
    /// </summary>
    [HarmonyPatch(typeof(NRelicInventoryHolder), nameof(NRelicInventoryHolder.OnRelicFlashed))]
    public static class HideTopBarFlash
    {
        [HarmonyPrefix]
        // The handler's parameter is named "_", so it is taken by position.
        public static bool Prefix(RelicModel __0) => !RelicSight.Hides(__0);
    }

    /// <summary>The player's handler plays the relic's sound and flashes it over each target.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.OnRelicFlashed))]
    public static class HideCreatureFlash
    {
        [HarmonyPrefix]
        public static bool Prefix(RelicModel relic) => !RelicSight.Hides(relic);
    }
}
