using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using StS2AP.Data;
using StS2AP.Domain;
using StS2AP.DomainAdapters;
using StS2AP.Extensions;
using StS2AP.Patches;
using StS2AP.UI;
using STS2RitsuLib.Combat.Rewards;
using StS2AP.Utils.RunGraph;

namespace StS2AP.Utils;

/// <summary>
/// Builds the AP reward menu as a native reward screen. In singleplayer it is a reward set begun on
/// the native synchronizer. In multiplayer it is the owner's alone, and each row's claim is a
/// queued action every peer executes in order (<see cref="ApQueuedClaims"/>).
/// </summary>
public static class ApMirroredRewardDispatcher
{
    private const string ReplicatedCardStrategyId = "ap_rng_replicated_card_v1";
    private const string OwnerFinalApRngStrategyId = "ap_rng_owner_final_v1";

    // The card reward row for each card receipt with an assignment, so a rebuilt menu shows the
    // same reward object and its revealed cards (singleplayer; multiplayer cards are queued claims).
    private static readonly Dictionary<(ulong OwnerNetId, int ItemIndex), ApNativeCardReward>
        CardAssignmentRewards = new();

    public static void EndRun()
    {
        CardAssignmentRewards.Clear();
    }

    /// <summary>Opens the local player's current AP receipt catalog as a native reward screen.</summary>
    public static async Task<bool> OpenMenu()
    {
        int apLifecycleVersion = ArchipelagoRewardUI.ApLifecycleVersion;
        Player? player = GameUtility.CurrentPlayer;
        if (player?.RunState is not RunState runState)
            return false;

        if (ArchipelagoRewardUI.IsOpen)
            return true;

        if (MultiplayerSupport.IsLocalGuest)
        {
            // A vanilla guest has no AP receipt source. Do not advance the synchronized reward-set
            // sequence for a screen which can never originate a selection.
            var emptySet = new RewardsSet(player);
            ArchipelagoRewardUI.ShowNativeMenu(
                emptySet,
                Guid.NewGuid(),
                synchronized: false,
                initiallyEmpty: true
            );
            return true;
        }

        ApRewardMenuSpec spec;
        IReadOnlyList<MirroredReward> rewards;
        // In multiplayer every claim is a queued action (ApQueuedClaims), and the menu is this
        // player's alone.
        bool multiplayer = MultiplayerSupport.IsRealMultiplayerRun;
        try
        {
            // The relic and Ancient assignments the menu needs are drawn first, by an action every
            // peer executes in queue order; the menu opens once it has executed here.
            IReadOnlySet<int>? approvedRelics = multiplayer
                ? await ApQueuedRelicClaims.PrepareMenu(player)
                : null;
            if (apLifecycleVersion != ArchipelagoRewardUI.ApLifecycleVersion
                || (multiplayer && !ArchipelagoRewardUI.CanBuildMenuAfterAwait(apLifecycleVersion)))
                return false;
            spec = BuildOwnerMenuSpec(player, runState, approvedRelics, queuedClaims: multiplayer);
            rewards = DecodeRewards(spec);
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Could not build native AP reward menu: {ex}");
            NotificationUtility.ShowRawText("Could not prepare AP rewards. Try opening the menu again.");
            return false;
        }

        RewardsSet set = BuildRewardsSet(spec, rewards, player);
        if (multiplayer)
        {
            // No other peer builds this set or sees its selections, so it takes no reward-set id:
            // the synchronizer never hears of it, and each row's claim is its own queued action.
            ArchipelagoRewardUI.ShowNativeMenu(
                set,
                spec.MenuId,
                synchronized: false,
                initiallyEmpty: set.Rewards.Count == 0
            );
            return true;
        }

        Task completion = RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(set);
        ArchipelagoRewardUI.ShowNativeMenu(
            set,
            spec.MenuId,
            synchronized: true,
            initiallyEmpty: set.Rewards.Count == 0
        );
        _ = ObserveOwnerCompletionAsync(spec, completion);
        await Task.Yield();
        return true;
    }

    /// <summary>
    /// Strict Card Rewards for the next menu. A local override wins over slot data, except
    /// on a multiplayer host, whose settings were frozen (with the override applied) at launch.
    /// It is read each time the menu opens, so changing it takes effect on the next opening.
    /// </summary>
    internal static bool StrictCardRewardsEnabled
    {
        get
        {
            ClientSettings local = ArchipelagoClient.LocalSettings.Value;
            return !MultiplayerSupport.UsesFrozenHostSettings && local.OverrideStrictCardRewards
                ? local.StrictCardRewards
                : ArchipelagoClient.Settings?.StrictCardRewards ?? false;
        }
    }

