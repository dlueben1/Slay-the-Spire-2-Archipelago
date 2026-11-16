using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.Cards;
using StS2AP.Data;

namespace StS2AP.Utils.Combat;

/// <summary>
/// The Hidden Cards challenge: cards look like the Compendium's unseen cards (blurred art, unknown
/// name and text, "?" cost, no tooltips) until the slot receives any Card Sight.
/// </summary>
internal static class CardSight
{
    // Cards this challenge hid, so a Card Sight restores them and leaves cards the game itself
    // shows as unseen or locked alone.
    private static readonly ConditionalWeakTable<NCard, object> HiddenCards = new();

    internal static bool IsHidden =>
        ChallengeSightItems.IsHidden(
            ArchipelagoClient.Settings?.HiddenCards == true,
            ItemTable.APItem.CardSight);

    /// <summary>
    /// The Card Sight bonus: card rewards offer one more card, and their Act 1 cards get an
    /// upgrade chance. Singleplayer only.
    /// </summary>
    internal static bool HasBonus =>
        ChallengeSightItems.HasGameplayBonus(
            ArchipelagoClient.Settings?.HiddenCards == true,
            ItemTable.APItem.CardSight,
            "CardSight");

    [ThreadStatic]
    private static int s_rewardDepth;

    [ThreadStatic]
    private static bool s_extraCardPending;

    /// <summary>
    /// Marks the generation of one card reward's cards, from the game's own rewards or AP's. Inside
    /// it, the first card generation call takes the bonus's extra card, and reward cards get the
    /// Act 1 upgrade chance. Cards that relics such as Lasting Candy generate inside the reward come
    /// later and take no extra card.
    /// </summary>
    internal static IDisposable EnterRewardGeneration(bool addsExtraCard)
    {
        s_rewardDepth++;
        if (s_rewardDepth == 1)
            s_extraCardPending = addsExtraCard && HasBonus;
        return new RewardGenerationScope();
    }

    internal static bool IsGeneratingReward => s_rewardDepth > 0;

    /// <summary>The bonus's extra card, once per reward: 1 for the first call, then 0.</summary>
    internal static int TakeExtraRewardCard()
    {
        if (!s_extraCardPending)
            return 0;
        s_extraCardPending = false;
        return 1;
    }

    private sealed class RewardGenerationScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (--s_rewardDepth == 0)
                s_extraCardPending = false;
        }
    }

    /// <summary>
    /// Switches a card the game would show normally to its unseen look, and says whether it did.
    /// This sets the field, not the property, because the property setter reloads the card and
    /// the patched callers draw it next anyway.
    /// </summary>
    internal static bool HideIfNeeded(NCard card)
    {
        if (card._visibility != ModelVisibility.Visible || !IsHidden)
            return false;
        card._visibility = ModelVisibility.NotSeen;
        HiddenCards.AddOrUpdate(card, null!);
        return true;
    }

    /// <summary>Applies a newly received Card Sight to the cards already on screen.</summary>
    internal static void RefreshVisibleCards()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;
        var cards = new List<NCard>();
        CollectCards(tree.Root, cards);
        foreach (NCard card in cards)
        {
            if (!HiddenCards.TryGetValue(card, out _))
                continue;
            HiddenCards.Remove(card);
            if (card._visibility != ModelVisibility.NotSeen || card.Model is null || !card.IsNodeReady())
                continue;
            // The setter reloads the art; the text, title and cost are drawn by UpdateVisuals.
            card.Visibility = ModelVisibility.Visible;
            card.UpdateVisuals(card.DisplayingPile, CardPreviewMode.Normal);
        }
    }

    private static void CollectCards(Node node, List<NCard> cards)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is NCard card)
                cards.Add(card);
            CollectCards(child, cards);
        }
    }
}
