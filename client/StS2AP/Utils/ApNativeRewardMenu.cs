using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Data;
using StS2AP.Extensions;
using StS2AP.Models;
using StS2AP.UI;
using STS2RitsuLib.Combat.Rewards;

namespace StS2AP.Utils;

/// <summary>
/// Builds the received-item menu from MegaCrit reward objects. AP supplies stable assignments and
/// records receipt consumption; MegaCrit owns the reward screen, selection UI, and grant lifecycle.
/// </summary>
public static class ApNativeRewardMenu
{
    private const int RewardOriginFontSize = 16;

    private enum ApRewardKind
    {
        Card,
        Relic,
        Potion,
        Ancient,

        // Kept distinct from Relic so claiming can never reach the Relic Coupon bookkeeping.
        Bonus,
    }

    private sealed record ReceiptPresentation(
        int ItemIndex,
        string ItemName,
        string SenderName,
        string FoundLocation);

    private static int _descriptionSequence;

    /// <summary>Metadata consumed by the narrow native-screen styling and selection patches.</summary>
    internal interface IApNativeReward
    {
        bool CanClaim(out string reason);
        bool HasOriginText { get; }
        bool UseAncientStyle { get; }
    }

    /// <summary>Opens one fixed snapshot of the current player's claimable AP receipts.</summary>
    public static Task<bool> Open()
    {
        Player? player = GameUtility.CurrentPlayer;
        if (player?.RunState == null)
            return Task.FromResult(false);
        if (ArchipelagoRewardUI.IsOpen)
            return Task.FromResult(true);

        try
        {
            RewardsSet set = BuildRewardsSet(player);
            Task completion = RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(set);
            ArchipelagoRewardUI.ShowNativeMenu(set, initiallyEmpty: set.Rewards.Count == 0);
            ObserveCompletion(completion);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Could not build native AP reward menu: {ex}");
            return Task.FromResult(false);
        }
    }

    private static RewardsSet BuildRewardsSet(Player player)
    {
        RelicRewardUtility.ReconcileBankedRewards(player);

        var rewards = new List<Reward>();
        ArchipelagoGoldOffer goldOffer = ArchipelagoClient.Progress.PrepareGoldOffer();
        if (goldOffer.GrantedAmount > 0)
            rewards.Add(new ApNativeGoldReward(goldOffer, player));

        foreach (IndexedItemInfo receipt in ArchipelagoClient.Progress.AllReceivedItems
                     .Where(item => ArchipelagoClient.Progress.IsAvailableInRewardMenu(item, player))
                     .OrderBy(item => item.Index))
        {
            rewards.Add(BuildNativeReward(receipt, player));
        }

        return new RewardsSet(player).WithCustomRewards(
            rewards.OrderBy(reward => reward.RewardsSetIndex).ToList()
        );
    }

    private static Reward BuildNativeReward(IndexedItemInfo receipt, Player player)
    {
        var presentation = new ReceiptPresentation(
            receipt.Index,
            receipt.Item.ItemDisplayName,
            receipt.Item.Player.Name,
            receipt.Item.LocationDisplayName
        );

        return receipt.Item.GetCharacterSpecificItemID() switch
        {
            ItemTable.APItem.CardReward => BuildCardReward(
                presentation,
                player,
                rare: false
            ),
            ItemTable.APItem.RareCardReward => BuildCardReward(
                presentation,
                player,
                rare: true
            ),
            ItemTable.APItem.Relic => BuildRelicReward(presentation, player),
            ItemTable.APItem.Potion => BuildPotionReward(presentation, player),
            ItemTable.APItem.ProgressiveAncient => BuildAncientReward(presentation, player),
            ItemTable.APItem.BonusWaxRelic => BuildBonusRelicReward(presentation, receipt, player),
            _ => new ApUnavailableReward(
                presentation,
                player,
                "This received item does not have a native reward implementation."
            ),
        };
    }

    private static Reward BuildCardReward(
        ReceiptPresentation presentation,
        Player player,
        bool rare)
    {
        CardReward? assignment = GameUtility.GetOrAssignCardReward(
            presentation.ItemIndex,
            player,
            rare
        );
        if (assignment == null)
        {
            return new ApUnavailableReward(
                presentation,
                player,
                "The card choices could not be assigned."
            );
        }

        CardCreationOptions options = CreateCardOptions(player, rare);
        return new ApNativeCardReward(
            assignment.Cards,
            player,
            options,
            presentation,
            assignment.CanReroll,
            rare
        );
    }

    private static Reward BuildRelicReward(ReceiptPresentation presentation, Player player)
    {
        IReadOnlyList<RelicModel> choices =
            ArchipelagoClient.Progress.GetOrAssignRelicChoices(
                presentation.ItemIndex,
                player,
                choiceCount: 1
            );
        if (choices.Count != 1)
        {
            return new ApUnavailableReward(
                presentation,
                player,
                "The relic choice could not be assigned."
            );
        }

        return new ApNativeRelicReward(
            CreateMutableRelic(choices[0], player),
            player,
            presentation,
            ApRewardKind.Relic
        );
    }