    /// <param name="queuedClaims">
    /// Whether this menu's rows are queued claims (<see cref="ApQueuedClaims"/>). Its relic
    /// receipts are then the ones the menu's relic_assignment action already assigned.
    /// </param>
    private static ApRewardMenuSpec BuildOwnerMenuSpec(
        Player player,
        RunState runState,
        IReadOnlySet<int>? approvedRelics,
        bool queuedClaims)
    {
        Guid runId = Guid.Empty;
        if (MultiplayerSupport.IsRealMultiplayerRun)
        {
            if (!ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
                || shared.RunId == Guid.Empty)
            {
                throw new InvalidOperationException("No shared AP run state exists.");
            }
            runId = shared.RunId;
        }

        int apSlotId = MultiplayerSupport.PreparedApSlotId ?? 0;
        var menu = new ApRewardMenuSpec
        {
            RunId = runId,
            MenuId = Guid.NewGuid(),
            ApSlotId = apSlotId,
            OwnerNetId = player.NetId,
            StrictCardRewards = StrictCardRewardsEnabled,
        };

        // The relic_assignment action already paired banked rewards with waiting receipts.
        if (!queuedClaims)
            RelicRewardUtility.ReconcileBankedRewards(player);

        ApGoldClaim? gold = ApGrantDispatcher.MaterializeGoldClaim();
        if (gold != null)
        {
            menu.Gold = new ApMenuGoldSpec
            {
                SourceAmount = gold.SourceAmount,
                GrantedAmount = gold.GrantedAmount,
                RedeemedRawAfter = gold.RedeemedRawAfter,
                QueuedClaim = queuedClaims,
            };
        }

        foreach (IndexedItemInfo receipt in GetMenuReceipts())
        {
            MultiplayerFeature feature = MultiplayerSupport.GetFeatureForItem(receipt);
            bool featureEnabled = MultiplayerSupport.IsFeatureEnabled(feature);
            if (featureEnabled)
            {
                if (!ArchipelagoClient.Progress.IsAvailableInRewardMenu(receipt, player)
                    || !TryGetMirroredKind(receipt, out ApMirroredRewardKind kind))
                {
                    continue;
                }

                if (kind == ApMirroredRewardKind.Relic && approvedRelics != null
                    && !approvedRelics.Contains(receipt.Index)) continue;

                menu.Rewards.Add(BuildAssignedSpec(receipt, player, apSlotId, kind, queuedClaims));
                continue;
            }

            bool belongsToCharacter = ArchipelagoIdCodec.IsUniversalItemId(receipt.Item.ItemId)
                || receipt.Item.GetAPCharacterNumber() == player.GetAPCharacterNumber();
            if (!MultiplayerSupport.IsMultiplayerScope
                || !belongsToCharacter
                || ArchipelagoClient.Progress.Items.IsUsed(receipt.Index))
            {
                continue;
            }

            menu.Rewards.Add(new ApMirroredRewardSpec
            {
                ApSlotId = apSlotId,
                ReceivedItemIndex = receipt.Index,
                OwnerNetId = player.NetId,
                Kind = ApMirroredRewardKind.Unavailable,
                ItemName = receipt.Item.ItemDisplayName,
                SenderName = receipt.Item.Player.Name,
                FoundLocation = receipt.Item.LocationDisplayName,
                UnavailableReason = $"Unavailable in experimental multiplayer ({feature}).",
            });
        }

        // Persist all newly materialized assignments in one revision after the complete menu
        // snapshot exists. A no-change call is intentionally a cheap no-op.
        if (!ApRunData.PublishLocalProgress(player))
            throw new InvalidOperationException("The AP reward assignments could not reach the host.");

        menu.Rewards = menu.Rewards
            .OrderBy(spec => GetNativeOrder(spec.Kind))
            .ThenBy(spec => spec.ReceivedItemIndex)
            .ToList();
        return menu;
    }

    // Every receipt the menu considers, in AP order, including ones multiplayer can't offer yet.
    private static IEnumerable<IndexedItemInfo> GetMenuReceipts() =>
        ArchipelagoClient.Progress.AllReceivedItems
            .Concat(MultiplayerSupport.PendingUnsupportedItems)
            .GroupBy(receipt => receipt.Index)
            .Select(group => group.First())
            .OrderBy(receipt => receipt.Index);

    /// <summary>
    /// The receipts of one kind that the owner's next menu offers as claimable rows: the same
    /// filter <see cref="BuildOwnerMenuSpec"/> applies before it builds each row.
    /// </summary>
    internal static IEnumerable<IndexedItemInfo> GetClaimableMenuReceipts(Player player, ApMirroredRewardKind kind) =>
        GetMenuReceipts().Where(receipt =>
            MultiplayerSupport.IsFeatureEnabled(MultiplayerSupport.GetFeatureForItem(receipt))
            && ArchipelagoClient.Progress.IsAvailableInRewardMenu(receipt, player)
            && TryGetMirroredKind(receipt, out ApMirroredRewardKind receiptKind)
            && receiptKind == kind);

