using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace StS2AP.Patches;

/// <summary>
/// Wing Charm advances the host-authoritative shared Niche RNG while modifying card rewards.
/// AP card assignments are materialized only by their owner, so the relic is not safe in an AP
/// multiplayer run when that owner is a guest.
/// </summary>
[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.IsAllowed))]
public static class Patches_WingCharmMultiplayer
{
    [HarmonyPostfix]
    private static void ExcludeFromMultiplayer(RelicModel __instance, ref bool __result)
    {
        if (__instance is WingCharm && MultiplayerSupport.IsMultiplayerScope)
            __result = false;
    }
}