    private static Reward BuildPotionReward(ReceiptPresentation presentation, Player player)
    {
        PotionModel? assignment = ArchipelagoClient.Progress.GetOrAssignPotion(
            presentation.ItemIndex,
            player
        );
        if (assignment == null)
        {
            return new ApUnavailableReward(
                presentation,
                player,
                "The potion could not be assigned."
            );
        }

        PotionModel potion = assignment.ToMutable();
        potion.Owner = player;
        return new ApNativePotionReward(potion, player, presentation);
    }

    /// <summary>
    /// Builds the row for a bonus relic. Bonus items are shared by every character and are not
    /// gated by earned relic rewards, so this deliberately bypasses the Relic Coupon bookkeeping.
    /// </summary>
    private static Reward BuildBonusRelicReward(
        ReceiptPresentation presentation,
        IndexedItemInfo receipt,
        Player player)
    {
        if (!ArchipelagoClient.Progress.TryGetBonusOrdinal(
                receipt,
                out string category,
                out int ordinal))
        {
            return new ApUnavailableReward(
                presentation,
                player,
                "This bonus item does not map to a known bonus category."
            );
        }

        RelicModel? resolved = ArchipelagoClient.Progress.GetOrAssignBonusRelic(
            category,
            ordinal,
            player
        );
        if (resolved == null)
        {
            return new ApUnavailableReward(
                presentation,
                player,
                "The bonus relic could not be resolved."
            );
        }

        RelicModel relic = CreateMutableRelic(resolved, player);
        relic.IsWax = true;

        // Show the resolved relic ("Wax The Boot") rather than the generic "Bonus Wax Relic" label.
        var bonusPresentation = presentation with { ItemName = $"Wax {resolved.Title}" };
        return new ApNativeRelicReward(relic, player, bonusPresentation, ApRewardKind.Bonus);
    }

    private static Reward BuildAncientReward(ReceiptPresentation presentation, Player player)
    {        IReadOnlyList<RelicModel> choices =
            ArchipelagoClient.Progress.GetOrAssignAncientRelicChoices(
                presentation.ItemIndex,
                player
            );
        if (choices.Count != AncientRelicPool.ChoiceCount)
        {
            return new ApUnavailableReward(
                presentation with { ItemName = "Ancient Relic Choice Unavailable" },
                player,
                "No valid Ancient relic choice is available for this receipt."
            );
        }

        var children = choices
            .Select(choice => (Reward)new ApNativeRelicReward(
                CreateMutableRelic(choice, player),
                player,
                presentation,
                ApRewardKind.Ancient
            ))
            .ToList();
        return LinkedRewardSets.Create(
            children,
            player,
            LinkedRewardSelectionMode.ChooseOne
        );
    }

    private static CardCreationOptions CreateCardOptions(Player player, bool rare)
    {
        CardRarityOddsType rarity = rare
            ? CardRarityOddsType.BossEncounter
            : CardRarityOddsType.RegularEncounter;
        return BetaMainCompatibility.WithCombatRewardCompatibility(
            new CardCreationOptions(
                new[] { player.Character.CardPool },
                CardCreationSource.Encounter,
                rarity
            )
        );
    }

    private static RelicModel CreateMutableRelic(RelicModel relic, Player player)
    {
        RelicModel mutable = relic.IsMutable
            ? RelicModel.FromSerializable(relic.ToSerializable())
            : relic.ToMutable();
        mutable.Owner = player;
        return mutable;
    }

    private static void CommitReceivedItem(int itemIndex, ApRewardKind kind)
    {
        if (kind == ApRewardKind.Relic)
        {
            RelicRewardUtility.CompleteMenuClaim(itemIndex);
            return;
        }

        if (!ArchipelagoClient.Progress.UsedItems.Contains(itemIndex))
            ArchipelagoClient.Progress.UsedItems.Add(itemIndex);
        if (kind == ApRewardKind.Card)
            ArchipelagoClient.Progress.CardAssignments.Remove(itemIndex);
    }

    private static LocString CreateApDescription(
        LocString primary,
        ReceiptPresentation presentation) =>
        CreateApDescription(primary.GetFormattedText(), presentation);

    private static LocString CreateApDescription(
        string primary,
        ReceiptPresentation presentation)
    {
        string location = string.IsNullOrWhiteSpace(presentation.FoundLocation)
            ? string.Empty
            : $" ({presentation.FoundLocation})";
        string origin = string.IsNullOrWhiteSpace(presentation.SenderName)
            ? string.Empty
            : $"\n[font_size={RewardOriginFontSize}]"
                + $"[blue]from {presentation.SenderName}{location}[/blue][/font_size]";
        string key = $"AP_NATIVE_REWARD_{Interlocked.Increment(ref _descriptionSequence)}";
        TextUtility.RegisterLocString(key, primary + origin, "ap");
        return new LocString("ap", key);
    }

    private static async void ObserveCompletion(Task completion)
    {
        try
        {
            await completion;
            LogUtility.Debug("Native AP reward menu completed");
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Native AP reward menu failed: {ex}");
        }
    }