    private static ApMirroredRewardSpec BuildAssignedSpec(
        IndexedItemInfo receipt,
        Player player,
        int apSlotId,
        ApMirroredRewardKind kind,
        bool queuedClaims)
    {
        int itemIndex = receipt.Index;
        var spec = new ApMirroredRewardSpec
        {
            ApSlotId = apSlotId,
            ReceivedItemIndex = itemIndex,
            OwnerNetId = player.NetId,
            Kind = kind,
            ItemName = receipt.Item.ItemDisplayName,
            SenderName = receipt.Item.Player.Name,
            FoundLocation = receipt.Item.LocationDisplayName,
        };

        switch (kind)
        {
            case ApMirroredRewardKind.Card when queuedClaims:
            {
                // Only the recipe: the claim action generates the offer, or reloads the one it
                // revealed, on every peer.
                bool rare = receipt.Item.GetCharacterItemType() == ItemTable.APItem.RareCardReward;
                spec.IsRareCardReward = rare;
                spec.CardRewardActIndex = rare ? null : GameUtility.GetCardRewardActIndex(itemIndex, player);
                spec.MaterializationStrategyId = ReplicatedCardStrategyId;
                spec.QueuedClaim = true;
                break;
            }
            case ApMirroredRewardKind.Card:
            {
                bool rare = receipt.Item.GetCharacterItemType() == ItemTable.APItem.RareCardReward;
                spec.IsRareCardReward = rare;
                spec.CardRewardActIndex = rare ? null : GameUtility.GetCardRewardActIndex(itemIndex, player);
                bool isNew = !ArchipelagoClient.Progress.CardAssignments.TryGetValue(
                    itemIndex,
                    out CardReward? existing
                );

                spec.MaterializationStrategyId = ReplicatedCardStrategyId;
                // The menu publishes only a recipe for a new card receipt. No cards or relic
                // effects exist until the owning player opens this particular picker.
                if (isNew)
                    break;

                spec.CardCanReroll = existing!.CanReroll;
                ApNativeCardReward reward = existing as ApNativeCardReward
                    ?? RestoreCardReward(MirroredRewardAdapter.Origin(spec),
                        MirroredRewardAdapter.CardConfiguration(spec), player, existing.Cards);
                spec.MaterializationStrategyId = reward.MaterializationStrategyId;
                spec.CardHasBeenRevealed = reward.HasBeenRevealed;
                reward.Configure(MirroredRewardAdapter.Origin(spec), MirroredRewardAdapter.CardConfiguration(spec));
                ArchipelagoClient.Progress.CardAssignments[itemIndex] = reward;

                CardAssignmentRewards[(player.NetId, itemIndex)] = reward;
                spec.SerializedModels = reward.Cards.Select(SerializeCard).ToList();
                break;
            }
            case ApMirroredRewardKind.Bonus:
            {
                RelicModel? relic = BonusRewardUtility.GetOrAssign(receipt, player);
                if (relic == null)
                {
                    spec.Kind = ApMirroredRewardKind.Unavailable;
                    spec.UnavailableReason = "No eligible bonus wax relic is configured for this receipt.";
                    break;
                }
                spec.SerializedModels.Add(SerializeRelic(relic));
                spec.QueuedClaim = queuedClaims;
                break;
            }
            case ApMirroredRewardKind.Relic when queuedClaims:
            {
                // Every peer drew and recorded this relic in the menu's relic_assignment action.
                spec.SerializedModels.Add(
                    ApQueuedRelicClaims.GetAssignedRelic((RunState)player.RunState, player, itemIndex));
                spec.QueuedClaim = true;
                break;
            }
            case ApMirroredRewardKind.Relic:
            {
                IReadOnlyList<RelicModel> choices =
                    ArchipelagoClient.Progress.GetOrAssignRelicChoices(itemIndex, player, 1);
                if (choices.Count != 1)
                    throw new InvalidOperationException($"Could not assign relic reward {itemIndex}.");
                spec.SerializedModels.Add(SerializeRelic(choices[0]));
                break;
            }
            case ApMirroredRewardKind.Potion:
            {
                bool isNew = !ArchipelagoClient.Progress.PotionAssignments.TryGetValue(
                    itemIndex,
                    out PotionModel? potion
                );
                spec.MaterializationStrategyId = OwnerFinalApRngStrategyId;
                if (isNew)
                {
                    potion = PotionFactory.CreateRandomPotionOutOfCombat(
                        player, CreateApRewardRng(spec, player, "potion")).ToMutable();
                    ArchipelagoClient.Progress.PotionAssignments[itemIndex] = potion;
                    LogUtility.Info(
                        $"Materialized AP potion reward {spec.GrantId} with {OwnerFinalApRngStrategyId} "
                            + $"as {potion.Id}"
                    );
                }

                spec.SerializedModels.Add(SerializePotion(potion!));
                spec.QueuedClaim = queuedClaims;
                break;
            }
            case ApMirroredRewardKind.Ancient when queuedClaims:
            {
                // Every peer drew these in the menu's relic_assignment action.
                IReadOnlyList<string>? assigned =
                    ApQueuedRelicClaims.GetAssignedAncientChoices((RunState)player.RunState, player, itemIndex);
                if (assigned == null || assigned.Count != AncientRelicPool.ChoiceCount)
                {
                    spec.Kind = ApMirroredRewardKind.Unavailable;
                    spec.ItemName = "Ancient Relic Choice Unavailable";
                    spec.UnavailableReason =
                        "No valid Ancient relic choice is available for this receipt.";
                    break;
                }
                spec.SerializedModels = assigned.ToList();
                spec.QueuedClaim = true;
                break;
            }
            case ApMirroredRewardKind.Ancient:
            {
                IReadOnlyList<RelicModel> choices =
                    ArchipelagoClient.Progress.GetOrAssignAncientRelicChoices(itemIndex, player);
                if (choices.Count != AncientRelicPool.ChoiceCount)
                {
                    // The old AP menu represented a surplus Progressive Ancient, or a choice
                    // whose pool could not be built, as an empty disabled chest. Preserve that
                    // fail-closed row without preventing every other native reward from opening.
                    spec.Kind = ApMirroredRewardKind.Unavailable;
                    spec.ItemName = "Ancient Relic Choice Unavailable";
                    spec.UnavailableReason =
                        "No valid Ancient relic choice is available for this receipt.";
                    break;
                }
                spec.SerializedModels = choices.Select(SerializeRelic).ToList();
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return spec;
    }

    private static IReadOnlyList<MirroredReward> DecodeRewards(ApRewardMenuSpec menu)
    {
        if (menu.Rewards == null)
            throw new InvalidOperationException("Invalid AP reward-menu payload.");
        return Array.AsReadOnly(menu.Rewards.Select(spec =>
        {
            if (spec == null || spec.OwnerNetId != menu.OwnerNetId || spec.ApSlotId != menu.ApSlotId)
                throw new InvalidOperationException("AP reward-menu entry had mismatched ownership.");
            return MirroredRewardAdapter.Decode(spec, AncientRelicPool.ChoiceCount);
        }).ToArray());
    }

    private static RewardsSet BuildRewardsSet(
        ApRewardMenuSpec menu, IReadOnlyList<MirroredReward> assignments, Player owner)
    {
        var rewards = new List<Reward>();
        if (menu.Gold != null)
            rewards.Add(new ApNativeGoldReward(menu.Gold.ToClaim(), menu.Gold.QueuedClaim, owner));

        // DecodeRewards keeps the menu's order, so each decoded reward pairs with its wire entry.
        for (int i = 0; i < assignments.Count; i++)
        {
            Reward reward = BuildNativeReward(assignments[i], menu.Rewards[i].QueuedClaim, owner);
            if (reward is ApNativeCardReward card)
                card.RequiresCommitment = menu.StrictCardRewards;
            rewards.Add(reward);
        }

        return new RewardsSet(owner).WithCustomRewards(rewards);
    }

    private static Reward BuildNativeReward(MirroredReward spec, bool queued, Player owner) =>
        spec.Match<Reward>(
            card => BuildCardReward(spec.Origin, card, owner, queued),
            potion => new ApNativePotionReward(
                LoadPotion(potion.Model, spec.Origin.ReceiptIdentity), owner, spec.Origin, potion.Model, queued),
            relic => BuildStandardRelicReward(spec.Origin, relic, owner, queued),
            choices => BuildAncientReward(spec.Origin, choices, owner, queued),
            reason => new ApUnavailableReward(spec.Origin.ItemName, reason, owner, spec.Origin),
            relic => new ApNativeRelicReward(
                DeserializeRelic(relic), owner, spec.Origin, ApMirroredRewardKind.Bonus,
                queued, serializedBonusRelic: relic));

    private static Reward BuildCardReward(RewardOrigin origin, CardRewardData card, Player player, bool queued)
    {
        // A queued card row is only a stub: its claim action holds the offer on every peer.
        if (queued)
        {
            ApNativeCardReward stub = RestoreCardReward(origin, card.Configuration, player, Array.Empty<CardModel>());
            stub.IsQueuedClaim = true;
            return stub;
        }

        if (!CardAssignmentRewards.TryGetValue(
                (player.NetId, origin.ReceivedItemIndex),
                out ApNativeCardReward? reward))
        {
            reward = RestoreCardReward(
                origin,
                card.Configuration,
                player,
                DeserializeCards(card, player)
            );
            CardAssignmentRewards[(player.NetId, origin.ReceivedItemIndex)] = reward;
        }

        reward.Configure(origin, card.Configuration);
        return reward;
    }

    /// <summary>
    /// The owner rolls each potion once and every menu carries that exact model, so all players
    /// load the same potion from the same message and there is no second copy to compare. A
    /// queued potion claim loads it again from its action the same way.
    /// </summary>
    internal static PotionModel LoadPotion(string model, string receiptIdentity)
    {
        SerializablePotion serialized = Deserialize<SerializablePotion>(model);
        PotionModel potion = PotionModel.FromSerializable(serialized);
        // An unknown ID loads as DeprecatedPotion, which would give this player a different potion.
        if (potion.Id != serialized.Id)
        {
            throw new InvalidOperationException(
                $"AP potion assignment {receiptIdentity} names {serialized.Id}, "
                    + "which this game does not have."
            );
        }
        return potion;
    }

    /// <summary>Loads a relic from a queued claim's action, refusing IDs this game does not have.</summary>
    internal static RelicModel LoadRelic(string model, string receiptIdentity)
    {
        SerializableRelic serialized = Deserialize<SerializableRelic>(model);
        RelicModel relic = RelicModel.FromSerializable(serialized);
        // An unknown ID loads as DeprecatedRelic, which would give this player a different relic.
        if (relic.Id != serialized.Id)
        {
            throw new InvalidOperationException(
                $"AP relic {receiptIdentity} names {serialized.Id}, which this game does not have."
            );
        }
        return relic;
    }

    private static Reward BuildAncientReward(
        RewardOrigin origin,
        IReadOnlyList<string> models,
        Player player,
        bool queued)
    {
        var children = models
            .Select((serialized, option) =>
            {
                RelicModel relic = DeserializeRelic(serialized);
                // These are fresh, unclaimed choices. Some Ancient saved-property setters (for
                // example Pumpkin Candle at zero kindle and Pael's Tooth with no stored cards)
                // mark a deserialized model Disabled even though the native Ancient presents the
                // same fresh model as Normal until its AfterObtained initialization runs.
                relic.Status = RelicStatus.Normal;
                return (Reward)new ApNativeRelicReward(
                    relic,
                    player,
                    origin,
                    ApMirroredRewardKind.Ancient,
                    queued,
                    ancientOption: option
                );
            })
            .ToList();
        return LinkedRewardSets.Create(children, player, LinkedRewardSelectionMode.ChooseOne);
    }

    private static Reward BuildStandardRelicReward(
        RewardOrigin origin,
        string model,
        Player player,
        bool queued)
    {
        RelicModel relic = DeserializeRelic(model);
        // A queued relic's assignment action already recorded and reserved it on every peer.
        if (queued)
            return new ApNativeRelicReward(relic, player, origin, ApMirroredRewardKind.Relic, queued);

        StandardRelicPool.ReserveChoice(player, relic);
        return new ApNativeRelicReward(
            relic,
            player,
            origin,
            ApMirroredRewardKind.Relic
        );
    }

    private static CardCreationOptions CreateCardOptions(Player player, bool rare)
    {
        CardRarityOddsType rarity = rare
            ? CardRarityOddsType.BossEncounter
            : CardRarityOddsType.RegularEncounter;
        return GameApiCompat.WithCombatRewardCompatibility(
            new CardCreationOptions(
                new[] { player.Character.CardPool },
                CardCreationSource.Encounter,
                rarity
            )
        );
    }

    private static Rng CreateApRewardRng(
        ApMirroredRewardSpec spec,
        Player player,
        string domain) =>
        CreateRngFromSeedMaterial(string.Join(
            "|",
            "sts2ap-reward-rng-v1",
            domain,
            player.RunState.Rng.StringSeed,
            spec.ApSlotId,
            player.RunState.GetPlayerSlotIndex(player),
            player.GetAPCharacterNumber(),
            spec.ReceivedItemIndex
        ));

    private static Rng CreateStrictCardRng(ApMirroredRewardSpec spec, Player player) =>
        CreateRngFromSeedMaterial(StrictCardChain.SeedMaterial(
            player.RunState.Rng.StringSeed,
            spec.ApSlotId,
            player.RunState.GetPlayerSlotIndex(player),
            player.GetAPCharacterNumber(),
            ArchipelagoClient.Progress.StrictCardRevealCount,
            spec.IsRareCardReward,
            spec.CardRewardActIndex,
            ArchipelagoClient.Progress.StrictCardPickHistory
        ));

    private static Rng CreateRngFromSeedMaterial(string seedMaterial)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(seedMaterial));
        // The main build accepts a 32-bit seed while the beta build widens the same value.
        // Using the shared 32-bit seed keeps materialization deterministic across both variants.
        return new Rng(BinaryPrimitives.ReadUInt32LittleEndian(digest));
    }

