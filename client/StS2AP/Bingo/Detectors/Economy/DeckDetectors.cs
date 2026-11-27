using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace StS2AP.Bingo.Detectors.Economy;

/// <summary>
/// Deck makeup: what the deck holds, upgrade levels, transforms and curses taken at events.
/// The deck is judged when a card enters it, when a card in it is upgraded or enchanted, and at
/// the checkpoints in <see cref="EconomyCheckpoints"/> (which also cover cards removed). A new
/// run's starting deck and a loaded deck are filled in directly, without hooks, so the deck is
/// never judged half-built.
/// </summary>
internal static class DeckDetectors
{
    // E18 "Have 5 upgraded cards".
    private const string FiveUpgradedSquare = "E18";

    // M10 "Have no Strikes in your deck": no card with the game's Strike tag, the tag that makes
    // Perfected Strike count "Strikes". That includes Twin Strike and the like, not only the
    // starter Strikes.
    private const string NoStrikesSquare = "M10";

    // M15 "Have 3 enchanted cards".
    private const string ThreeEnchantedSquare = "M15";

    // M16 "Have 5 copies of one card": copies of one card, upgraded or not. Starter (Basic) cards
    // are left out, as the game's Honed badge does, since a starting deck already holds 5 Strikes.
    private const string FiveCopiesSquare = "M16";

    // M17 "Have 35 cards in your deck".
    private const string ThirtyFiveCardsSquare = "M17";

    // M22 "Have 3 Curses in your deck".
    private const string ThreeCursesSquare = "M22";

    // H11 "Have no starter cards in your deck": no Basic-rarity card, the rarity of every starting
    // deck card (Strikes, Defends and each character's own starters).
    private const string NoStarterCardsSquare = "H11";

    // H12 "Upgrade every card in your deck": every card that can be upgraded at all is, and at
    // least one card is upgraded, so a deck of only Curses (or an empty one) doesn't count.
    // Curses and the statuses that can never be upgraded don't block it.
    private const string EveryCardUpgradedSquare = "H12";

    // AP-M05 / AP-H02 "Upgrade one card 3 / 6 times" (Endless Upgrades boon): any card the player
    // owns reaching that upgrade level, in the deck or a combat copy upgraded during a fight.
    private static readonly (string Key, int Level)[] UpgradeLevelSquares =
    {
        ("AP-M05", 3),
        ("AP-H02", 6),
    };

    // I-M13 "Take a Curse from an event": a Curse entering the deck while an event option is being
    // carried out. Curses added by Cursed Run at the start of an act and Death Link curses arrive
    // outside an option, so they don't count.
    private const string CurseFromEventSquare = "I-M13";

    private static readonly string[] DeckSquares =
    {
        FiveUpgradedSquare, NoStrikesSquare, ThreeEnchantedSquare, FiveCopiesSquare,
        ThirtyFiveCardsSquare, ThreeCursesSquare, NoStarterCardsSquare, EveryCardUpgradedSquare,
        UpgradeLevelSquares[0].Key, UpgradeLevelSquares[1].Key,
    };

    public static void CheckDeck(Player player)
    {
        IReadOnlyList<CardModel> deck = player.Deck.Cards;
        if (deck.Count == 0 || !EconomyDetector.AnyOpen(DeckSquares))
            return;

        int upgraded = 0, enchanted = 0, curses = 0, strikes = 0, starters = 0;
        int upgradableButNotUpgraded = 0, highestUpgradeLevel = 0;
        foreach (CardModel card in deck)
        {
            if (card.IsUpgraded)
                upgraded++;
            else if (card.MaxUpgradeLevel > 0)
                upgradableButNotUpgraded++;
            highestUpgradeLevel = Math.Max(highestUpgradeLevel, card.CurrentUpgradeLevel);
            if (card.Enchantment != null)
                enchanted++;
            if (card.Type == CardType.Curse)
                curses++;
            if (card.Tags.Contains(CardTag.Strike))
                strikes++;
            if (card.Rarity == CardRarity.Basic)
                starters++;
        }
        int mostCopies = deck
            .Where(card => card.Rarity != CardRarity.Basic)
            .GroupBy(card => card.Id)
            .Select(copies => copies.Count())
            .DefaultIfEmpty(0)
            .Max();

        BingoSquares.Reach(FiveUpgradedSquare, player, upgraded, 5);
        EconomyDetector.CompleteIf(NoStrikesSquare, strikes == 0, player);
        BingoSquares.Reach(ThreeEnchantedSquare, player, enchanted, 3);
        BingoSquares.Reach(FiveCopiesSquare, player, mostCopies, 5);
        BingoSquares.Reach(ThirtyFiveCardsSquare, player, deck.Count, 35);
        BingoSquares.Reach(ThreeCursesSquare, player, curses, 3);
        EconomyDetector.CompleteIf(NoStarterCardsSquare, starters == 0, player);
        EconomyDetector.CompleteIf(
            EveryCardUpgradedSquare, upgraded > 0 && upgradableButNotUpgraded == 0, player);
        CheckUpgradeLevel(player, highestUpgradeLevel);
    }

