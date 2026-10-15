using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using StS2AP.Utils;

namespace StS2AP.Patches;

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeSideTurnStart))]
internal static class Patches_CombatEffectDelivery
{
    [HarmonyPostfix]
    private static void Postfix(ref Task __result, CombatSide side, IReadOnlyList<Creature> participants)
    {
        if (!MultiplayerSupport.IsMultiplayerScope && side == CombatSide.Player)
            __result = ApplyBeforeOpeningHand(__result, participants);
    }

    private static async Task ApplyBeforeOpeningHand(Task nativeHook, IReadOnlyList<Creature> participants)
    {
        await nativeHook;
        var player = GameUtility.CurrentPlayer;
        if (player != null && participants.Contains(player.Creature))
            // A lifecycle event cannot await power commands. Joining the native task keeps
            // Confused/No Draw and other effects ahead of the opening draw and autoplay.
            await BuffUtility.ProcessQueuedBuffsAsync(player);
    }
}