    private static async Task<List<CardCreationResult>> GenerateCardChoices(
        ApMirroredRewardSpec spec,
        Player player,
        bool strictChain)
    {
        if (player.RunState is not RunState runState)
            throw new InvalidOperationException("AP reward generation requires a native run state.");
        CardRewardConfiguration configuration = MirroredRewardAdapter.CardConfiguration(spec);
        Rng rng = strictChain
            ? CreateStrictCardRng(spec, player)
            : CreateApRewardRng(spec, player, "card");
        CardCreationOptions options = CreateCardOptions(player, configuration.Recipe.IsRareReward)
            .WithFlags(CardCreationFlags.IsCardReward)
            .WithRngOverride(rng);

        List<CardCreationResult> cards;
        List<AbstractModel> modifiers;
        bool modified;
        using (Patches_APCardRewardUpgradeOdds.EnterApRewardRng(rng))
        using (Patches_APCardRewardUpgradeOdds.EnterRewardAct(configuration.Recipe.ActIndex))
        {
            cards = Patches_APCardRewardUpgradeOdds.RunDeferringOptionHooks(
                () => CardFactory.CreateForReward(player, 3, options).ToList()
            );

            modified = Hook.TryModifyCardRewardOptions(
                player.RunState,
                player,
                cards,
                options,
                out modifiers
            );
        }
        if (modified)
            await Hook.AfterModifyingCardRewardOptions(player.RunState, modifiers);

        return cards;
    }

