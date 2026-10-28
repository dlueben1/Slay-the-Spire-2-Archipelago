namespace StS2AP.Utils;

/// <summary>
/// Chained seeds for Strict Card Rewards in singleplayer. An unopened Strict reward's cards
/// depend on how many Strict rewards were opened before it this run and on what was taken
/// from them, not on its AP item. Loading a checkpoint and repeating the same picks shows the
/// same cards, while a different pick changes every reward opened after it, so a checkpoint
/// no longer reveals all of a run's rewards for free.
/// </summary>
/// <remarks>
/// Only the player's own AP card picks feed the chain. Anything else that a player can change
/// for free (the deck, floor, gold, vanilla card picks, or which pending row they open first)
/// would turn a reload into a reroll.
/// </remarks>
internal static class StrictCardChain
{
    // Discard, and Pael's Wing's Sacrifice, complete the reward without adding a card.
    internal const string NoCardTaken = "none";

    internal static string SeedMaterial(
        string runSeed,
        int apSlotId,
        int playerSlotIndex,
        long? characterNumber,
        int revealOrdinal,
        bool isRare,
        int? actIndex,
        IReadOnlyList<string> pickHistory) =>
        string.Join(
            "|",
            "sts2ap-strict-card-rng-v1",
            runSeed,
            apSlotId,
            playerSlotIndex,
            characterNumber,
            revealOrdinal,
            isRare ? "rare" : "normal",
            actIndex,
            string.Join(";", pickHistory)
        );

    internal static string PickEntry(IEnumerable<(string CardId, int UpgradeLevel)> takenCards)
    {
        string[] cards = takenCards.Select(card => $"{card.CardId}@{card.UpgradeLevel}").ToArray();
        return cards.Length == 0 ? NoCardTaken : string.Join("+", cards);
    }
}
