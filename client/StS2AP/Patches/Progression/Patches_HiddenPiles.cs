using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using StS2AP.Utils.Combat;

namespace StS2AP.Patches;

/// <summary>
/// The Hidden Piles challenge. The draw, discard and exhaust pile buttons show "?" instead of
/// their card count and no longer open their screens. The Deck button still lists the whole deck.
/// Known leaks left on purpose: the exhaust button only appears once a card is exhausted, the
/// shuffle animation, cards that let you pick from a pile, and card text that counts a pile.
/// </summary>
public static class Patches_HiddenPiles
{
    private const string HiddenText = "?";

    /// <summary>
    /// The button writes its count when combat starts and each time a card lands in or leaves the
    /// pile. It keeps tracking the real count, so a later Pile Sight can show it straight away.
    /// AddCard is virtual, which the publicizer leaves protected, so it is looked up by name.
    /// </summary>
    [HarmonyPatch]
    public static class HideCount
    {
        [HarmonyTargetMethods]
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
        [
            AccessTools.Method(typeof(NCombatCardPile), nameof(NCombatCardPile.Initialize)),
            AccessTools.Method(typeof(NCombatCardPile), "AddCard"),
            AccessTools.Method(typeof(NCombatCardPile), nameof(NCombatCardPile.RemoveCard)),
        ];

        [HarmonyPostfix]
        public static void Postfix(NCombatCardPile __instance)
        {
            if (PileSight.IsHidden)
                __instance._countLabel.SetTextAutoSize(HiddenText);
        }
    }

    /// <summary>
    /// OnRelease finishes the button's press animation, then stops early if the button has no
    /// pile. Taking the pile away for that call skips both the pile screen and the empty pile
    /// message, which would give the count away. OnRelease is virtual, so it is named by string.
    /// </summary>
    [HarmonyPatch(typeof(NCombatCardPile), "OnRelease")]
    public static class BlockPileScreen
    {
        [HarmonyPrefix]
        public static void Prefix(NCombatCardPile __instance, out CardPile? __state)
        {
            __state = null;
            if (!PileSight.IsHidden)
                return;
            __state = __instance._pile;
            __instance._pile = null;
        }

        [HarmonyPostfix]
        public static void Postfix(NCombatCardPile __instance, CardPile? __state)
        {
            if (__state is null)
                return;
            __instance._pile = __state;

            if (__instance._localPlayer is not { } player || !CombatManager.Instance.IsInProgress)
                return;
            NThoughtBubbleVfx? bubble = NThoughtBubbleVfx.Create(
                new LocString("combat_messages", "AP_PILE_SIGHT_HIDDEN").GetFormattedText(),
                player.Creature,
                2.0);
            NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(bubble);
        }
    }
}
