using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using StS2AP.Domain;
using StS2AP.Models;

namespace StS2AP.Utils;

/// <summary>
/// Native reward rows for bonus items. Relic bonuses (wax or not) are ordinary relic rewards; the
/// other kinds arrive as a <see cref="BonusRewardPayload"/> and get their own reward here.
/// </summary>
public static partial class ApMirroredRewardDispatcher
{
    // Loaded once per receipt, so reopening the menu offers the same card objects.
    private static readonly Dictionary<(ulong OwnerNetId, int ItemIndex), ApBonusCardReward> BonusCardRewards = new();

    private static Reward BuildBonusReward(RewardOrigin origin, string model, Player owner)
    {
        BonusRewardPayload? payload = BonusRewardPayload.TryParse(model);
        if (payload == null)
            return new ApNativeRelicReward(DeserializeRelic(model), owner, origin, ApMirroredRewardKind.Bonus);

        switch (payload.Category)
        {
            case BonusItemDefinition.MaxHpCategory:
                return new ApBonusMaxHpReward(payload.Amount, owner, origin);
            case BonusItemDefinition.EnchantmentCategory:
            {
                BonusEnchantment enchantment = BonusEnchantmentCatalog.FindByName(payload.Enchantment ?? "")
                    ?? throw new InvalidOperationException(
                        $"AP bonus {origin.ReceiptIdentity} names unknown enchantment '{payload.Enchantment}'.");
                return new ApBonusEnchantReward(enchantment, payload.Amount, owner, origin);
            }
            case BonusItemDefinition.EnchantedCardsCategory:
                return BonusCardReward(payload, owner, origin, "Choose an enchanted card");
            case BonusItemDefinition.CardsCategory:
                return BonusCardReward(payload, owner, origin,
                    string.IsNullOrEmpty(payload.Label) ? "Choose a card" : $"Choose a card: {payload.Label}");
            default:
                return new ApUnavailableReward(origin.ItemName,
                    $"This version of the mod does not know the bonus '{payload.Category}'.", owner, origin);
        }
    }

    private static ApBonusCardReward BonusCardReward(
        BonusRewardPayload payload, Player owner, RewardOrigin origin, string description)
    {
        if (!BonusCardRewards.TryGetValue((owner.NetId, origin.ReceivedItemIndex), out ApBonusCardReward? reward))
        {
            List<CardModel> cards = (payload.Cards ?? [])
                .Select(json => owner.RunState.LoadCard(Deserialize<SerializableCard>(json), owner))
                .ToList();
            reward = new ApBonusCardReward(cards, owner, origin, description);
            BonusCardRewards[(owner.NetId, origin.ReceivedItemIndex)] = reward;
        }
        return reward;
    }

    /// <summary>Gain max HP (and as much current HP), like the game's max HP relics.</summary>
    private sealed class ApBonusMaxHpReward : Reward, IApNativeReward
    {
        private readonly int _itemIndex;
        private readonly int _amount;
        private readonly LocString _description;

        protected override RewardType RewardType => RewardType.None;
        public override int RewardsSetIndex => 3;
        public override LocString Description => _description;
        public override bool IsPopulated => true;

        public ApBonusMaxHpReward(int amount, Player player, RewardOrigin origin) : base(player)
        {
            _itemIndex = origin.ReceivedItemIndex;
            _amount = amount;
            _description = CreateApDescription($"Gain {amount} Max HP", origin);
        }

        public bool CanClaim(out string reason) =>
            MultiplayerSupport.CanClaimReceivedReward(ApMirroredRewardKind.Bonus, out reason);
        public bool HasOriginText => true;
        public bool UseAncientStyle => false;

        // Strawberry's art: the game's own picture for "more max HP".
        public override Control CreateIcon() => new TextureRect
        {
            Texture = ModelDb.Relic<Strawberry>().Icon,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            AnchorRight = 1,
            AnchorBottom = 1,
        };

        public override void Populate() { }

        protected override async Task<bool> OnSelect()
        {
            await CreatureCmd.GainMaxHp(Player.Creature, _amount);
            if (LocalContext.IsMe(Player))
                CommitDiscreteReward(_itemIndex, ApMirroredRewardKind.Bonus);
            return true;
        }

