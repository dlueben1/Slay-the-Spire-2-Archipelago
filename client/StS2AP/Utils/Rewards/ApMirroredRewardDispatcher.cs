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
using MegaCrit.Sts2.Core.Multiplayer.Transport;
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
using STS2RitsuLib.Networking.Sidecar;

namespace StS2AP.Utils;

/// <summary>
/// Builds one immutable native AP reward-menu snapshot. In multiplayer the owner publishes the
/// complete recipe before any native RewardsSet begins, so every replica has matching reward
/// indexes and MegaCrit can own the entire selection lifecycle.
/// </summary>
public static class ApMirroredRewardDispatcher
{
    private const string SidecarMessageKey = "received_reward_menu";
    private const string ReplicatedCardStrategyId = "ap_rng_replicated_card_v1";
    private const string OwnerFinalApRngStrategyId = "ap_rng_owner_final_v1";

    private static readonly RitsuLibSidecarJsonSerializer<ApRewardMenuSpec> MenuSerializer = new();
    private static readonly RitsuLibSidecarSyncMessageDescriptor<ApRewardMenuSpec> MenuDescriptor =
        new(
            ModEntry.ModId,
            SidecarMessageKey,
            MenuSerializer.Serialize,
            MenuSerializer.Deserialize,
            HandleMenuSpec,
            LocationTargeted: true,
            ShouldBuffer: true,
            Mode: NetTransferMode.Reliable,
            FailurePolicy: RitsuLibSidecarSyncFailurePolicy.Required,
            BroadcastScope: RitsuLibSidecarSyncBroadcastScope.ReadyPeers,
            DispatchLocalOnBroadcast: false,
            LogLevel: LogLevel.Debug,
            ShouldBroadcast: true
        );
    private static readonly HashSet<(ulong OwnerNetId, Guid MenuId)> ActiveRemoteMenus = new();
    private static readonly Dictionary<(ulong OwnerNetId, int ItemIndex), ApNativeCardReward>
        ReplicaCardAssignments = new();
    private static readonly Dictionary<(ulong OwnerNetId, int ItemIndex), PotionModel>
        ReplicaPotionAssignments = new();
    private static string? _activeRunIdentity;

    public static void Initialize()
    {
        RitsuLibSidecarSyncMessages.Register(MenuDescriptor);
    }

    /// <summary>Binds menu assignments and receipt consumption to the current native run.</summary>
    public static bool BeginRun(RunState runState, out string reason)
    {
        reason = string.Empty;
        EndRun();

        if (!ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
            || shared.RunId == Guid.Empty)
        {
            reason = "The host-owned AP run identity was missing when rewards were bound.";
            return false;
        }

        _activeRunIdentity = shared.RunId.ToString("N");
        try
        {
            RestoreReplicaAssignments(runState);
        }
        catch (Exception ex)
        {
            reason = $"Could not restore pending AP native rewards: {ex.GetBaseException().Message}";
            EndRun();
            return false;
        }
        LogUtility.Info($"Bound native AP reward menus to run {_activeRunIdentity}");
        return true;
    }

    private static void RestoreReplicaAssignments(RunState runState)
    {
        foreach (Player player in runState.Players.OrderBy(candidate => candidate.NetId))
        {
            if (LocalContext.IsMe(player))
            {
                foreach ((int itemIndex, CardReward reward) in
                         ArchipelagoClient.Progress.CardAssignments)
                {
                    if (reward is ApNativeCardReward native)
                        ReplicaCardAssignments[(player.NetId, itemIndex)] = native;
                }
                foreach ((int itemIndex, PotionModel potion) in
                         ArchipelagoClient.Progress.PotionAssignments)
                {
                    ReplicaPotionAssignments[(player.NetId, itemIndex)] = potion;
                }
                continue;
            }

            if (!ApRunData.TryGetPlayerState(runState, player.NetId, out ApPlayerRunState state)
                || !state.Progress.Initialized)
            {
                continue;
            }

            foreach ((int itemIndex, ApCardAssignmentState assignment) in
                     state.Progress.CardAssignments.OrderBy(entry => entry.Key))
            {
                List<CardModel> cards = assignment.SerializedCards
                    .Select(serialized => runState.LoadCard(
                        Deserialize<SerializableCard>(serialized),
                        player
                    ))
                    .ToList();
                RestorePersistedCardAssignment(itemIndex, assignment, player, cards);
            }
            foreach ((int itemIndex, string serialized) in
                     state.Progress.PotionAssignments.OrderBy(entry => entry.Key))
            {
                ReplicaPotionAssignments[(player.NetId, itemIndex)] = PotionModel.FromSerializable(
                    Deserialize<SerializablePotion>(serialized)
                );
            }
        }
    }