    private static List<CardCreationResult> RefreshCardChoices(CardReward reward)
    {
        // Preserve native modifier provenance; each replica already owns its generated cards.
        List<CardCreationResult> cards = reward._cards.ToList();
        ApCardRewardLifecycle.RefreshEggUpgrades(reward.Player, cards, reward.Options);
        return cards;
    }

    private static ApNativeCardReward RestoreCardReward(
        RewardOrigin origin,
        CardRewardConfiguration configuration,
        Player player,
        IEnumerable<CardModel> cards) =>
        new(
            cards.Select(card => new CardCreationResult(card)).ToList(),
            player,
            CreateCardOptions(player, configuration.Recipe.IsRareReward)
                .WithFlags(CardCreationFlags.IsCardReward),
            origin,
            configuration
        );

    /// <summary>
    /// Generates a queued card claim's offer with the receipt's AP RNG, as a singleplayer first
    /// reveal outside Strict does. Runs on every peer inside the claim action.
    /// </summary>
    internal static Task<List<CardCreationResult>> GenerateQueuedCardOffer(
        Player player,
        int apSlotId,
        int itemIndex,
        bool rare,
        int? actIndex) =>
        GenerateCardChoices(QueuedCardSpec(player, apSlotId, itemIndex, rare, actIndex, revealed: false, canReroll: false),
            player, strictChain: false);

    /// <summary>
    /// The card reward a queued card claim action picks from. It is not a menu row: it carries the
    /// offer, the Discard rule under Strict Card Rewards, and native rerolls.
    /// </summary>
    internal static ApNativeCardReward CreateQueuedCardOffer(
        Player player,
        int apSlotId,
        int itemIndex,
        bool rare,
        int? actIndex,
        IReadOnlyList<CardCreationResult> cards,
        bool canReroll,
        bool hasBeenRerolled,
        bool strictCardRewards)
    {
        ApMirroredRewardSpec spec = QueuedCardSpec(player, apSlotId, itemIndex, rare, actIndex, revealed: true, canReroll);
        var reward = new ApNativeCardReward(
            cards,
            player,
            CreateCardOptions(player, rare).WithFlags(CardCreationFlags.IsCardReward),
            MirroredRewardAdapter.Origin(spec),
            MirroredRewardAdapter.CardConfiguration(spec)
        )
        {
            RequiresCommitment = strictCardRewards,
        };
        reward._hasBeenRerolled = hasBeenRerolled;
        // A reroll regenerates through the native Populate, which a menu row only allows once open.
        reward.AllowPopulate();
        return reward;
    }

    private static ApMirroredRewardSpec QueuedCardSpec(
        Player player,
        int apSlotId,
        int itemIndex,
        bool rare,
        int? actIndex,
        bool revealed,
        bool canReroll) =>
        new()
        {
            ApSlotId = apSlotId,
            ReceivedItemIndex = itemIndex,
            OwnerNetId = player.NetId,
            Kind = ApMirroredRewardKind.Card,
            IsRareCardReward = rare,
            CardRewardActIndex = rare ? null : actIndex,
            CardHasBeenRevealed = revealed,
            CardCanReroll = canReroll,
            MaterializationStrategyId = ReplicatedCardStrategyId,
        };

    internal static CardReward RestorePersistedCardAssignment(
        int itemIndex,
        ApCardAssignmentState assignment,
        Player player,
        IReadOnlyList<CardModel> cards)
    {
        var restored = MirroredRewardAdapter.DecodeSavedCardAssignment(
            itemIndex, assignment, player.NetId);
        ApNativeCardReward reward = RestoreCardReward(
            restored.Origin, restored.Card.Configuration, player, cards);
        CardAssignmentRewards[(player.NetId, itemIndex)] = reward;
        return reward;
    }

    private static List<CardModel> DeserializeCards(CardRewardData card, Player player) =>
        card.Models
            .Select(serialized => player.RunState.LoadCard(
                Deserialize<SerializableCard>(serialized),
                player
            ))
            .ToList();

    private static async Task ObserveOwnerCompletionAsync(ApRewardMenuSpec menu, Task completion)
    {
        try
        {
            await completion;
            LogUtility.Debug($"Native AP reward menu {menu.MenuId} completed");
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Native AP reward menu {menu.MenuId} failed: {ex}");
            MultiplayerSupport.InvalidateRunClaims($"AP reward menu {menu.MenuId} failed");
        }
    }

