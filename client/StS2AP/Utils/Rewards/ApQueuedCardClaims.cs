using System.Text.Json;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves.Runs;
using StS2AP.Models;
using StS2AP.Multiplayer;
using StS2AP.Persistence;
using StS2AP.UI;

namespace StS2AP.Utils;

/// <summary>
/// AP card rewards as queued claims (<see cref="ApQueuedClaims"/>). One <c>card_claim</c> action
/// does, on every peer, what a vanilla card reward does when selected: it generates the offer (or
/// reloads one revealed earlier) and loops over the owner's picks, applying each after the pick.
/// </summary>
/// <remarks>
/// Each pick pauses the action as vanilla in-action choices do (as <c>CardSelectCmd</c> does):
/// reserve a choice id, signal the choice begun so other players' queues keep running, the owner
/// picks and syncs while the others wait for it, then signal it ended, which resumes the action in
/// queue order. Nothing changes game state during the pause, because the code after the wait runs
/// whenever the choice message arrives; effects come after the resume. While the owner picks,
/// their own later actions wait behind this one.
///
/// A revealed offer that is skipped or closed stays in
/// <see cref="ApPlayerRunState.QueuedCardOffers"/>, written only by this action on every peer, so
/// reopening the reward offers the same cards and Strict Card Rewards can lock other card rewards
/// behind it.
/// </remarks>
internal static class ApQueuedCardClaims
{
    internal static readonly ApQueuedClaimKind<ApCardClaimActionMessage> ClaimKind = new(
        actionKey: "card_claim",
        noun: "AP card reward",
        apply: ApplyClaim,
        describe: static message => $"receipt {message.ReceivedItemIndex}, rare={message.IsRare}, "
            + $"act={message.ActIndex?.ToString() ?? "current"}, strict={message.StrictCardRewards}"
    );

    /// <summary>
    /// The queued card reward whose picker this process's player has open. The AP menu presses its
    /// Skip when it closes for travel, which is still a synced choice.
    /// </summary>
    internal static CardReward? OwnerPickerReward { get; private set; }

    internal static async Task<ApQueuedClaimResult> SelectRow(
        Guid claimId,
        int receivedItemIndex,
        bool isRare,
        int? actIndex,
        bool strictCardRewards,
        Player player) =>
        await ApQueuedClaims.SelectRow(ClaimKind, claimId, player, () => new ApCardClaimActionMessage
        {
            ReceivedItemIndex = receivedItemIndex,
            IsRare = isRare,
            ActIndex = isRare ? null : actIndex,
            StrictCardRewards = strictCardRewards,
        });

    /// <summary>The offer a queued card claim revealed for this receipt and nobody resolved yet.</summary>
    internal static ApQueuedCardOfferState? GetRevealedOffer(Player player, int receivedItemIndex) =>
        player.RunState is RunState runState
        && ApRunData.TryGetPlayerState(runState, player.NetId, out ApPlayerRunState state)
            ? state.QueuedCardOffers.GetValueOrDefault(receivedItemIndex)
            : null;

    /// <summary>
    /// Whether another revealed offer of this player can still be discarded, which is what Strict
    /// Card Rewards locks the other card rewards behind. A rerollable offer offers no Discard.
    /// </summary>
    internal static bool HasOtherDiscardableOffer(Player player, int receivedItemIndex) =>
        player.RunState is RunState runState
        && ApRunData.TryGetPlayerState(runState, player.NetId, out ApPlayerRunState state)
        && state.QueuedCardOffers.Any(entry => entry.Key != receivedItemIndex
            && !entry.Value.CanReroll
            && !entry.Value.HasBeenRerolled);

    private static async Task<ApQueuedClaimResult> ApplyClaim(ApQueuedClaim<ApCardClaimActionMessage> claim)
    {
        ApCardClaimActionMessage message = claim.Message;
        Player player = claim.Player;
        int index = message.ReceivedItemIndex;
        if (index < 0)
            return ApQueuedClaims.Refuse(ClaimKind, message, player, "it names no receipt", "Could not claim that AP card reward.");
        if (ApQueuedClaims.IsReceiptClaimed(claim, index))
        {
            return ApQueuedClaims.Refuse(ClaimKind, message, player,
                "its receipt was already claimed", "That AP card reward was already claimed.");
        }

        ApMirroredRewardDispatcher.ApNativeCardReward reward;
        try
        {
            reward = await PrepareOffer(claim);
        }
        catch (Exception ex)
        {
            return ApQueuedClaims.Refuse(ClaimKind, message, player,
                $"its offer could not be prepared ({ex.GetBaseException().Message})", "Could not open that AP card reward.");
        }
        // The offer is revealed now, before the first pause, in queue order on every peer.
        RecordOffer(claim, reward);
        LogUtility.Info(
            $"Queued AP card reward claim {message.ClaimId} attempt {message.Attempt} for player {player.NetId} "
                + $"offers [{string.Join(", ", reward.Cards.Select(DescribeCard))}], canReroll={reward.CanReroll}"
        );

        (bool rewardComplete, List<CardModel> taken) = await RunPicks(claim, reward);

        if (rewardComplete)
        {
            ApRunData.RecordQueuedCardOffer(claim.RunState, player.NetId, index, null);
            ApQueuedClaims.MarkReceiptClaimed(claim, index);
        }
        else
        {
            // Skipped or closed: keep what is left of the offer, rerolled or not, for next time.
            RecordOffer(claim, reward);
        }
        ApQueuedClaims.LogApplied(ClaimKind, message, player,
            $"complete={rewardComplete}, taken=[{string.Join(", ", taken.Select(DescribeCard))}]");
        if (rewardComplete && claim.IsLocalOwner)
            ApMirroredRewardDispatcher.CommitDiscreteReward(index, ApMirroredRewardKind.Card);
        return new ApQueuedClaimResult(rewardComplete);
    }