    private static void CheckUpgradeLevel(Player player, int upgradeLevel)
    {
        foreach ((string key, int level) in UpgradeLevelSquares)
            BingoSquares.Reach(key, player, upgradeLevel, level);
    }

    private static void CheckCurseFromEvent(Player player, CardModel card)
    {
        EconomyDetector.CompleteIf(
            CurseFromEventSquare,
            card.Type == CardType.Curse
                && card is not DeathLinkCurse
                && EventOptions.AnyInProgress
                && player.RunState.CurrentRoom is EventRoom,
            player);
    }

    /// <summary>Cards entering the deck: rewards, purchases, events, transforms.</summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardChangedPiles))]
    private static class AfterCardChangedPiles
    {
        [HarmonyPrefix]
        private static void Prefix(CardModel card) =>
            EconomyDetector.Run(nameof(AfterCardChangedPiles), card, static card =>
            {
                // The hook also fires for every draw and discard in combat; only the deck matters.
                Player? owner = card._owner;
                if (owner == null || !LocalContext.IsMe(owner) || card.Pile?.Type != PileType.Deck)
                    return;
                CheckDeck(owner);
                CheckCurseFromEvent(owner, card);
            });
    }

    /// <summary>
    /// Every upgrade (CardCmd.Upgrade) and enchantment (CardCmd.Enchant) ends with this call on the
    /// card. Cards rebuilt from a save, card library entries and hover-tip previews call it too,
    /// but those are in none of the owner's piles, so they are skipped.
    /// </summary>
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.FinalizeUpgradeInternal))]
    private static class AfterUpgradeFinalized
    {
        [HarmonyPostfix]
        private static void Postfix(CardModel __instance) =>
            EconomyDetector.Run(nameof(AfterUpgradeFinalized), __instance, static card =>
            {
                Player? owner = card._owner;
                if (owner == null || !LocalContext.IsMe(owner))
                    return;
                CardPile? pile = card.Pile;
                if (pile == null)
                    return;
                CheckUpgradeLevel(owner, card.CurrentUpgradeLevel);
                if (pile.Type == PileType.Deck)
                    CheckDeck(owner);
            });
    }

    /// <summary>
    /// Whether an event option's effect is being carried out. An option's effect may finish
    /// inside the call (before it returns its task) or later, after a card selection or an
    /// animation, so both the call itself and its unfinished task count.
    /// </summary>
    private static class EventOptions
    {
        private static int _choosing;
        private static readonly List<Task> Unfinished = new();

        public static bool AnyInProgress
        {
            get
            {
                Unfinished.RemoveAll(task => task.IsCompleted);
                return _choosing > 0 || Unfinished.Count > 0;
            }
        }

        [HarmonyPatch(typeof(EventOption), nameof(EventOption.Chosen))]
        private static class TrackChosen
        {
            // Only tracked while the square is open, so a board without it leaves events alone.
            [HarmonyPrefix]
            private static void Prefix(out bool __state)
            {
                __state = BingoSquares.IsActive && BingoSquares.IsOpen(CurseFromEventSquare);
                if (__state)
                    _choosing++;
            }

            // An async method returns its task rather than throwing, so the postfix always runs.
            [HarmonyPostfix]
            private static void Postfix(bool __state, Task __result)
            {
                if (!__state)
                    return;
                _choosing--;
                Unfinished.RemoveAll(task => task.IsCompleted);
                if (!__result.IsCompleted)
                    Unfinished.Add(__result);
            }
        }
    }
}
