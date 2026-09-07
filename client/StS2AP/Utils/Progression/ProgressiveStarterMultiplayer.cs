using System.Text.Json;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using StS2AP.Extensions;
using StS2AP.Domain;
using StS2AP.DomainAdapters;
using STS2RitsuLib.Networking.ManagedActions;

namespace StS2AP.Utils;

/// <summary>
/// Synchronizes progressive starter transitions through MegaCrit's native action queue. The AP
/// receipt owner authors concrete model recipes; every replica executes those recipes only in a
/// non-combat action slot, with requests deferred until local execution is idle.
/// </summary>
public static class ProgressiveStarterMultiplayer
{
    private sealed record ValidatedTarget(
        ulong PlayerNetId,
        StarterKind Kind,
        StarterPlan<CapturedStarterRecipe> Plan);

    private const int SchemaVersion = 1;
    private const string ActionKey = "progressive_starter_v1";

    private static readonly RitsuLibManagedNetActionDescriptor<ApProgressiveStarterActionMessage>
        ActionDescriptor = new(
            ModuleId: ModEntry.ModId,
            ActionKey: ActionKey,
            Serialize: static message => JsonSerializer.SerializeToUtf8Bytes(message),
            Deserialize: DeserializeMessage,
            Execute: ExecuteAction,
            ActionType: GameActionType.NonCombat
        );
    private static readonly Dictionary<
        (Guid RunId, ulong PlayerNetId, ApProgressiveStarterActionMessage.StarterKind Kind),
        StarterState<CapturedStarterRecipe>
    > PendingSpecifications = new();
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
            return;