        public override void OnSkipped() { }
        public override void MarkContentAsSeen() { }
    }

    /// <summary>Choose a card in the deck to enchant, on the game's own enchant screen.</summary>
    private sealed class ApBonusEnchantReward : Reward, IApNativeReward
    {
        private readonly int _itemIndex;
        private readonly BonusEnchantment _enchantment;
        private readonly int _amount;
        private readonly LocString _description;

        protected override RewardType RewardType => RewardType.None;
        public override int RewardsSetIndex => 6;
        public override LocString Description => _description;
        public override bool IsPopulated => true;

        protected override IEnumerable<IHoverTip> ExtraHoverTips
        {
            get
            {
                EnchantmentModel preview = _enchantment.Canonical().ToMutable();
                preview.Amount = _amount;
                preview.RecalculateValues();
                return preview.HoverTips;
            }
        }

        public ApBonusEnchantReward(BonusEnchantment enchantment, int amount, Player player, RewardOrigin origin)
            : base(player)
        {
            _itemIndex = origin.ReceivedItemIndex;
            _enchantment = enchantment;
            _amount = amount;
            string title = enchantment.Canonical().Title.GetFormattedText();
            _description = CreateApDescription(amount > 1 ? $"Enchant a card: {title} {amount}" : $"Enchant a card: {title}", origin);
        }

        public bool CanClaim(out string reason)
        {
            if (!MultiplayerSupport.CanClaimReceivedReward(ApMirroredRewardKind.Bonus, out reason))
                return false;
            if (Player.Deck.Cards.Any(_enchantment.Canonical().CanEnchant))
                return true;
            reason = $"No card in your deck can take {_enchantment.Canonical().Title.GetFormattedText()} right now.";
            return false;
        }

        public bool HasOriginText => true;
        public bool UseAncientStyle => false;

        public override Control CreateIcon() => new TextureRect
        {
            Texture = _enchantment.Canonical().Icon,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            AnchorRight = 1,
            AnchorBottom = 1,
        };

        public override void Populate() { }

        protected override async Task<bool> OnSelect()
        {
            EnchantmentModel canonical = _enchantment.Canonical();
            var prefs = new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1)
            {
                Cancelable = true,
                RequireManualConfirmation = true,
            };
            CardModel? card = (await CardSelectCmd.FromDeckForEnchantment(Player, canonical, _amount, prefs))
                .FirstOrDefault();
            if (card == null)
                return false;

            CardCmd.Enchant(canonical.ToMutable(), card, _amount);
            CardCmd.Preview(card);
            if (LocalContext.IsMe(Player))
                CommitDiscreteReward(_itemIndex, ApMirroredRewardKind.Bonus);
            return true;
        }

        public override void OnSkipped() { }
        public override void MarkContentAsSeen() { }
    }

    /// <summary>A card reward whose cards were rolled (and for Enchanted Cards, enchanted) when the bonus was assigned.</summary>
    private sealed class ApBonusCardReward : CardReward, IApNativeReward
    {
        private readonly int _itemIndex;
        private readonly LocString _description;

        public override LocString Description => _description;

        public ApBonusCardReward(IReadOnlyList<CardModel> cards, Player player, RewardOrigin origin, string description)
            : base(cards, CardCreationSource.Other, player,
                new CardCreationOptions([player.Character.CardPool], CardCreationSource.Other,
                    CardRarityOddsType.RegularEncounter))
        {
            _itemIndex = origin.ReceivedItemIndex;
            _description = CreateApDescription(description, origin);
        }

        public bool CanClaim(out string reason) =>
            MultiplayerSupport.CanClaimReceivedReward(ApMirroredRewardKind.Bonus, out reason);
        public bool HasOriginText => true;
        public bool UseAncientStyle => false;

        protected override async Task<bool> OnSelect()
        {
            bool applied = await base.OnSelect();
            if (applied && LocalContext.IsMe(Player))
            {
                CommitDiscreteReward(_itemIndex, ApMirroredRewardKind.Bonus);
                BonusCardRewards.Remove((Player.NetId, _itemIndex));
            }
            return applied;
        }

        public override void OnSkipped() { }
    }
}
