using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Observes the exact point at which death prevention has finished and a player is truly dead.
/// Every replica sees this callback; DeathLinkMultiplayer permits only the native host to
/// authorize an AP-side send.
/// </summary>
[HarmonyPatch(typeof(Creature), nameof(Creature.InvokeDiedEvent))]
public static class Patches_DeathLinkPlayerDeath
{
    [HarmonyPostfix]
    private static void Postfix(Creature __instance)
    {
        try
        {
            if (__instance.Player is { } player)
                DeathLinkMultiplayer.PlayerDied(player);
        }
        catch (Exception ex)
        {
            // AP_MP: AP transport failure must never interrupt the base game's death lifecycle.
            LogUtility.Error($"Could not process multiplayer DeathLink death: {ex}");
        }
    }
}
