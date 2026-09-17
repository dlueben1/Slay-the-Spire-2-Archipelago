using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;

namespace StS2AP.Patches;

/// <summary>Removes epoch locks from the playable and colorless card pools.</summary>
[HarmonyPatch(typeof(CardPoolModel), nameof(CardPoolModel.GetUnlockedCards))]
public static class Patches_UnlockAllCards
{
    [HarmonyPostfix]
    private static void Postfix(
        CardPoolModel __instance,
        CardMultiplayerConstraint multiplayerConstraint,
        ref IEnumerable<CardModel> __result)
    {
        if (__instance is not (ColorlessCardPool
            or IroncladCardPool
            or SilentCardPool
            or DefectCardPool
            or NecrobinderCardPool
            or RegentCardPool))
            return;

        __result = __instance.AllCards.Where(card => multiplayerConstraint switch
        {
            CardMultiplayerConstraint.MultiplayerOnly =>
                card.MultiplayerConstraint != CardMultiplayerConstraint.SingleplayerOnly,
            CardMultiplayerConstraint.SingleplayerOnly =>
                card.MultiplayerConstraint != CardMultiplayerConstraint.MultiplayerOnly,
            _ => true,
        }).ToList();
    }
}