    public static void EndRun()
    {
        _activeRunIdentity = null;
        Patches_APRewardSelectionOrder.Reset();
        ActiveRemoteMenus.Clear();
        ReplicaCardAssignments.Clear();
        ReplicaPotionAssignments.Clear();
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
        bool ownerMaterializationStarted = false;
        try
        {
            var approvedRelics = await RelicReceiptMultiplayer.ApproveMenu(
                player, RelicRewardUtility.GetMenuReservationCandidates(player));
            if (apLifecycleVersion != ArchipelagoRewardUI.ApLifecycleVersion
                || (MultiplayerSupport.IsRealMultiplayerRun
                    && !ArchipelagoRewardUI.CanBuildMenuAfterAwait(apLifecycleVersion)))
                return false;
            ownerMaterializationStarted = true;
            spec = BuildOwnerMenuSpec(player, runState, approvedRelics);
            rewards = DecodeRewards(spec);
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Could not build native AP reward menu: {ex}");
            if (ownerMaterializationStarted && MultiplayerSupport.IsRealMultiplayerRun)
            {
                MultiplayerSupport.InvalidateRunClaims(
                    "AP reward owner materialization failed"
                );
            }
            NotificationUtility.ShowRawText("Could not prepare AP rewards. Try opening the menu again.");
            return false;
        }

        if (MultiplayerSupport.IsRealMultiplayerRun)
        {
            INetGameService netService = RunManager.Instance.NetService;
            bool sent = netService.Type == NetGameType.Host
                ? RitsuLibSidecarSyncMessages.Broadcast(netService, MenuDescriptor, spec)
                : RitsuLibSidecarSyncMessages.SendToHostAndBroadcast(netService, MenuDescriptor, spec);
            if (!sent)
            {
                LogUtility.Error($"Could not publish AP reward menu {spec.MenuId} to every peer");
                MultiplayerSupport.InvalidateRunClaims(
                    $"AP reward menu {spec.MenuId} could not reach every replica"
                );
                NotificationUtility.ShowRawText("Could not synchronize the AP reward menu.");
                return false;
            }

            if (apLifecycleVersion != ArchipelagoRewardUI.ApLifecycleVersion
                || !ArchipelagoRewardUI.CanBuildMenuAfterAwait(apLifecycleVersion))
            {
                return false;
            }
        }

        RewardsSet set = BuildRewardsSet(spec.Gold, rewards, player);
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

    private static ApRewardMenuSpec BuildOwnerMenuSpec(
        Player player,
        RunState runState,
        IReadOnlySet<int>? approvedRelics)
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
        };

        RelicRewardUtility.ReconcileBankedRewards(player, approvedRelics);

        ApGoldClaim? gold = ApGrantDispatcher.MaterializeGoldClaim();
        if (gold != null)
        {
            menu.Gold = new ApMenuGoldSpec
            {
                SourceAmount = gold.SourceAmount,
                GrantedAmount = gold.GrantedAmount,
                RedeemedRawAfter = gold.RedeemedRawAfter,
            };
        }

        IEnumerable<IndexedItemInfo> receipts = ArchipelagoClient.Progress.AllReceivedItems
            .Concat(MultiplayerSupport.PendingUnsupportedItems)
            .GroupBy(receipt => receipt.Index)
            .Select(group => group.First())
            .OrderBy(receipt => receipt.Index);

        foreach (IndexedItemInfo receipt in receipts)
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

                menu.Rewards.Add(BuildAssignedSpec(receipt, player, apSlotId, kind));
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

    private static ApMirroredRewardSpec BuildAssignedSpec(
        IndexedItemInfo receipt,
        Player player,
        int apSlotId,
        ApMirroredRewardKind kind)
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

                ReplicaCardAssignments[(player.NetId, itemIndex)] = reward;
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

