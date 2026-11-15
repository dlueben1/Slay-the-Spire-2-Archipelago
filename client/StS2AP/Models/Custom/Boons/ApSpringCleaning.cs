using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using StS2AP.Utils;

namespace StS2AP.Models;

/// <summary>
/// After the Act 1 and Act 2 bosses, the rewards include a linked choice: remove a card,
/// transform a card, or upgrade 2 random cards. Taking one removes the others, through a
/// choose-one linked reward set. Singleplayer only.
/// </summary>
public sealed class ApSpringCleaning : ApRunModifier
{
    protected override string IconModifierEntry => "draft";

    // Rewards added here are rebuilt with the room's rewards when a save is loaded; they are not
    // saved themselves, so the plain Reward subclasses below need no serialization.
    public override bool TryModifyRewards(Player player, List<Reward> rewards, AbstractRoom? room)
    {
        if (room?.RoomType != RoomType.Boss || RunState.CurrentActIndex >= 2)
            return false;

        rewards.Add(ApLinkedRewardSets.CreateChooseOne(
            [new CardRemovalReward(player), new ApTransformCardReward(player), new ApUpgradeCardsReward(player)],
            player));
        return true;
    }
}

/// <summary>A reward to transform one card of the player's choice.</summary>
public sealed class ApTransformCardReward(Player player) : Reward(player)
{
    protected override RewardType RewardType => RewardType.None;
    public override int RewardsSetIndex => 7;
    public override bool IsPopulated => true;
    public override LocString Description => new("gameplay_ui", "AP_REWARD_TRANSFORM_CARD");
    protected override string IconPath => ImageHelper.GetImagePath("ui/reward_screen/reward_icon_card_removal.png");

    public override void Populate()
    {
    }

    protected override async Task<bool> OnSelect()
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, 1)
        {
            Cancelable = true,
        };
        CardModel? card = (await CardSelectCmd.FromDeckForTransformation(Player, prefs)).FirstOrDefault();
        if (card == null)
            return false;
        await CardCmd.TransformToRandom(card, Player.PlayerRng.Transformations, CardPreviewStyle.EventLayout);
        return true;
    }

    public override void MarkContentAsSeen()
    {
    }
}

/// <summary>A reward that upgrades 2 random upgradable cards.</summary>
public sealed class ApUpgradeCardsReward(Player player) : Reward(player)
{
    private const int CardsToUpgrade = 2;

    protected override RewardType RewardType => RewardType.None;
    public override int RewardsSetIndex => 7;
    public override bool IsPopulated => true;
    public override LocString Description => new("gameplay_ui", "AP_REWARD_UPGRADE_CARDS");
    protected override string IconPath => ImageHelper.GetImagePath("ui/rest_site/option_smith.png");

    public override void Populate()
    {
    }

    protected override Task<bool> OnSelect()
    {
        // A dedicated stream, named for this floor, so the pick does not shift the run's other
        // random rolls and stays the same after a reload.
        var rng = new Rng(Player.RunState.Rng.Seed, $"ap_spring_cleaning_{Player.RunState.TotalFloor}");
        List<CardModel> upgraded = Player.Deck.Cards
            .Where(card => card.IsUpgradable)
            .ToList()
            .StableShuffle(rng)
            .Take(CardsToUpgrade)
            .ToList();
        foreach (CardModel card in upgraded)
            CardCmd.Upgrade(card);
        return Task.FromResult(true);
    }

    public override void MarkContentAsSeen()
    {
    }
}