    /// <summary>
    /// The offer as this action shows it: reloaded from its recorded reveal, with Egg upgrades
    /// refreshed as reopening a revealed reward does today, or generated now with the receipt's AP
    /// RNG. Both run on every peer in queue order.
    /// </summary>
    private static async Task<ApMirroredRewardDispatcher.ApNativeCardReward> PrepareOffer(
        ApQueuedClaim<ApCardClaimActionMessage> claim)
    {
        ApCardClaimActionMessage message = claim.Message;
        Player player = claim.Player;
        ApQueuedCardOfferState? recorded = claim.OwnerState.QueuedCardOffers.GetValueOrDefault(message.ReceivedItemIndex);
        if (recorded == null)
        {
            List<CardCreationResult> generated = await ApMirroredRewardDispatcher.GenerateQueuedCardOffer(
                player, message.ApSlotId, message.ReceivedItemIndex, message.IsRare, message.ActIndex);
            return ApMirroredRewardDispatcher.CreateQueuedCardOffer(
                player, message.ApSlotId, message.ReceivedItemIndex, message.IsRare, message.ActIndex,
                generated, canReroll: false, hasBeenRerolled: false, message.StrictCardRewards);
        }

        List<CardCreationResult> cards = recorded.SerializedCards
            .Select(json => new CardCreationResult(claim.RunState.LoadCard(
                JsonSerializer.Deserialize<SerializableCard>(json, SerializationUtility.CombinedOptions)
                    ?? throw new InvalidOperationException("A recorded AP card was empty."),
                player)))
            .ToList();
        ApMirroredRewardDispatcher.ApNativeCardReward reward = ApMirroredRewardDispatcher.CreateQueuedCardOffer(
            player, message.ApSlotId, message.ReceivedItemIndex, message.IsRare, message.ActIndex,
            cards, recorded.CanReroll, recorded.HasBeenRerolled, message.StrictCardRewards);
        ApCardRewardLifecycle.RefreshEggUpgrades(player, reward._cards, reward.Options);
        return reward;
    }

    private static void RecordOffer(
        ApQueuedClaim<ApCardClaimActionMessage> claim,
        ApMirroredRewardDispatcher.ApNativeCardReward reward)
    {
        ApRunData.RecordQueuedCardOffer(claim.RunState, claim.Player.NetId, claim.Message.ReceivedItemIndex,
            new ApQueuedCardOfferState
            {
                SerializedCards = reward.Cards.Select(ApMirroredRewardDispatcher.SerializeCard).ToList(),
                CanReroll = reward.CanReroll,
                HasBeenRerolled = reward._hasBeenRerolled,
            });
    }

