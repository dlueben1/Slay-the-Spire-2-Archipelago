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