    internal static bool CommitDiscreteReward(int itemIndex, ApMirroredRewardKind kind)
    {
        ArchipelagoClient.Progress.Items.MarkUsed(itemIndex);
        ApRunGraphRecorder.RecordItemClaim(itemIndex);

        switch (kind)
        {
            case ApMirroredRewardKind.Card:
                ArchipelagoClient.Progress.CardAssignments.Remove(itemIndex);
                break;
            case ApMirroredRewardKind.Relic:
                ArchipelagoClient.Progress.RelicChoiceAssignments.Remove(itemIndex);
                break;
        }

        Player? player = GameUtility.CurrentPlayer;
        if (player != null && ApRunData.PublishLocalProgress(player))
            return true;

        MultiplayerSupport.InvalidateRunClaims(
            $"AP {kind} receipt {itemIndex} applied but its progress could not reach the host"
        );
        return false;
    }

    private static int GetNativeOrder(ApMirroredRewardKind kind) => kind switch
    {
        ApMirroredRewardKind.Potion => 2,
        ApMirroredRewardKind.Relic or ApMirroredRewardKind.Ancient or ApMirroredRewardKind.Bonus => 3,
        ApMirroredRewardKind.Card => 5,
        _ => 99,
    };

    private static bool TryGetMirroredKind(
        IndexedItemInfo receipt,
        out ApMirroredRewardKind kind)
    {
        kind = default;
        if (ArchipelagoIdCodec.IsUniversalItemId(receipt.Item.ItemId))
        {
            kind = ApMirroredRewardKind.Bonus;
            return receipt.Item.GetUniversalItemId() == ItemTable.APItem.BonusWaxRelic;
        }
        switch (receipt.Item.GetCharacterItemType())
        {
            case ItemTable.APItem.CardReward:
            case ItemTable.APItem.RareCardReward:
                kind = ApMirroredRewardKind.Card;
                return true;
            case ItemTable.APItem.Relic:
                kind = ApMirroredRewardKind.Relic;
                return true;
            case ItemTable.APItem.Potion:
                kind = ApMirroredRewardKind.Potion;
                return true;
            case ItemTable.APItem.ProgressiveAncient:
                kind = ApMirroredRewardKind.Ancient;
                return true;
            default:
                return false;
        }
    }

    internal static string SerializeCard(CardModel card) => Serialize(card.ToSerializable());

    internal static string SerializeRelic(RelicModel relic) =>
        Serialize((relic.IsMutable ? relic : relic.ToMutable()).ToSerializable());

    private static string SerializePotion(PotionModel potion) =>
        Serialize((potion.IsMutable ? potion : potion.ToMutable()).ToSerializable(-1));

