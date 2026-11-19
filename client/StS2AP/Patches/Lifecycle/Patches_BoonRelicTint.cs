using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using StS2AP.Models;

namespace StS2AP.Patches;

/// <summary>
/// Tints the icons of AP relics that borrow a base-game relic's art, such as the Wayfinder and
/// Second Wind Boons, so they do not look identical to Winged Boots or Lizard Tail. Every place
/// that draws a relic icon (the relic bar, the inspect view, reward rows) calls UpdateTexture,
/// which is also where the game tints a melted relic dark red. The tint uses the same
/// SelfModulate, so the shader's used-up and pulse effects still apply on top of it.
/// </summary>
[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.UpdateTexture))]
internal static class Patches_BoonRelicTint
{
    [HarmonyPostfix]
    public static void TintIcon(RelicModel __instance, TextureRect texture)
    {
        if (__instance is not IApTintedRelic tinted || __instance.IsMelted)
            return;
        // Leave any other tint alone, such as the relic collection's locked silhouette.
        if (texture.SelfModulate != Colors.White && texture.SelfModulate != tinted.IconTint)
            return;
        texture.SelfModulate = tinted.IconTint;
    }
}
