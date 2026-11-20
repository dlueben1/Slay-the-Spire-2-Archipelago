using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using StS2AP.Models;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Gives the Intent Sight bonus's Forewarned power at the start of the player's turn whenever the
/// player lacks it: on the first turn of each combat, or on the next turn after the second Intent
/// Sight arrives mid-combat.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeSideTurnStart))]
internal static class Patches_IntentSightBonus
{
    [HarmonyPostfix]
    private static void Postfix(ref Task __result, CombatSide side, IReadOnlyList<Creature> participants)
    {
        if (side == CombatSide.Player && IntentSight.HasBonus)
            __result = GrantForewarned(__result, participants);
    }

    private static async Task GrantForewarned(Task nativeHook, IReadOnlyList<Creature> participants)
    {
        await nativeHook;
        // A lifecycle event cannot await power commands, so this joins the native task, as
        // Patches_CombatEffectDelivery does.
        if (GameUtility.CurrentPlayer?.Creature is not { } player
            || !participants.Contains(player)
            || player.HasPower<ForewarnedPower>())
            return;
        await PowerCmd.Apply<ForewarnedPower>(new ThrowingPlayerChoiceContext(), player, 1, null, null);
    }
}