    private static RelicModel DeserializeRelic(string json) =>
        RelicModel.FromSerializable(Deserialize<SerializableRelic>(json));

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, SerializationUtility.CombinedOptions);

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, SerializationUtility.CombinedOptions)
        ?? throw new InvalidOperationException($"Could not deserialize AP model {typeof(T).Name}.");

    private static int _descriptionSequence;
    private const int RewardOriginFontSize = 16;

    private static LocString CreateApDescription(LocString primary, RewardOrigin spec) =>
        CreateApDescription(primary.GetFormattedText(), spec);

    private static LocString CreateApDescription(string primary, RewardOrigin spec)
    {
        string location = string.IsNullOrWhiteSpace(spec.FoundLocation)
            ? string.Empty
            : $" ({spec.FoundLocation})";
        string origin = string.IsNullOrWhiteSpace(spec.SenderName)
            ? string.Empty
            : $"\n[font_size={RewardOriginFontSize}]"
                + $"[blue]from {spec.SenderName}{location}[/blue][/font_size]";
        string key = $"AP_NATIVE_REWARD_{System.Threading.Interlocked.Increment(ref _descriptionSequence)}";
        TextUtility.RegisterLocString(key, primary + origin, "ap");
        return new LocString("ap", key);
    }

    internal interface IApNativeReward
    {
        bool CanClaim(out string reason);
        bool HasOriginText { get; }
        bool UseAncientStyle { get; }
    }

    private sealed class ApNativeGoldReward(ApGoldClaim claim, bool queued, Player player)
        : GoldReward(claim.GrantedAmount, player), IApNativeReward
    {
        public bool CanClaim(out string reason) => MultiplayerSupport.CanClaimGold(out reason);
        public bool HasOriginText => false;
        public bool UseAncientStyle => false;

        protected override async Task<bool> OnSelect()
        {
            // A queued claim's action applies the gold on every peer; this row waits for it here.
            if (queued)
                return await ApQueuedGoldClaims.SelectRow(claim, Player);

            // Known edge case: removing Poverty while this menu stays open leaves this offer underpaying.
            bool applied = await base.OnSelect();
            if (applied && LocalContext.IsMe(Player))
                ApGrantDispatcher.CommitGoldClaim(claim);
            return applied;
        }
    }

    private sealed class ApNativeRelicReward : RelicReward, IApNativeReward
    {
        private readonly int _itemIndex;
        private readonly ApMirroredRewardKind _kind;
        private readonly LocString _description;
        // Set when this row is a queued claim. A bonus relic's claim carries its relic; a standard
        // relic's claim and an Ancient pick read theirs from the receipt's recorded assignment.
        private readonly bool _queued;
        private readonly string? _serializedBonusRelic;
        // Which of an Ancient's three choices this row is.
        private readonly int _ancientOption;

        public override LocString Description => _description;

        public ApNativeRelicReward(
            RelicModel relic,
            Player player,
            RewardOrigin spec,
            ApMirroredRewardKind kind,
            bool queued = false,
            string? serializedBonusRelic = null,
            int ancientOption = -1)
            : base(relic, player)
        {
            _itemIndex = spec.ReceivedItemIndex;
            _kind = kind;
            _description = CreateApDescription(relic.Title, spec);
            _queued = queued;
            _serializedBonusRelic = serializedBonusRelic;
            _ancientOption = ancientOption;
            if (queued && kind == ApMirroredRewardKind.Bonus && serializedBonusRelic == null)
                throw new ArgumentException("A queued bonus relic row needs its relic.", nameof(serializedBonusRelic));
        }

        public bool CanClaim(out string reason) =>
            MultiplayerSupport.CanClaimReceivedReward(_kind, out reason);
        public bool HasOriginText => true;
        public bool UseAncientStyle => _kind == ApMirroredRewardKind.Ancient;

        protected override async Task<bool> OnSelect()
        {
            // A queued claim's action obtains the relic on every peer; this row waits for it here.
            if (_queued)
            {
                ApQueuedClaimResult result = _kind switch
                {
                    ApMirroredRewardKind.Bonus => await ApQueuedReceiptClaims.SelectBonusRelicRow(
                        _itemIndex, _serializedBonusRelic!, Player),
                    ApMirroredRewardKind.Ancient => await ApQueuedRelicClaims.SelectAncientRow(
                        _itemIndex, _ancientOption, Player),
                    _ => await ApQueuedRelicClaims.SelectRow(_itemIndex, Player),
                };
                // The reward screen finds the relic to animate by instance, as for potions below.
                ClaimedRelic = result.ClaimedModel as RelicModel;
                return result.Applied;
            }

            bool applied = await base.OnSelect();
            if (applied && LocalContext.IsMe(Player))
                CommitDiscreteReward(_itemIndex, _kind);
            return applied;
        }

        public override void OnSkipped() { }
    }

    private sealed class ApNativePotionReward : PotionReward, IApNativeReward
    {
        private readonly int _itemIndex;
        private readonly LocString _description;
        private readonly string _serializedPotion;
        private readonly bool _queued;

        public override LocString Description => _description;

        public ApNativePotionReward(
            PotionModel potion,
            Player player,
            RewardOrigin spec,
            string serializedPotion,
            bool queued)
            : base(potion, player)
        {
            _itemIndex = spec.ReceivedItemIndex;
            _description = CreateApDescription(potion.Title, spec);
            _serializedPotion = serializedPotion;
            _queued = queued;
        }

        public bool CanClaim(out string reason) =>
            MultiplayerSupport.CanClaimReceivedReward(ApMirroredRewardKind.Potion, out reason);
        public bool HasOriginText => true;
        public bool UseAncientStyle => false;

        protected override async Task<bool> OnSelect()
        {
            // A queued claim's action procures the potion on every peer; this row waits for it here.
            if (_queued)
            {
                ApQueuedClaimResult result = await ApQueuedReceiptClaims.SelectPotionRow(
                    _itemIndex, _serializedPotion, Player);
                // The reward screen animates the claimed potion from this row into the belt, and
                // looks it up there by instance: the action's, not this row's display copy.
                ClaimedPotion = result.ClaimedModel as PotionModel;
                return result.Applied;
            }

            bool applied = await base.OnSelect();
            if (applied && LocalContext.IsMe(Player))
                CommitDiscreteReward(_itemIndex, ApMirroredRewardKind.Potion);
            return applied;
        }

        public override void OnSkipped() { }
    }

    internal sealed class ApNativeCardReward : ApDeferredCardReward, IApNativeReward
    {
        private readonly int _itemIndex;
        private CardRewardConfiguration _configuration;
        private RewardOrigin _origin;
        private LocString _description;

        internal bool IsRare => _configuration.Recipe.IsRareReward;
        internal int? RewardActIndex => _configuration.Recipe.ActIndex;
        internal bool HasBeenRevealed => _configuration.HasBeenRevealed;
        internal string MaterializationStrategyId => _configuration.Policy.StrategyId;

        /// <summary>
        /// Set when this row is a queued claim (<see cref="ApQueuedCardClaims"/>). The row is then a
        /// stub without cards: its claim action generates or reloads the offer on every peer.
        /// </summary>
        internal bool IsQueuedClaim { get; set; }

        // A queued row's offer counts as revealed once its claim action recorded it.
        private bool IsRevealed => IsQueuedClaim
            ? ApQueuedCardClaims.GetRevealedOffer(Player, _itemIndex) != null
            : HasBeenRevealed;

        /// <summary>Set for each menu from its owner's Strict Card Rewards setting.</summary>
        internal bool RequiresCommitment { get; set; }

        /// <summary>
        /// Whether the picker should offer Discard. See <see cref="CanOfferDiscard"/>, and
        /// <see cref="StS2AP.Patches.Patches_APCardRewardDiscard"/> for the Pael's Wing caveat,
        /// where Sacrifice takes Discard's slot.
        /// </summary>
        internal bool OffersDiscard => RequiresCommitment && CanOfferDiscard;

        // Singleplayer only. A multiplayer card claim generates its offer on every peer, so the
        // chain would have to reach every peer before each reveal.
        private bool UsesStrictCardChain =>
            RequiresCommitment && !MultiplayerSupport.IsRealMultiplayerRun && LocalContext.IsMe(Player);

        // A reroll (Driftwood) takes the picker's second alternative slot, and the alternatives
        // must not change when a reroll regenerates them. A rerollable reward therefore offers no
        // Discard, and since it cannot be discarded it never locks the other card rewards.
        private bool CanOfferDiscard => !CanReroll && !_hasBeenRerolled;

        protected override string IconPath => IsRare
            ? ImageHelper.GetImagePath("ui/reward_screen/reward_icon_rare.png")
            : base.IconPath;

        public override LocString Description => _description;

        public ApNativeCardReward(
            IReadOnlyList<CardCreationResult> cards,
            Player player,
            CardCreationOptions options,
            RewardOrigin origin,
            CardRewardConfiguration configuration)
            : base(options, player)
        {
            _cards.AddRange(cards);
            _itemIndex = origin.ReceivedItemIndex;
            _configuration = configuration;
            _origin = origin;
            _description = new LocString("gameplay_ui", "COMBAT_REWARD_ADD_CARD");
            Configure(origin, configuration);
        }

        internal void Configure(RewardOrigin origin, CardRewardConfiguration configuration)
        {
            _configuration = configuration;
            _origin = origin;
            CanReroll = configuration.CanReroll;
            _description = CreateApDescription(
                new LocString("gameplay_ui", "COMBAT_REWARD_ADD_CARD"),
                origin
            );
        }

        public bool CanClaim(out string reason)
        {
            if (!MultiplayerSupport.CanClaimReceivedReward(ApMirroredRewardKind.Card, out reason))
                return false;
            if (!IsLockedByAnotherReveal())
                return true;
            reason = "Take a card from your revealed card reward or discard it first.";
            return false;
        }

        // Revealed offers stay in the local assignments until a card is taken or they are
        // discarded, including across menu closes and checkpoint loads. Already revealed offers
        // can always be reopened, so offers revealed before the setting was enabled cannot deadlock.
        internal bool IsLockedByAnotherReveal() =>
            RequiresCommitment
            && !IsRevealed
            && LocalContext.IsMe(Player)
            && (IsQueuedClaim
                ? ApQueuedCardClaims.HasOtherDiscardableOffer(Player, _itemIndex)
                : ArchipelagoClient.Progress.CardAssignments
                    .Any(entry => entry.Key != _itemIndex
                        && entry.Value is ApNativeCardReward { HasBeenRevealed: true } other
                        && other.CanOfferDiscard));

        public bool HasOriginText => true;
        public bool UseAncientStyle => false;

        protected override async Task PrepareCards()
        {
            bool hasAssignment = _cards.Count > 0;
            var run = Player.RunState;
            var spec = new ApMirroredRewardSpec
            {
                ApSlotId = _origin.ApSlotId,
                ReceivedItemIndex = _itemIndex,
                OwnerNetId = Player.NetId,
                Kind = ApMirroredRewardKind.Card,
                ItemName = _origin.ItemName,
                SenderName = _origin.SenderName,
                FoundLocation = _origin.FoundLocation,
                IsRareCardReward = IsRare,
                CardRewardActIndex = RewardActIndex,
                CardHasBeenRevealed = HasBeenRevealed,
                CardCanReroll = CanReroll,
                MaterializationStrategyId = MaterializationStrategyId,
                SerializedModels = Cards.Select(SerializeCard).ToList(),
            };
            try
            {
                bool strictChain = !hasAssignment && UsesStrictCardChain;
                List<CardCreationResult> preparedCards = hasAssignment
                    ? RefreshCardChoices(this)
                    : await GenerateCardChoices(spec, Player, strictChain);
                if (RunManager.Instance.DebugOnlyGetState() != run)
                    throw new OperationCanceledException("Run changed during AP card reveal.");
                if (strictChain)
                    ArchipelagoClient.Progress.StrictCardRevealCount++;
                spec.CardHasBeenRevealed = true;
                spec.SerializedModels = preparedCards.Select(result => SerializeCard(result.Card)).ToList();
                _cards.Clear();
                _cards.AddRange(preparedCards);

                Configure(_origin, MirroredRewardAdapter.CardConfiguration(spec));
                PublishRevealedAssignment();
                string offer = string.Join(", ", preparedCards.Select(
                    result => $"{result.Card.Id}+{result.Card.CurrentUpgradeLevel}"));
                LogUtility.Info($"Prepared AP card offer {_origin.ReceiptIdentity} for player {Player.NetId} "
                    + $"(firstReveal={!hasAssignment}, cards=[{offer}])");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"AP card reveal {_origin.ReceiptIdentity} for player {Player.NetId} failed: {ex}");
                throw;
            }
        }

        private void PublishRevealedAssignment()
        {
            CardAssignmentRewards[(Player.NetId, _itemIndex)] = this;
            if (!LocalContext.IsMe(Player))
                return;
            ArchipelagoClient.Progress.CardAssignments[_itemIndex] = this;
            if (!ApRunData.PublishLocalProgress(Player))
            {
                MultiplayerSupport.InvalidateRunClaims("AP card reveal could not be published; reload the campaign.");
                throw new InvalidOperationException($"Could not publish revealed AP card receipt {_itemIndex}.");
            }
        }

        protected override async Task<bool> OnSelect()
        {
            try
            {
                return IsQueuedClaim
                    ? await SelectQueued()
                    : await SelectAndCommit();
            }
            finally
            {
                // A reveal locks the other card rows and a taken or discarded card unlocks them.
                if (LocalContext.IsMe(Player))
                    ArchipelagoRewardUI.RefreshCardRewardLocks();
            }
        }

        // The claim action shows the picker and applies the pick on every peer; this row waits
        // for it here.
        private async Task<bool> SelectQueued() =>
            (await ApQueuedCardClaims.SelectRow(
                _itemIndex, IsRare, RewardActIndex, RequiresCommitment, Player)).Applied;

        private async Task<bool> SelectAndCommit()
        {
            HashSet<CardModel>? deckBefore = LocalContext.IsMe(Player)
                ? Player.Deck.Cards.ToHashSet()
                : null;
            bool applied = await base.OnSelect();
            if (!applied)
            {
                if (LocalContext.IsMe(Player)
                    && !ApRunData.PublishLocalProgress(Player))
                {
                    MultiplayerSupport.InvalidateRunClaims(
                        $"AP card receipt {_itemIndex} was revealed but its progress "
                            + "could not reach the host"
                    );
                }
                return applied;
            }
            CardAssignmentRewards.Remove((Player.NetId, _itemIndex));
            if (!LocalContext.IsMe(Player))
                return true;

            List<CardModel> taken = Player.Deck.Cards
                .Where(card => deckBefore != null && !deckBefore.Contains(card))
                .ToList();
            foreach (CardModel selected in taken)
                await GameUtility.AddCardRewardToCombatDrawPile(selected, Player);
            // Only rewards resolved while Strict is on extend the chain. The native pick has
            // already removed the taken card, so Cards holds the rest of the offer.
            if (UsesStrictCardChain)
            {
                ArchipelagoClient.Progress.StrictCardPickHistory.Add(StrictCardChain.PickEntry(
                    taken.Select(card => (card.Id.ToString(), card.CurrentUpgradeLevel)),
                    Cards.Select(card => (card.Id.ToString(), card.CurrentUpgradeLevel))));
            }
            CommitDiscreteReward(_itemIndex, ApMirroredRewardKind.Card);
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
            string itemName,
            string reason,
            Player player,
            RewardOrigin spec)
            : base(player)
        {
            _description = CreateApDescription(itemName, spec);
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