    /// <summary>
    /// Vanilla <c>CardReward.OnSelect</c>'s loop on every peer, with the action paused around each
    /// pick. Returns whether the reward completed (a card taken, Discard or Sacrifice) and the cards
    /// added to the deck.
    /// </summary>
    private static async Task<(bool RewardComplete, List<CardModel> Taken)> RunPicks(
        ApQueuedClaim<ApCardClaimActionMessage> claim,
        ApMirroredRewardDispatcher.ApNativeCardReward reward)
    {
        Player player = claim.Player;
        PlayerChoiceSynchronizer synchronizer = RunManager.Instance.PlayerChoiceSynchronizer;
        bool rewardComplete = false;
        bool endSelection = false;
        var taken = new List<CardModel>();
        // As vanilla does, the alternatives are generated once; a reroll refreshes the shown picker.
        IReadOnlyList<CardRewardAlternative> alternatives = CardRewardAlternative.Generate(reward);
        try
        {
            while (!endSelection)
            {
                uint choiceId = synchronizer.ReserveChoiceId(player);
                await GameApiCompat.SignalPlayerChoiceBegun(claim.ChoiceContext, player);
                int? index;
                if (claim.IsLocalOwner)
                {
                    index = await AskOwner(reward, alternatives);
                    synchronizer.SyncLocalChoice(player, choiceId, PlayerChoiceResult.FromIndex(index));
                }
                else
                {
                    index = (await synchronizer.WaitForRemoteChoice(player, choiceId)).AsIndexOrNull();
                }
                await claim.ChoiceContext.SignalPlayerChoiceEnded();

                // Resumed in queue order: effects from here on.
                CardModel? obtained = null;
                CardRewardAlternative? alternative = null;
                NCardHolder? holder = null;
                if (index.HasValue)
                {
                    if (index.Value < reward._cards.Count)
                    {
                        obtained = reward._cards[index.Value].Card;
                        rewardComplete = true;
                        endSelection = !Hook.ShouldAllowSelectingMoreCardRewards(player.RunState, player, reward);
                        holder = TryGetShownCardHolder(reward, obtained);
                    }
                    else if (index.Value < reward._cards.Count + alternatives.Count)
                    {
                        alternative = alternatives[index.Value - reward._cards.Count];
                        rewardComplete = alternative.AfterSelected == PostAlternateCardRewardAction.EndSelectionAndCompleteReward;
                        endSelection = alternative.AfterSelected
                            is PostAlternateCardRewardAction.EndSelectionAndCompleteReward
                            or PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward;
                    }
                    else
                    {
                        LogUtility.Error(
                            $"Queued AP card claim got pick {index} for {reward._cards.Count} cards and "
                                + $"{alternatives.Count} alternatives; asking again."
                        );
                        continue;
                    }
                }
                else
                {
                    rewardComplete = false;
                    endSelection = true;
                }
                LogUtility.Info(
                    $"Queued AP card reward claim {claim.Message.ClaimId} attempt {claim.Message.Attempt} for "
                        + $"player {player.NetId}: choice {choiceId} picked "
                        + (obtained != null ? DescribeCard(obtained)
                            : alternative != null ? alternative.OptionId
                            : "nothing (skip)")
                );

                if (obtained != null)
                {
                    CardPileAddResult added = await CardPileCmd.Add(obtained, PileType.Deck);
                    if (added.success)
                    {
                        obtained = added.cardAdded;
                        taken.Add(obtained);
                        reward._cards.RemoveAll(result => result.Card == obtained);
                        if (holder != null)
                            FlyCardToDeck(holder, obtained);
                        // A claim the host orders after a move into combat runs in that combat;
                        // the taken card joins this combat's draw pile there, on every peer.
                        await GameUtility.AddCardRewardToCombatDrawPile(obtained, player);
                    }
                }
                else if (alternative != null)
                {
                    await alternative.OnSelect();
                }
            }

            // The map point's card choice history, as vanilla card rewards fill it in.
            List<CardChoiceHistoryEntry>? history =
                player.RunState.CurrentMapPointHistoryEntry?.GetEntry(player.NetId).CardChoices;
            foreach (CardModel card in taken)
                history?.Add(new CardChoiceHistoryEntry(card, wasPicked: true));
            if (rewardComplete)
                foreach (CardCreationResult left in reward._cards)
                    history?.Add(new CardChoiceHistoryEntry(left.Card, wasPicked: false));
        }
        finally
        {
            if (claim.IsLocalOwner)
                ClosePicker(reward);
        }
        return (rewardComplete, taken);
    }

    /// <summary>
    /// Owner only: shows the picker and waits for a pick. A pick nobody can make now is a skip,
    /// still synced: the AP menu closed (for travel, say) before this claim's turn came, or the
    /// 0.113 picker was removed mid-choice, which cancels its wait instead of returning a skip.
    /// </summary>
    private static async Task<int?> AskOwner(
        ApMirroredRewardDispatcher.ApNativeCardReward reward,
        IReadOnlyList<CardRewardAlternative> alternatives)
    {
        if (!ArchipelagoRewardUI.IsOpen)
            return null;
        reward._currentlyShownScreen ??= NCardRewardSelectionScreen.ShowScreen(reward._cards, alternatives);
        if (reward._currentlyShownScreen is not { } picker)
            return null;

        OwnerPickerReward = reward;
        try
        {
            return await picker.OptionSelected();
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    // Card id and upgrade level, as the replicated path's offer log lists them.
    private static string DescribeCard(CardModel card) => $"{card.Id}+{card.CurrentUpgradeLevel}";

    private static NCardHolder? TryGetShownCardHolder(CardReward reward, CardModel card)
    {
        try
        {
            return reward._currentlyShownScreen?.GetCardHolder(card);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // Vanilla's flight of a taken card from the picker into the deck.
    private static void FlyCardToDeck(NCardHolder holder, CardModel card)
    {
        if (NRun.Instance is not { } run || holder.CardNode is not NCard cardNode)
            return;
        run.GlobalUi.ReparentCard(cardNode);
        holder.QueueFreeSafely();
        run.GlobalUi.TopBar.TrailContainer.AddChildSafely(
            NCardFlyVfx.Create(cardNode, PileType.Deck, isAddingToPile: true, card.Owner.Character.TrailPath));
    }

    private static void ClosePicker(CardReward reward)
    {
        if (ReferenceEquals(OwnerPickerReward, reward))
            OwnerPickerReward = null;
        if (reward._currentlyShownScreen is { } picker)
        {
            NOverlayStack.Instance?.Remove(picker);
            reward._currentlyShownScreen = null;
        }
    }
}