    private sealed class ApNativeGoldReward : GoldReward, IApNativeReward
    {
        private readonly ArchipelagoGoldOffer _offer;

        public ApNativeGoldReward(ArchipelagoGoldOffer offer, Player player)
            : base(offer.GrantedAmount, player) => _offer = offer;

        public bool CanClaim(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool HasOriginText => false;
        public bool UseAncientStyle => false;

        protected override async Task<bool> OnSelect()
        {
            bool applied = await base.OnSelect();
            if (!applied)
                return false;

            int currentAmount = ArchipelagoClient.Progress.ConsumeGoldOffer(_offer);
            int ascensionAdjustment = currentAmount - _offer.GrantedAmount;
            if (ascensionAdjustment > 0)
                await PlayerCmd.GainGold(ascensionAdjustment, Player);
            return true;
        }
    }

    private sealed class ApNativeRelicReward : RelicReward, IApNativeReward
    {
        private readonly int _itemIndex;
        private readonly ApRewardKind _kind;
        private readonly LocString _description;

        public override LocString Description => _description;

        public ApNativeRelicReward(
            RelicModel relic,
            Player player,
            ReceiptPresentation presentation,
            ApRewardKind kind)
            : base(relic, player)
        {
            _itemIndex = presentation.ItemIndex;
            _kind = kind;
            _description = CreateApDescription(relic.Title, presentation);
        }

        public bool CanClaim(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool HasOriginText => true;
        public bool UseAncientStyle => _kind == ApRewardKind.Ancient;

        protected override async Task<bool> OnSelect()
        {
            bool applied = await base.OnSelect();
            if (!applied)
                return false;

            CommitReceivedItem(_itemIndex, _kind);
            return true;
        }

        public override void OnSkipped() { }
    }

    private sealed class ApNativePotionReward : PotionReward, IApNativeReward
    {
        private readonly int _itemIndex;
        private readonly LocString _description;

        public override LocString Description => _description;

        public ApNativePotionReward(
            PotionModel potion,
            Player player,
            ReceiptPresentation presentation)
            : base(potion, player)
        {
            _itemIndex = presentation.ItemIndex;
            _description = CreateApDescription(potion.Title, presentation);
        }

        public bool CanClaim(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool HasOriginText => true;
        public bool UseAncientStyle => false;

        protected override async Task<bool> OnSelect()
        {
            bool applied = await base.OnSelect();
            if (applied)
                CommitReceivedItem(_itemIndex, ApRewardKind.Potion);
            return applied;
        }

        public override void OnSkipped() { }
    }

    private sealed class ApNativeCardReward : CardReward, IApNativeReward
    {
        private readonly int _itemIndex;
        private readonly bool _isRare;
        private readonly LocString _description;

        protected override string IconPath => _isRare
            ? ImageHelper.GetImagePath("ui/reward_screen/reward_icon_rare.png")
            : base.IconPath;

        public override LocString Description => _description;

        public ApNativeCardReward(
            IEnumerable<CardModel> cards,
            Player player,
            CardCreationOptions rerollOptions,
            ReceiptPresentation presentation,
            bool canReroll,
            bool isRare)
            : base(cards, CardCreationSource.Encounter, player, rerollOptions)
        {
            _itemIndex = presentation.ItemIndex;
            _isRare = isRare;
            _description = CreateApDescription(
                new LocString("gameplay_ui", "COMBAT_REWARD_ADD_CARD"),
                presentation
            );
            CanReroll = canReroll;
        }

        public bool CanClaim(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool HasOriginText => true;
        public bool UseAncientStyle => false;

        protected override async Task<bool> OnSelect()
        {
            HashSet<CardModel> deckBefore = Player.Deck.Cards.ToHashSet();
            bool applied = await base.OnSelect();
            if (!applied)
                return false;

            foreach (CardModel selected in Player.Deck.Cards.Where(card => !deckBefore.Contains(card)))
                await GameUtility.AddCardRewardToCombatDrawPile(selected, Player);
            CommitReceivedItem(_itemIndex, ApRewardKind.Card);
            return true;
        }

        public override void OnSkipped() { }
    }

    private sealed class ApUnavailableReward : Reward, IApNativeReward
    {
        private readonly LocString _description;
        private readonly string _reason;

        protected override RewardType RewardType => RewardType.None;
        public override int RewardsSetIndex => 99;
        public override LocString Description => _description;
        public override bool IsPopulated => true;

        public ApUnavailableReward(
            ReceiptPresentation presentation,
            Player player,
            string reason)
            : base(player)
        {
            _description = CreateApDescription(presentation.ItemName, presentation);
            _reason = reason;
        }

        public bool CanClaim(out string reason)
        {
            reason = _reason;
            return false;
        }

        public bool HasOriginText => true;
        public bool UseAncientStyle => false;

        public override void Populate() { }
        protected override Task<bool> OnSelect() => Task.FromResult(false);
        public override Control CreateIcon() => new();
        public override void OnSkipped() { }
        public override void MarkContentAsSeen() { }
    }
}