        RitsuLibManagedNetActions.Register(ActionDescriptor);
        _initialized = true;
    }

    public static void EndRun() => PendingSpecifications.Clear();

    /// <summary>
    /// Enqueues the local player's initialization projection from this process's AP connection.
    /// </summary>
    public static void BeginRun(RunState runState, Player localPlayer)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.IsLocalOwnApSlot
            || !MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.ProgressiveStarters)
            || !TryGetActionIdentity(runState, localPlayer, out Guid runId, out int apSlotId))
        {
            return;
        }

        try
        {
            var targets = new List<ApProgressiveStarterActionMessage.Target>();
            AddInitializationTargets(localPlayer, targets);

            if (targets.Count == 0)
                return;

            var message = new ApProgressiveStarterActionMessage
            {
                RunId = runId,
                ActionId = Guid.NewGuid(),
                OwnerNetId = localPlayer.NetId,
                ApSlotId = apSlotId,
                Reason = ApProgressiveStarterActionMessage.ActionReason.Initialization,
                Targets = targets
                    .OrderBy(target => target.PlayerNetId)
                    .ThenBy(target => target.Kind)
                    .ToList(),
            };

            Request(message, localPlayer, "initialization");
        }
        catch (Exception ex)
        {
            FailCapture("initialization", ex);
        }
    }

    /// <summary>
    /// Converts exactly one live AP receipt into one ordered action. Receipts for characters not
    /// active under this writer remain banked and are projected by a future initialization.
    /// </summary>
    public static void ReceiveLiveReceipt(
        int receivedItemIndex,
        long characterOffset,
        ApProgressiveStarterActionMessage.StarterKind kind,
        int receivedCount)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.IsLocalOwnApSlot
            || !MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.ProgressiveStarters)
            || receivedCount is < 1 or > 2
            || GameUtility.CurrentPlayer is not Player localPlayer
            || localPlayer.RunState is not RunState runState
            || !TryGetActionIdentity(runState, localPlayer, out Guid runId, out int apSlotId))
        {
            return;
        }

        try
        {
            if (localPlayer.GetAPCharacterNumber() != characterOffset || !IsEnabledFor(localPlayer, kind))
                return;

            var specification = GetOrCaptureSpecification(localPlayer, kind);
            var targets = new List<ApProgressiveStarterActionMessage.Target>
            {
                new()
                {
                    PlayerNetId = localPlayer.NetId,
                    Kind = kind,
                    TargetTier = ProgressiveStarterAdapter.ReceivedTarget(specification, receivedCount),
                    Specification = ProgressiveStarterAdapter.Encode(specification),
                },
            };

            var message = new ApProgressiveStarterActionMessage
            {
                RunId = runId,
                ActionId = Guid.NewGuid(),
                OwnerNetId = localPlayer.NetId,
                ApSlotId = apSlotId,
                ReceivedItemIndex = receivedItemIndex,
                CharacterOffset = characterOffset,
                Reason = ApProgressiveStarterActionMessage.ActionReason.LiveReceipt,
                Targets = targets,
            };

            Request(message, localPlayer, $"receipt {receivedItemIndex}");
        }
        catch (Exception ex)
        {
            FailCapture($"receipt {receivedItemIndex}", ex);
        }
    }

    private static void AddInitializationTargets(
        Player player,
        ICollection<ApProgressiveStarterActionMessage.Target> targets)
    {
        long? offset = player.GetAPCharacterNumber();
        if (!offset.HasValue)
            return;

        if (IsEnabledFor(player, ApProgressiveStarterActionMessage.StarterKind.Card))
        {
            var specification = GetOrCaptureSpecification(
                player,
                ApProgressiveStarterActionMessage.StarterKind.Card
            );
            targets.Add(new ApProgressiveStarterActionMessage.Target
            {
                PlayerNetId = player.NetId,
                Kind = ApProgressiveStarterActionMessage.StarterKind.Card,
                TargetTier = ProgressiveStarterAdapter.ReceivedTarget(specification,
                    ArchipelagoClient.Progress.ProgressiveStarterCards.GetValueOrDefault(offset.Value)),
                Specification = ProgressiveStarterAdapter.Encode(specification),
            });
        }

        if (IsEnabledFor(player, ApProgressiveStarterActionMessage.StarterKind.Relic))
        {
            var specification = GetOrCaptureSpecification(
                player,
                ApProgressiveStarterActionMessage.StarterKind.Relic
            );
            targets.Add(new ApProgressiveStarterActionMessage.Target
            {
                PlayerNetId = player.NetId,
                Kind = ApProgressiveStarterActionMessage.StarterKind.Relic,
                TargetTier = ProgressiveStarterAdapter.ReceivedTarget(specification,
                    ArchipelagoClient.Progress.ProgressiveStarterRelics.GetValueOrDefault(offset.Value)),
                Specification = ProgressiveStarterAdapter.Encode(specification),
            });
        }
    }

    private static bool TryGetActionIdentity(
        RunState runState,
        Player owner,
        out Guid runId,
        out int apSlotId)
    {
        runId = Guid.Empty;
        apSlotId = 0;
        if (!ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
            || shared.RunId == Guid.Empty
            || !ApRunData.TryGetPlayerState(runState, owner.NetId, out ApPlayerRunState state)
            || state.Participation != ApParticipationKind.OwnApSlot
            || !state.ApSlotId.HasValue)
        {
            return false;
        }

        runId = shared.RunId;
        apSlotId = state.ApSlotId.Value;
        return true;
    }

    private static bool IsEnabledFor(
        Player player,
        ApProgressiveStarterActionMessage.StarterKind kind) =>
        ApPlayerContextResolver.TryGetRewardSettings(player, out ArchipelagoSettings settings)
        && kind switch
        {
            ApProgressiveStarterActionMessage.StarterKind.Card =>
                settings.ProgressiveStarterCard,
            ApProgressiveStarterActionMessage.StarterKind.Relic =>
                settings.ProgressiveStarterRelic,
            _ => false,
        };

    private static StarterState<CapturedStarterRecipe> GetOrCaptureSpecification(
        Player player, ApProgressiveStarterActionMessage.StarterKind kind)
    {
        StarterKind domainKind = ProgressiveStarterAdapter.Kind(kind);
        StarterState<CapturedStarterRecipe> Capture() => ProgressiveStarterAdapter.Decode(
            domainKind.Match(() => CaptureCardSpecification(player), () => CaptureRelicSpecification(player)), domainKind);

        if (player.RunState is RunState runState
            && ApRunData.TryGetPlayerState(runState, player.NetId, out ApPlayerRunState playerState))
        {
            var saved = ProgressiveStarterAdapter.Decode(SelectKind(playerState.ProgressiveStarters, domainKind), domainKind);
            if (saved.Match(() => false, () => true, (_, _) => true))
                return saved;

            if (ApRunData.TryGetSharedState(runState, out ApRunSharedState shared))
            {
                var key = (shared.RunId, player.NetId, kind);
                if (PendingSpecifications.TryGetValue(key, out var pending))
                    return pending;
                var captured = Capture();
                PendingSpecifications[key] = captured;
                return captured;
            }
        }
        return Capture();
    }

    private static ApProgressiveStarterKindState CaptureCardSpecification(Player player)
    {
        RunState? captureRunState = null;
        HashSet<CardModel>? preexistingRunCards = null;
        captureRunState = player.RunState as RunState
            ?? throw new InvalidOperationException(
                $"Player {player.NetId} is not attached to a concrete RunState."
            );
        preexistingRunCards = new HashSet<CardModel>(
            GetAllRunCards(captureRunState),
            ReferenceEqualityComparer.Instance
        );
        try
        {
            var tooth = (ArchaicTooth)ModelDb.Relic<ArchaicTooth>().ToMutable();
            if (!tooth.SetupForPlayer(player))
                return Unsupported("card", player);

            if (tooth.StarterCard?.Id is not ModelId baseId
                || tooth.AncientCard?.Id is not ModelId upgradedId)
                throw new InvalidOperationException("Archaic Tooth produced an incomplete recipe.");

            CardModel? baseCard = FindDeckCard(player, baseId.ToString());
            if (baseCard == null)
                throw new InvalidOperationException(
                    $"Archaic Tooth selected absent starter card {baseId}."
                );

            return new ApProgressiveStarterKindState
            {
                Initialized = true,
                Supported = true,
                BaseId = baseId.ToString(),
                UpgradedId = upgradedId.ToString(),
                SerializedBaseModel = Serialize(baseCard.ToSerializable()),
                SerializedUpgradeRelic = Serialize(tooth.ToSerializable()),
                AppliedTier = ProgressiveStarterTier.Basic,
            };
        }
        finally
        {
            RemoveSetupOnlyCards(captureRunState, preexistingRunCards);
        }
    }

    private static ApProgressiveStarterKindState CaptureRelicSpecification(Player player)
    {
        var touch = (TouchOfOrobas)ModelDb.Relic<TouchOfOrobas>().ToMutable();
        if (!touch.SetupForPlayer(player))
            return Unsupported("relic", player);

        if (touch.StarterRelic is not ModelId baseId
            || touch.UpgradedRelic is not ModelId upgradedId)
            throw new InvalidOperationException("Touch of Orobas produced an incomplete recipe.");

        RelicModel? baseRelic = FindOwnedRelic(player, baseId.ToString());
        if (baseRelic == null)
            throw new InvalidOperationException(
                $"Touch of Orobas selected absent starter relic {baseId}."
            );

        return new ApProgressiveStarterKindState
        {
            Initialized = true,
            Supported = true,
            BaseId = baseId.ToString(),
            UpgradedId = upgradedId.ToString(),
            SerializedBaseModel = Serialize(
                (baseRelic.IsMutable ? baseRelic : baseRelic.ToMutable()).ToSerializable()
            ),
            SerializedUpgradeRelic = Serialize(touch.ToSerializable()),
            AppliedTier = ProgressiveStarterTier.Basic,
        };
    }

    private static ApProgressiveStarterKindState Unsupported(string kind, Player player)
    {
        LogUtility.Warn(
            $"Progressive Starter {kind} is enabled, but {player.Character.Id.Entry} has no "
                + "compatible Orobas mapping; leaving it unchanged."
        );
        return new ApProgressiveStarterKindState
        {
            Initialized = true,
            Supported = false,
            AppliedTier = ProgressiveStarterTier.Unsupported,
        };
    }

    private static void Request(
        ApProgressiveStarterActionMessage message,
        Player owner,
        string description)
    {
        ManagedActionRequestScheduler.RequestOrDefer(
            message.ActionId,
            $"Progressive Starter {description}",
            () => RitsuLibManagedNetActions.Request(
                RunManager.Instance,
                ActionDescriptor,
                message,
                owner.NetId
            ),
            () => IsCurrentRequest(message),
            () => LogUtility.Info(
                $"Requested managed Progressive Starter {description} {message.ActionId} "
                    + $"with {message.Targets.Count} target(s) at an idle noncombat boundary."
            ),
            reason =>
            {
                LogUtility.Error(reason);
                MultiplayerSupport.InvalidateRunClaims(reason);
                NotificationUtility.ShowRawText(
                    "Could not synchronize a Progressive Starter item."
                );
            },
            canRequest: NonCombatActionAdmission.CreateGate($"Progressive Starter {description}")
        );
    }

    private static bool IsCurrentRequest(ApProgressiveStarterActionMessage message) =>
        MultiplayerSupport.IsRealMultiplayerRun
        && !MultiplayerSupport.ClaimsInvalidated
        && RunManager.Instance.DebugOnlyGetState() is RunState runState
        && ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
        && shared.RunId == message.RunId;

    private static void FailCapture(string description, Exception ex)
    {
        string reason = $"could not author Progressive Starter {description}: {ex.Message}";
        LogUtility.Error($"{reason}\n{ex}");
        MultiplayerSupport.InvalidateRunClaims(reason);
        NotificationUtility.ShowRawText("Could not synchronize a Progressive Starter item.");
    }

    private static ApProgressiveStarterActionMessage DeserializeMessage(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<ApProgressiveStarterActionMessage>(bytes) ?? new();
        }
        catch (JsonException ex)
        {
            LogUtility.Warn($"Could not deserialize managed Progressive Starter payload: {ex.Message}");
            return new ApProgressiveStarterActionMessage();
        }
    }

    private static async Task ExecuteAction(
        RitsuLibManagedNetActionContext<ApProgressiveStarterActionMessage> context)
    {
        ApProgressiveStarterActionMessage message = context.Message;
        if (!TryValidate(message, context.Player, out RunState runState, out var targets))
        {
            string reason = $"invalid managed Progressive Starter action {message.ActionId}";
            LogUtility.Error(reason);
            MultiplayerSupport.InvalidateRunClaims(reason);
            throw new InvalidOperationException($"Invalid Progressive Starter action {message.ActionId}.");
        }

        try
        {
            foreach (ValidatedTarget target in targets)
            {
                Player player = runState.GetPlayer(target.PlayerNetId)
                    ?? throw new InvalidOperationException(
                        $"Progressive Starter target {target.PlayerNetId} is absent."
                    );
                await ApplyTarget(runState, player, target);
            }
        }
        catch (Exception ex)
        {
            string reason = $"managed Progressive Starter {message.ActionId} failed: {ex.Message}";
            LogUtility.Error($"{reason}\n{ex}");
            MultiplayerSupport.InvalidateRunClaims(reason);
            throw;
        }
    }

    private static bool TryValidate(
        ApProgressiveStarterActionMessage message,
        Player owner,
        out RunState runState,
        out List<ValidatedTarget> targets)
    {
        runState = null!;
        targets = new();
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.ShouldRunReplicatedConstruction(
                MultiplayerFeature.ProgressiveStarters
            )
            || message.SchemaVersion != SchemaVersion
            || message.RunId == Guid.Empty
            || message.ActionId == Guid.Empty
            || message.Reason is not (
                ApProgressiveStarterActionMessage.ActionReason.Initialization
                or ApProgressiveStarterActionMessage.ActionReason.LiveReceipt
            )
            || message.OwnerNetId != owner.NetId
            || message.Targets.Count is < 1 or > 16
            || message.ReceivedItemIndex < 0
            || RunManager.Instance.DebugOnlyGetState() is not RunState current
            || !ApRunData.TryGetSharedState(current, out ApRunSharedState shared)
            || shared.RunId != message.RunId
            || !ApRunData.TryGetPlayerState(current, owner.NetId, out ApPlayerRunState ownerState)
            || ownerState.Participation != ApParticipationKind.OwnApSlot
            || ownerState.ApSlotId != message.ApSlotId)
        {
            return false;
        }

        if (message.Reason == ApProgressiveStarterActionMessage.ActionReason.Initialization
            && (message.ReceivedItemIndex != 0 || message.CharacterOffset.HasValue))
        {
            return false;
        }

        var identities = new HashSet<(ulong, ApProgressiveStarterActionMessage.StarterKind)>();
        foreach (ApProgressiveStarterActionMessage.Target target in message.Targets)
        {
            if (target == null || !identities.Add((target.PlayerNetId, target.Kind))
                || current.GetPlayer(target.PlayerNetId) is not Player player
                || !ApRunData.TryGetPlayerState(
                    current,
                    target.PlayerNetId,
                    out ApPlayerRunState targetState
                )
                || target.PlayerNetId != owner.NetId
                || targetState.Participation != ApParticipationKind.OwnApSlot
                || !IsEnabledFor(player, target.Kind))
            {
                return false;
            }

            if (message.Reason == ApProgressiveStarterActionMessage.ActionReason.LiveReceipt
                && (message.ReceivedItemIndex <= 0
                    || !message.CharacterOffset.HasValue
                    || player.GetAPCharacterNumber() != message.CharacterOffset))
            {
                return false;
            }

            try
            {
                StarterKind kind = ProgressiveStarterAdapter.Kind(target.Kind);
                var specification = ProgressiveStarterAdapter.Decode(target.Specification, kind);
                if (!specification.Match(() => false, () => true,
                        (recipe, _) => ValidateRecipe(recipe, player)))
                    return false;

                var saved = ProgressiveStarterAdapter.Decode(SelectKind(targetState.ProgressiveStarters, kind), kind);
                var plan = ProgressiveStarterAdapter.Require(StarterProgression.Plan(saved, specification,
                    ProgressiveStarterAdapter.Context(message.Reason), (int)target.TargetTier));
                targets.Add(new ValidatedTarget(target.PlayerNetId, kind, plan));
            }
            catch (InvalidOperationException ex)
            {
                LogUtility.Warn($"Invalid Progressive Starter {target.Kind} for {player.NetId}: {ex.Message}");
                return false;
            }
        }

        runState = current;
        return true;
    }

    private static bool ValidateRecipe(CapturedStarterRecipe recipe, Player player)
    {
        try
        {
            StarterMapping mapping = recipe.Mapping;
            bool baseMatches = mapping.Kind.Match(
                () => IdEquals(Deserialize<SerializableCard>(recipe.SerializedBaseModel).Id?.ToString(), mapping.BaseId),
                () => IdEquals(RelicModel.FromSerializable(Deserialize<SerializableRelic>(recipe.SerializedBaseModel))
                    .Id.ToString(), mapping.BaseId));
            if (!baseMatches)
                return false;

            RelicModel upgrade = RelicModel.FromSerializable(Deserialize<SerializableRelic>(recipe.SerializedUpgradeRelic));
            return mapping.Kind.Match(
                () => upgrade is ArchaicTooth tooth
                    && IdEquals(tooth.StarterCard?.Id?.ToString(), mapping.BaseId)
                    && IdEquals(tooth.AncientCard?.Id?.ToString(), mapping.UpgradedId),
                () => upgrade is TouchOfOrobas touch
                    && IdEquals(touch.StarterRelic?.ToString(), mapping.BaseId)
                    && IdEquals(touch.UpgradedRelic?.ToString(), mapping.UpgradedId));
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Invalid Progressive Starter {recipe.Mapping.Kind} specification for {player.NetId}: {ex.Message}");
            return false;
        }
    }

    private static async Task ApplyTarget(RunState runState, Player player, ValidatedTarget target)
    {
        if (!ApRunData.TryGetPlayerState(runState, player.NetId, out ApPlayerRunState playerState))
            throw new InvalidOperationException($"No AP run state exists for {player.NetId}.");

        var state = target.Plan.State;
        SetKind(playerState.ProgressiveStarters, target.Kind, ProgressiveStarterAdapter.Encode(state));
        foreach (StarterOperation operation in target.Plan.Operations)
        {
            await state.Match(
                () => throw new InvalidOperationException("Cannot execute an uninitialized starter."),
                () => throw new InvalidOperationException("Cannot execute an unsupported starter."),
                (recipe, _) => operation.Match(
                    () => RemoveBase(player, recipe),
                    () => RestoreBase(player, recipe),
                    () => GrantUpgradeRelic(player, recipe)));
            // Preserve successful intermediate commands if a later command fails. No replay or rollback.
            state = ProgressiveStarterAdapter.Require(StarterProgression.AfterApplied(state, operation));
            SetKind(playerState.ProgressiveStarters, target.Kind, ProgressiveStarterAdapter.Encode(state));
        }

        if (!ApRunData.SetProgressiveStarterState(runState, player.NetId, playerState.ProgressiveStarters))
            throw new InvalidOperationException($"Could not persist Progressive Starter state for {player.NetId}.");

        state.Match(
            () => false,
            () =>
            {
                LogUtility.Info($"Managed Progressive Starter {target.Kind} is unsupported for "
                    + $"{player.Character.Id.Entry} ({player.NetId}); no mutation was applied.");
                return true;
            },
            (_, tier) =>
            {
                LogUtility.Success($"Managed Progressive Starter {target.Kind} applied tier {tier} for "
                    + $"{player.Character.Id.Entry} ({player.NetId}).");
                return true;
            });
    }

    private static Task RemoveBase(Player player, CapturedStarterRecipe recipe) =>
        recipe.Mapping.Kind.Match(
            async () =>
            {
                CardModel? card = FindDeckCard(player, recipe.Mapping.BaseId);
                if (card != null)
                    await CardPileCmd.RemoveFromDeck(card, showPreview: false);
            },
            async () =>
            {
                RelicModel? relic = FindOwnedRelic(player, recipe.Mapping.BaseId);
                if (relic != null)
                    await RelicCmd.Remove(relic);
            });

    private static Task RestoreBase(Player player, CapturedStarterRecipe recipe) =>
        recipe.Mapping.Kind.Match(
            async () =>
            {
                if (FindDeckCard(player, recipe.Mapping.BaseId) != null)
                    return;
                CardModel card = player.RunState.LoadCard(Deserialize<SerializableCard>(recipe.SerializedBaseModel), player);
                var addResult = await CardPileCmd.Add(card, PileType.Deck, skipVisuals: true);
                if (!addResult.success)
                    throw new InvalidOperationException($"The game rejected starter card {card.Id}.");
            },
            async () =>
            {
                if (FindOwnedRelic(player, recipe.Mapping.BaseId) != null)
                    return;
                RelicModel relic = RelicModel.FromSerializable(Deserialize<SerializableRelic>(recipe.SerializedBaseModel));
                await RelicCmd.Obtain(relic, player);
                if (FindOwnedRelic(player, recipe.Mapping.BaseId) == null)
                    throw new InvalidOperationException($"The game rejected starter relic {recipe.Mapping.BaseId}.");
            });

    private static async Task GrantUpgradeRelic(
        Player player,
        CapturedStarterRecipe state)
    {
        RelicModel relic = RelicModel.FromSerializable(
            Deserialize<SerializableRelic>(state.SerializedUpgradeRelic)
        );
        if (FindOwnedRelic(player, relic.Id.ToString()) != null)
            return;

        await RelicCmd.Obtain(relic, player);
        if (FindOwnedRelic(player, relic.Id.ToString()) == null)
            throw new InvalidOperationException($"The game rejected Orobas relic {relic.Id}.");
    }

    private static ApProgressiveStarterKindState SelectKind(
        ApProgressiveStarterPlayerState state, StarterKind kind) =>
        kind.Match(() => state.Card, () => state.Relic);

    private static void SetKind(ApProgressiveStarterPlayerState state, StarterKind kind,
        ApProgressiveStarterKindState value) => kind.Match(
            () => state.Card = value, () => state.Relic = value);

    private static CardModel? FindDeckCard(Player player, string idEntry) =>
        player.Deck.Cards.FirstOrDefault(card =>
            string.Equals(card.Id.ToString(), idEntry, StringComparison.OrdinalIgnoreCase));

    private static RelicModel? FindOwnedRelic(Player player, string idEntry) =>
        player.Relics.FirstOrDefault(relic =>
            string.Equals(relic.Id.ToString(), idEntry, StringComparison.OrdinalIgnoreCase));

    private static bool IdEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Archaic Tooth's public setup method creates its preview transformation through
    /// RunState.CreateCard. Recipe capture happens only on the action author, so that preview must
    /// be removed again before networking or the replicas would begin with different run cards.
    /// </summary>
    private static IReadOnlyList<CardModel> GetAllRunCards(RunState runState) => runState._allCards;

    private static void RemoveSetupOnlyCards(
        RunState runState,
        IReadOnlySet<CardModel> preexistingRunCards)
    {
        foreach (CardModel card in GetAllRunCards(runState)
                     .Where(card => !preexistingRunCards.Contains(card))
                     .ToArray())
        {
            runState.RemoveCard(card);
        }
    }

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, SerializationUtility.CombinedOptions);

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, SerializationUtility.CombinedOptions)
        ?? throw new InvalidOperationException($"Could not deserialize {typeof(T).Name}.");
}