                ReplicaPotionAssignments[(player.NetId, itemIndex)] = potion!;
                spec.SerializedModels.Add(SerializePotion(potion!));
                break;
            }
            case ApMirroredRewardKind.Ancient:
            {
                string? choiceKey = MultiplayerSupport.IsRealMultiplayerRun
                    ? $"{player.NetId}:{itemIndex}"
                    : null;
                IReadOnlyList<RelicModel> choices =
                    ArchipelagoClient.Progress.GetOrAssignAncientRelicChoices(
                        itemIndex,
                        player,
                        choiceKey
                    );
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
        ApMenuGoldSpec? gold, IReadOnlyList<MirroredReward> assignments, Player owner)
    {
        var rewards = new List<Reward>();
        if (gold != null)
            rewards.Add(new ApNativeGoldReward(gold.ToClaim(), owner));

        foreach (MirroredReward spec in assignments)
            rewards.Add(BuildNativeReward(spec, owner));

        return new RewardsSet(owner).WithCustomRewards(rewards);
    }

    private static Reward BuildNativeReward(MirroredReward spec, Player owner) => spec.Match<Reward>(
        card => BuildCardReward(spec.Origin, card, owner),
        potion => new ApNativePotionReward(GetReplicaPotionAssignment(spec.Origin, potion, owner), owner, spec.Origin),
        relic => BuildStandardRelicReward(spec.Origin, relic, owner),
        choices => BuildAncientReward(spec.Origin, choices, owner),
        reason => new ApUnavailableReward(spec.Origin.ItemName, reason, owner, spec.Origin),
        relic => new ApNativeRelicReward(DeserializeRelic(relic), owner, spec.Origin, ApMirroredRewardKind.Bonus));

    private static Reward BuildCardReward(RewardOrigin origin, CardRewardData card, Player player)
    {
        if (!ReplicaCardAssignments.TryGetValue(
                (player.NetId, origin.ReceivedItemIndex),
                out ApNativeCardReward? reward))
        {
            reward = RestoreCardReward(
                origin,
                card.Configuration,
                player,
                DeserializeCards(card, player)
            );
            ReplicaCardAssignments[(player.NetId, origin.ReceivedItemIndex)] = reward;
        }

        ValidateSerializedModels(origin, card.Models, reward.Cards.Select(SerializeCard), "card assignment");
        reward.Configure(origin, card.Configuration);
        return reward;
    }

    private static PotionModel GetReplicaPotionAssignment(
        RewardOrigin origin,
        PotionRewardData assignment,
        Player player)
    {
        if (!ReplicaPotionAssignments.TryGetValue(
                (player.NetId, origin.ReceivedItemIndex),
                out PotionModel? potion))
        {
            potion = PotionModel.FromSerializable(
                Deserialize<SerializablePotion>(assignment.Model)
            );
            ReplicaPotionAssignments[(player.NetId, origin.ReceivedItemIndex)] = potion;
        }

        // PotionFactory returns canonical pool entries, but PotionReward owns and may mutate the
        // offered potion. Normalize older in-memory/save-restored assignments here as well as at
        // their creation sites so an existing pending receipt can recover without being rerolled.
        if (!potion.IsMutable)
        {
            potion = potion.ToMutable();
            ReplicaPotionAssignments[(player.NetId, origin.ReceivedItemIndex)] = potion;
            if (LocalContext.IsMe(player)
                && ArchipelagoClient.Progress.PotionAssignments.ContainsKey(
                    origin.ReceivedItemIndex
                ))
            {
                ArchipelagoClient.Progress.PotionAssignments[origin.ReceivedItemIndex] = potion;
            }
        }

        ValidateSerializedModels(origin, new[] { assignment.Model }, new[] { SerializePotion(potion) }, "potion assignment");
        return potion;
    }

    private static Reward BuildAncientReward(RewardOrigin origin, IReadOnlyList<string> models, Player player)
    {
        var children = models
            .Select(serialized =>
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
                    ApMirroredRewardKind.Ancient
                );
            })
            .ToList();
        return LinkedRewardSets.Create(children, player, LinkedRewardSelectionMode.ChooseOne);
    }

    private static Reward BuildStandardRelicReward(
        RewardOrigin origin,
        string model,
        Player player)
    {
        RelicModel relic = DeserializeRelic(model);
        RelicReceiptMultiplayer.RecordMenuAssignment(player, origin.ReceivedItemIndex, model);
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
        return BetaMainCompatibility.WithCombatRewardCompatibility(
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
        string domain)
    {
        string seedMaterial = string.Join(
            "|",
            "sts2ap-reward-rng-v1",
            domain,
            player.RunState.Rng.StringSeed,
            spec.ApSlotId,
            player.RunState.GetPlayerSlotIndex(player),
            player.GetAPCharacterNumber(),
            spec.ReceivedItemIndex
        );
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(seedMaterial));
        // The public build accepts a 32-bit seed while the beta build widens the same value.
        // Using the shared 32-bit seed keeps materialization deterministic across both variants.
        return new Rng(BinaryPrimitives.ReadUInt32LittleEndian(digest));
    }

    private static async Task<List<CardCreationResult>> GenerateCardChoices(
        ApMirroredRewardSpec spec,
        Player player)
    {
        if (player.RunState is not RunState runState)
            throw new InvalidOperationException("AP reward generation requires a native run state.");
        CardRewardConfiguration configuration = MirroredRewardAdapter.CardConfiguration(spec);
        Rng rng = CreateApRewardRng(spec, player, "card");
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
        ReplicaCardAssignments[(player.NetId, itemIndex)] = reward;
        return reward;
    }

    private static List<CardModel> DeserializeCards(CardRewardData card, Player player) =>
        card.Models
            .Select(serialized => player.RunState.LoadCard(
                Deserialize<SerializableCard>(serialized),
                player
            ))
            .ToList();

    private static void ValidateSerializedModels(
        RewardOrigin origin,
        IReadOnlyList<string> expectedModels,
        IEnumerable<string> actualModels,
        string description)
    {
        string[] actual = actualModels.ToArray();
        if (!expectedModels.SequenceEqual(actual, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"AP {description} {origin.ReceiptIdentity} differed after native materialization "
                    + $"(expectedHash={StableHash(string.Join("\n", expectedModels))}, "
                    + $"actualHash={StableHash(string.Join("\n", actual))})."
            );
        }
    }

    private static string StableHash(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)
        ))[..16];

    private static Task HandleMenuSpec(
        RitsuLibSidecarSyncMessageContext<ApRewardMenuSpec> context)
    {
        ApRewardMenuSpec menu = context.Message;
        if (context.SenderNetId != menu.OwnerNetId)
            throw new InvalidOperationException("Invalid AP reward-menu owner.");

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        bool posted = RitsuLibSidecarGodotMainLoopScheduling.TryPostToMainLoop(() =>
        {
            _ = CompleteRemoteMenuAsync(menu, completion);
        });
        if (!posted)
        {
            completion.SetException(
                new InvalidOperationException("Godot main loop was unavailable for AP reward menu.")
            );
        }
        return completion.Task;
    }

    private static void ValidateMenuOnHost(ApRewardMenuSpec menu, IReadOnlyList<MirroredReward> rewards)
    {
        if (!TryGetCurrentMenuOwner(menu, out RunState runState, out ApPlayerRunState ownerState))
            throw new InvalidOperationException("AP reward menu did not match the active run owner.");
        if (ownerState.Participation == ApParticipationKind.OwnApSlot && ownerState.ApSlotId != menu.ApSlotId)
            throw new InvalidOperationException("AP reward menu did not match its owner's slot.");

        // Authentication and live receipt reservations are integration facts, not pure domain rules.
        foreach (MirroredReward reward in rewards)
        {
            RewardOrigin origin = reward.Origin;
            if (ApRunData.IsReceiptUsed(runState, menu.OwnerNetId, origin.ReceivedItemIndex))
                throw new InvalidOperationException($"AP receipt {origin.ReceiptIdentity} was already consumed.");
            if (reward.IsRelic && !RelicReceiptMultiplayer.State(runState).CanUseMenu(
                    menu.OwnerNetId, origin.ReceivedItemIndex))
                throw new InvalidOperationException($"AP relic {origin.ReceiptIdentity} has no host menu reservation.");
        }
    }

    private static bool TryGetCurrentMenuOwner(
        ApRewardMenuSpec menu,
        out RunState runState,
        out ApPlayerRunState ownerState)
    {
        runState = null!;
        ownerState = null!;
        if (RunManager.Instance.DebugOnlyGetState() is not RunState current
            || !ApRunData.TryGetSharedState(current, out ApRunSharedState shared)
            || shared.RunId != menu.RunId
            || !ApRunData.TryGetPlayerState(current, menu.OwnerNetId, out ownerState)
            || ownerState.Participation == ApParticipationKind.VanillaGuest)
        {
            return false;
        }
        runState = current;
        return true;
    }

    // changed to async Task as async void has lots of issues
    private static async Task CompleteRemoteMenuAsync(
        ApRewardMenuSpec menu,
        TaskCompletionSource sidecarCompletion)
    {
        var key = (menu.OwnerNetId, menu.MenuId);
        try
        {
            if (!ActiveRemoteMenus.Add(key))
                throw new InvalidOperationException($"AP reward menu {menu.MenuId} is already active.");
            if (!TryGetCurrentMenuOwner(menu, out RunState runState, out _))
                throw new InvalidOperationException("No matching player exists for the AP reward menu.");
            Player owner = runState.GetPlayer(menu.OwnerNetId)
                ?? throw new InvalidOperationException($"Player {menu.OwnerNetId} is not in the run.");
            // A quick close/reopen can deliver the next menu while this replica still finishes
            // the preceding selection. Install its final assignment before validating the new menu.
            await Patches_APRewardSelectionOrder.WhenIdle(owner);
            if (RunManager.Instance.DebugOnlyGetState() != runState)
                throw new OperationCanceledException("Run changed while waiting for the preceding AP reward.");
            IReadOnlyList<MirroredReward> rewards = DecodeRewards(menu);
            if (RunManager.Instance.NetService.Type == NetGameType.Host)
                ValidateMenuOnHost(menu, rewards);
            await RelicReceiptMultiplayer.WaitForMenuReservations(owner,
                rewards.Where(r => r.IsRelic).Select(r => r.Origin.ReceivedItemIndex));
            RewardsSet set = BuildRewardsSet(menu.Gold, rewards, owner);
            await RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(set);
            sidecarCompletion.TrySetResult();
        }
        catch (Exception ex)
        {
            sidecarCompletion.TrySetException(ex);
            if (MultiplayerSupport.IsRealMultiplayerRun && TryGetCurrentMenuOwner(menu, out _, out _))
                MultiplayerSupport.InvalidateRunClaims($"remote AP reward menu {menu.MenuId} failed");
        }
        finally
        {
            ActiveRemoteMenus.Remove(key);
        }
    }

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

    private static string SerializeCard(CardModel card) => Serialize(card.ToSerializable());

    private static string SerializeRelic(RelicModel relic) =>
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

    private sealed class ApNativeGoldReward(ApGoldClaim claim, Player player)
        : GoldReward(claim.GrantedAmount, player), IApNativeReward
    {
        public bool CanClaim(out string reason) => MultiplayerSupport.CanClaimGold(out reason);
        public bool HasOriginText => false;
        public bool UseAncientStyle => false;

        protected override async Task<bool> OnSelect()
        {
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

        public override LocString Description => _description;

        public ApNativeRelicReward(
            RelicModel relic,
            Player player,
            RewardOrigin spec,
            ApMirroredRewardKind kind)
            : base(relic, player)
        {
            _itemIndex = spec.ReceivedItemIndex;
            _kind = kind;
            _description = CreateApDescription(relic.Title, spec);
        }

        public bool CanClaim(out string reason) =>
            MultiplayerSupport.CanClaimReceivedReward(_kind, out reason);
        public bool HasOriginText => true;
        public bool UseAncientStyle => _kind == ApMirroredRewardKind.Ancient;

        protected override async Task<bool> OnSelect()
        {
            if (_kind == ApMirroredRewardKind.Relic && !RelicReceiptMultiplayer.CanUseMenu(Player, _itemIndex))
                throw new InvalidOperationException($"AP relic receipt {Player.NetId}:{_itemIndex} is not approved for this menu.");
            bool applied = await base.OnSelect();
            if (applied && _kind == ApMirroredRewardKind.Relic)
                RelicReceiptMultiplayer.ConsumeMenu(Player, _itemIndex);
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

        public override LocString Description => _description;

        public ApNativePotionReward(
            PotionModel potion,
            Player player,
            RewardOrigin spec)
            : base(potion, player)
        {
            _itemIndex = spec.ReceivedItemIndex;
            _description = CreateApDescription(potion.Title, spec);
        }

        public bool CanClaim(out string reason) =>
            MultiplayerSupport.CanClaimReceivedReward(ApMirroredRewardKind.Potion, out reason);
        public bool HasOriginText => true;
        public bool UseAncientStyle => false;

        protected override async Task<bool> OnSelect()
        {
            bool applied = await base.OnSelect();
            if (applied)
                ReplicaPotionAssignments.Remove((Player.NetId, _itemIndex));
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
        private uint? _firstPickerChoice;
        private LocString _description;

        internal bool IsRare => _configuration.Recipe.IsRareReward;
        internal int? RewardActIndex => _configuration.Recipe.ActIndex;
        internal bool HasBeenRevealed => _configuration.HasBeenRevealed;
        internal string MaterializationStrategyId => _configuration.Policy.StrategyId;

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

        public bool CanClaim(out string reason) =>
            MultiplayerSupport.CanClaimReceivedReward(ApMirroredRewardKind.Card, out reason);
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
                var synchronizer = RunManager.Instance.PlayerChoiceSynchronizer;
                // Reserve verification and picker IDs before generation can await native callbacks.
                bool multiplayer = MultiplayerSupport.IsRealMultiplayerRun;
                uint verificationChoice = multiplayer ? synchronizer.ReserveChoiceId(Player) : 0;
                _firstPickerChoice = multiplayer ? synchronizer.ReserveChoiceId(Player) : null;
                List<CardCreationResult> preparedCards = hasAssignment
                    ? RefreshCardChoices(this)
                    : await GenerateCardChoices(spec, Player);
                if (RunManager.Instance.DebugOnlyGetState() != run)
                    throw new OperationCanceledException("Run changed during AP card reveal.");
                spec.CardHasBeenRevealed = true;
                spec.SerializedModels = preparedCards.Select(result => SerializeCard(result.Card)).ToList();
                if (multiplayer)
                {
                    // Check that picker indexes mean the same offered cards on every replica.
                    // General gameplay-state divergence remains the native checksum system's job.
                    List<int> verification = ApCardRevealCodec.Encode(spec, firstReveal: !hasAssignment);
                    if (LocalContext.IsMe(Player))
                    {
                        synchronizer.SyncLocalChoice(Player, verificationChoice,
                            PlayerChoiceResult.FromIndexes(verification));
                    }
                    else
                    {
                        PlayerChoiceResult ownerVerification = await synchronizer
                            .WaitForRemoteChoice(Player, verificationChoice)
                            .WaitAsync(TimeSpan.FromSeconds(15));
                        if (RunManager.Instance.DebugOnlyGetState() != run)
                            throw new OperationCanceledException("Run changed during AP card verification.");
                        ApCardRevealCodec.Verify(verification, ownerVerification.AsIndexes(), spec.GrantId.ToString());
                    }
                }
                _cards.Clear();
                _cards.AddRange(preparedCards);

                Configure(_origin, MirroredRewardAdapter.CardConfiguration(spec));
                PublishRevealedAssignment();
                LogUtility.Info($"Prepared replicated AP card offer {_origin.ReceiptIdentity} for player {Player.NetId} "
                    + $"(firstReveal={!hasAssignment}, localOwner={LocalContext.IsMe(Player)})");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"AP card reveal {_origin.ReceiptIdentity} for player {Player.NetId} failed: {ex}");
                if (MultiplayerSupport.IsRealMultiplayerRun)
                    MultiplayerSupport.InvalidateRunClaims("AP card reveal failed; reload the campaign.");
                throw;
            }
        }

        private void PublishRevealedAssignment()
        {
            ReplicaCardAssignments[(Player.NetId, _itemIndex)] = this;
            if (!LocalContext.IsMe(Player))
                return;
            ArchipelagoClient.Progress.CardAssignments[_itemIndex] = this;
            if (!ApRunData.PublishLocalProgress(Player))
            {
                MultiplayerSupport.InvalidateRunClaims("AP card reveal could not be published; reload the campaign.");
                throw new InvalidOperationException($"Could not publish revealed AP card receipt {_itemIndex}.");
            }
        }

        protected override Task<bool> SelectCards()
        {
            uint? firstChoice = _firstPickerChoice;
            _firstPickerChoice = null;
            return firstChoice.HasValue
                ? Patches_APCardRevealChoice.Select(RunManager.Instance.PlayerChoiceSynchronizer,
                    Player, firstChoice.Value, base.SelectCards)
                : base.SelectCards();
        }

        protected override async Task<bool> OnSelect()
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
            ReplicaCardAssignments.Remove((Player.NetId, _itemIndex));
            if (!LocalContext.IsMe(Player))
                return true;

            foreach (CardModel selected in Player.Deck.Cards
                         .Where(card => deckBefore != null && !deckBefore.Contains(card)))
            {
                await GameUtility.AddCardRewardToCombatDrawPile(selected, Player);
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
