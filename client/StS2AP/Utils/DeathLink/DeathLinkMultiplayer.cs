using System.Text.Json;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Converters;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Networking.ManagedActions;
using STS2RitsuLib.Networking.Sidecar;

namespace StS2AP.Utils;

/// <summary>
/// Relays external DeathLinks to the native host, admits them one at a time at an idle native
/// action boundary, and makes the host the sole authority for outgoing multiplayer DeathLinks.
/// </summary>
public static class DeathLinkMultiplayer
{
    private const int SchemaVersion = 3;
    private const string InboundRequestMessageKey = "death_link_inbound_request_v3";
    private const string CombatActionKey = "death_link_combat_damage_v3";
    private const string NonCombatActionKey = "death_link_noncombat_damage_v3";
    private const string OutboundInstructionMessageKey = "death_link_outbound_instruction_v3";
    private static readonly object StateLock = new();

    private static readonly Queue<DeathLinkInboundRequestMessage> PendingInbound = new();
    private static readonly HashSet<Guid> AcceptedInboundEvents = new();
    private static readonly DeathLinkEventLedger EventLedger = new();
    private static readonly HashSet<Guid> HandledInboundEvents = new();
    private static readonly HashSet<Guid> HandledOutboundInstructions = new();

    private static readonly RitsuLibSidecarJsonSerializer<DeathLinkInboundRequestMessage>
        InboundRequestSerializer = new();
    private static readonly RitsuLibSidecarMessageDescriptor<DeathLinkInboundRequestMessage>
        InboundRequestDescriptor = new(
            ModEntry.ModId,
            InboundRequestMessageKey,
            InboundRequestSerializer.Serialize,
            InboundRequestSerializer.Deserialize,
            Required: true
        );
    private static readonly RitsuLibSidecarJsonSerializer<DeathLinkSendInstructionMessage>
        OutboundInstructionSerializer = new();
    private static readonly RitsuLibSidecarMessageDescriptor<DeathLinkSendInstructionMessage>
        OutboundInstructionDescriptor = new(
            ModEntry.ModId,
            OutboundInstructionMessageKey,
            OutboundInstructionSerializer.Serialize,
            OutboundInstructionSerializer.Deserialize,
            Required: true
        );

    private static readonly RitsuLibManagedNetActionDescriptor<DeathLinkActionMessage>
        CombatActionDescriptor = new(
            ModuleId: ModEntry.ModId,
            ActionKey: CombatActionKey,
            Serialize: static message => JsonSerializer.SerializeToUtf8Bytes(message),
            Deserialize: DeserializeActionMessage,
            Execute: ExecuteDamageAction,
            ActionType: GameActionType.CombatPlayPhaseOnly
        );
    private static readonly RitsuLibManagedNetActionDescriptor<DeathLinkActionMessage>
        NonCombatActionDescriptor = new(
            ModuleId: ModEntry.ModId,
            ActionKey: NonCombatActionKey,
            Serialize: static message => JsonSerializer.SerializeToUtf8Bytes(message),
            Deserialize: DeserializeActionMessage,
            Execute: ExecuteDamageAction,
            ActionType: GameActionType.NonCombat
        );

    private static IDisposable? _inboundRequestSubscription;
    private static IDisposable? _outboundInstructionSubscription;
    private static SceneTree? _sceneTree;
    private static bool _processFrameHooked;
    private static Guid? _inboundActionInFlight;
    private static Guid? _lastBlockedInboundEvent;
    private static string? _lastInboundBlockReason;

    public static void Initialize()
    {
        if (_inboundRequestSubscription != null)
            return;

        _inboundRequestSubscription = RitsuLibSidecarTypedMessageRegistry.Subscribe(
            InboundRequestDescriptor,
            OnInboundRequested
        );
        _outboundInstructionSubscription = RitsuLibSidecarTypedMessageRegistry.Subscribe(
            OutboundInstructionDescriptor,
            OnOutboundInstructionReceived
        );
        RitsuLibManagedNetActions.Register(CombatActionDescriptor);
        RitsuLibManagedNetActions.Register(NonCombatActionDescriptor);
    }

    public static void EndRun()
    {
        lock (StateLock)
        {
            PendingInbound.Clear();
            AcceptedInboundEvents.Clear();
            HandledInboundEvents.Clear();
            HandledOutboundInstructions.Clear();
            EventLedger.Clear();
            _inboundActionInFlight = null;
            _lastBlockedInboundEvent = null;
            _lastInboundBlockReason = null;
        }
        UnhookProcessFrame();
    }

    /// <summary>
    /// Relays one AP SDK callback to the native host. The callback belongs only to the local
    /// connected player, even when other players use the same AP slot.
    /// </summary>
    public static void Receive(DeathLink info)
    {
        if (!TryGetLocalOwnSlotContext(out _, out ApRunSharedState shared, out Player owner, out _))
            return;
        string source = info.Source ?? string.Empty;
        string? cause = info.Cause;
        long timestampTicks = info.Timestamp.Ticks;
        Guid runId = shared.RunId;
        ulong ownerNetId = owner.NetId;
        Callable.From(() => SubmitInboundOnMainThread(
            runId, ownerNetId, source, cause, timestampTicks)).CallDeferred();
    }

    /// <summary>
    /// Observes a death only after base-game death prevention has completed. Every replica sees
    /// this callback, but only the native host is allowed to authorize an AP-side send.
    /// </summary>
    public static void PlayerDied(Player player)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.DeathLink)
            || RunManager.Instance.NetService.Type != NetGameType.Host
            || player.RunState is not RunState runState
            || runState.CurrentRoom?.IsVictoryRoom == true
            || !ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
            || shared.RunId == Guid.Empty
            || !ApRunData.TryGetPlayerState(runState, player.NetId, out ApPlayerRunState playerState)
            || playerState.Participation == ApParticipationKind.VanillaGuest
            || !ApPlayerContextResolver.TryGetRewardSettings(
                player,
                out ArchipelagoSettings settings
            )
            || !settings.IsDeathLinkEnabled)
        {
            return;
        }

        if (ShouldSuppressOutgoing(player.NetId, out string reason))
        {
            LogUtility.Info(
                $"Suppressing outgoing DeathLink for player {player.NetId}: {reason}."
            );
            return;
        }

        string floorCause = $"Act {runState.CurrentActIndex + 1} Floor {runState.ActFloor}";
        string characterName = player.Character.Id.Entry;
        Guid eventId = Guid.NewGuid();
        ulong hostNetId = RunManager.Instance.NetService.NetId;

        if (player.NetId == hostNetId)
        {
            SendLocalAuthorizedDeathLink(
                eventId,
                player.NetId,
                characterName,
                floorCause
            );
            return;
        }

        var instruction = new DeathLinkSendInstructionMessage
        {
            RunId = shared.RunId,
            EventId = eventId,
            OwnerNetId = player.NetId,
            CharacterName = characterName,
            FloorCause = floorCause,
        };

        bool sent;
        try
        {
            sent = RitsuLibSidecarTypedMessageRegistry.SendToPeer(
                RunManager.Instance.NetService,
                player.NetId,
                OutboundInstructionDescriptor,
                instruction
            );
        }
        catch (Exception ex)
        {
            LogUtility.Error(
                $"Discarded host-authorized DeathLink {eventId} for AP owner "
                    + $"{player.NetId}: {ex.Message}"
            );
            return;
        }

        if (!sent)
        {
            LogUtility.Warn(
                $"Discarded host-authorized DeathLink {eventId} for unavailable AP owner "
                    + $"{player.NetId}."
            );
            return;
        }

        LogUtility.Info($"Host authorized DeathLink {eventId} for AP owner {player.NetId}.");
    }

    private static void SubmitInboundOnMainThread(
        Guid runId, ulong ownerNetId, string source, string? cause, long timestampTicks)
    {
        if (!TryGetLocalOwnSlotContext(
                out _,
                out ApRunSharedState shared,
                out Player owner,
                out _
            )
            || shared.RunId != runId || owner.NetId != ownerNetId
            || source.Length > 1024
            || timestampTicks <= 0 || timestampTicks > DateTime.MaxValue.Ticks
            || cause?.Length > 2048)
        {
            LogUtility.Warn("Ignored multiplayer DeathLink without a valid local own-slot run owner.");
            return;
        }

        lock (StateLock)
        {
            if (EventLedger.WasSent(source, timestampTicks))
            {
                LogUtility.Info($"Ignored own DeathLink echo for AP owner {owner.NetId}.");
                return;
            }
        }

        var request = new DeathLinkInboundRequestMessage
        {
            RunId = shared.RunId,
            EventId = Guid.NewGuid(),
            OwnerNetId = owner.NetId,
            TimestampTicks = timestampTicks,
            Source = source,
            Cause = cause,
        };

        INetGameService netService = RunManager.Instance.NetService;
        if (netService.Type == NetGameType.Host)
        {
            AcceptInboundRequest(request, owner.NetId);
            return;
        }

        bool sent;
        try
        {
            sent = RitsuLibSidecarTypedMessageRegistry.SendToHost(
                netService,
                InboundRequestDescriptor,
                request
            );
        }
        catch (Exception ex)
        {
            LogUtility.Error(
                $"Discarded incoming DeathLink {request.EventId}; it could not reach the host: "
                    + ex.Message
            );
            return;
        }

        if (!sent)
        {
            LogUtility.Warn(
                $"Discarded incoming DeathLink {request.EventId}; the host relay was unavailable."
            );
            return;
        }

        LogUtility.Info(
            $"Relayed incoming DeathLink {request.EventId} for AP owner {owner.NetId} to the host."
        );
    }

    private static void OnInboundRequested(
        RitsuLibSidecarTypedDispatchContext<DeathLinkInboundRequestMessage> context)
    {
        bool posted = RitsuLibSidecarGodotMainLoopScheduling.TryPostToMainLoop(
            () => AcceptInboundRequest(context.Message, context.SenderNetId)
        );
        if (!posted)
        {
            LogUtility.Error(
                $"Discarded incoming DeathLink {context.Message.EventId}; the host main loop "
                    + "was unavailable."
            );
        }
    }

    private static void AcceptInboundRequest(
        DeathLinkInboundRequestMessage request,
        ulong senderNetId)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host
            || senderNetId != request.OwnerNetId
            || !TryValidateInboundRequest(request, out _, out _, out _))
        {
            LogUtility.Warn(
                $"Rejected multiplayer DeathLink request {request.EventId} from {senderNetId}."
            );
            return;
        }

        lock (StateLock)
        {
            // A duplicate SDK callback can have a different relay GUID. Deduplicate the actual
            // AP event per recipient, never per shared AP slot or across all recipients.
            if (!AcceptedInboundEvents.Add(request.EventId)
                || !EventLedger.TryAcceptInbound(
                    request.OwnerNetId, request.Source, request.TimestampTicks))
            {
                LogUtility.Info($"Ignored duplicate DeathLink for AP owner {request.OwnerNetId}.");
                return;
            }
            PendingInbound.Enqueue(request);
        }

        LogUtility.Info(
            $"Host queued incoming DeathLink {request.EventId} for AP owner "
                + $"{request.OwnerNetId}; {DescribeAdmissionState()}."
        );
        if (!EnsureProcessFrameHook())
        {
            LogUtility.Error(
                $"Incoming DeathLink {request.EventId} is pending, but the Godot process-frame "
                    + "signal is unavailable."
            );
            return;
        }
        ProcessPendingInbound();
    }

    private static void ProcessPendingInbound()
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host)
            return;

        DeathLinkInboundRequestMessage request;
        lock (StateLock)
        {
            if (_inboundActionInFlight.HasValue || PendingInbound.Count == 0)
            {
                if (!_inboundActionInFlight.HasValue)
                    UnhookProcessFrame();
                return;
            }
            request = PendingInbound.Peek();
        }

        if (!TryValidateInboundRequest(
                request,
                out RunState runState,
                out Player slotOwner,
                out ArchipelagoSettings settings
            ))
        {
            lock (StateLock)
                PendingInbound.Dequeue();
            ClearAdmissionBlocker(request.EventId);
            LogUtility.Warn(
                $"Consumed stale incoming DeathLink {request.EventId} before admission."
            );
            ProcessPendingInbound();
            return;
        }

        if (!TryGetSafeActionDescriptor(
                out RitsuLibManagedNetActionDescriptor<DeathLinkActionMessage> descriptor,
                out string blockedReason
            ))
        {
            LogAdmissionBlocked(request.EventId, blockedReason);
            return;
        }

        var message = new DeathLinkActionMessage
        {
            RunId = request.RunId,
            EventId = request.EventId,
            SlotOwnerNetId = request.OwnerNetId,
            DamagePercent = settings.DeathLinkDamagePercent,
            Source = request.Source,
            Cause = request.Cause,
            Targets = BuildTargetPlans(slotOwner, settings.DeathLinkDamagePercent),
        };

        lock (StateLock)
            _inboundActionInFlight = request.EventId;

        bool requested;
        try
        {
            requested = RitsuLibManagedNetActions.Request(
                RunManager.Instance,
                descriptor,
                message,
                RunManager.Instance.NetService.NetId
            );
        }
        catch (Exception ex)
        {
            lock (StateLock)
                _inboundActionInFlight = null;
            LogAdmissionBlocked(
                request.EventId,
                $"managed-action request threw {ex.GetType().Name}: {ex.Message}"
            );
            return;
        }

        if (!requested)
        {
            lock (StateLock)
                _inboundActionInFlight = null;
            LogAdmissionBlocked(
                request.EventId,
                "managed-action request returned false; transport, peer capability, or run "
                    + "context is not ready"
            );
            return;
        }

        lock (StateLock)
            PendingInbound.Dequeue();
        ClearAdmissionBlocker(request.EventId);

        LogUtility.Info(
            $"Host admitted incoming DeathLink {request.EventId} as a "
                + $"{descriptor.ActionType} action; {DescribeAdmissionState()}."
        );
    }

    private static bool TryGetSafeActionDescriptor(
        out RitsuLibManagedNetActionDescriptor<DeathLinkActionMessage> descriptor,
        out string blockedReason)
    {
        descriptor = null!;
        blockedReason = string.Empty;
        CombatManager combat = CombatManager.Instance;
        if (combat.IsStarting)
        {
            blockedReason = "combat is starting";
            return false;
        }
        if (combat.IsEnding)
        {
            blockedReason = $"combat is ending (aboutToLose={combat.IsAboutToLose})";
            return false;
        }
        if (RunManager.Instance.ActionExecutor.CurrentlyRunningAction is { } currentAction)
        {
            blockedReason = $"native action {currentAction.GetType().Name} is still running "
                + $"(state={currentAction.State}, synchronizer="
                + $"{RunManager.Instance.ActionQueueSynchronizer.CombatState})";
            return false;
        }
        if (!RunManager.Instance.ActionQueueSet.IsEmpty)
        {
            blockedReason = "native action queues are not empty "
                + $"(executorRunning={RunManager.Instance.ActionExecutor.IsRunning}, "
                + $"executorPaused={RunManager.Instance.ActionExecutor.IsPaused}, synchronizer="
                + $"{RunManager.Instance.ActionQueueSynchronizer.CombatState})";
            return false;
        }

        ActionSynchronizerCombatState synchronizerState =
            RunManager.Instance.ActionQueueSynchronizer.CombatState;
        if (BetaMainCompatibility.IsActionSynchronizerCombatState(
                synchronizerState,
                ActionSynchronizerCombatState.PlayPhase))
        {
            // PlayPhase is the native synchronizer's authoritative indication that it is safe to
            // enqueue a CombatPlayPhaseOnly action. Rechecking CombatManager.IsInProgress here can
            // observe a different transition snapshot and incorrectly defer until NonCombat.
            descriptor = CombatActionDescriptor;
            return true;
        }
        if (!combat.IsInProgress
            && BetaMainCompatibility.IsActionSynchronizerCombatState(
                synchronizerState,
                ActionSynchronizerCombatState.NotInCombat))
        {
            if (NonCombatActionAdmission.CaptureState().BlockedReason is string reason)
            {
                blockedReason = reason;
                return false;
            }
            descriptor = NonCombatActionDescriptor;
            return true;
        }

        blockedReason = "no descriptor matches the current phase "
            + $"(combatInProgress={combat.IsInProgress}, synchronizer={synchronizerState})";
        return false;
    }

    private static void LogAdmissionBlocked(Guid eventId, string reason)
    {
        bool changed;
        lock (StateLock)
        {
            changed = _lastBlockedInboundEvent != eventId
                || !string.Equals(_lastInboundBlockReason, reason, StringComparison.Ordinal);
            _lastBlockedInboundEvent = eventId;
            _lastInboundBlockReason = reason;
        }

        if (changed)
        {
            LogUtility.Info(
                $"Incoming DeathLink {eventId} is waiting for admission: {reason}; "
                    + $"{DescribeAdmissionState()}."
            );
        }
    }

    private static void ClearAdmissionBlocker(Guid eventId)
    {
        lock (StateLock)
        {
            if (_lastBlockedInboundEvent != eventId)
                return;
            _lastBlockedInboundEvent = null;
            _lastInboundBlockReason = null;
        }
    }

    private static string DescribeAdmissionState()
    {
        CombatManager combat = CombatManager.Instance;
        var executor = RunManager.Instance.ActionExecutor;
        string currentAction = executor.CurrentlyRunningAction?.GetType().Name ?? "none";
        return $"combatInProgress={combat.IsInProgress}, combatStarting={combat.IsStarting}, "
            + $"combatEnding={combat.IsEnding}, aboutToLose={combat.IsAboutToLose}, "
            + $"synchronizer={RunManager.Instance.ActionQueueSynchronizer.CombatState}, "
            + $"executorRunning={executor.IsRunning}, executorPaused={executor.IsPaused}, "
            + $"currentAction={currentAction}, queueEmpty="
            + RunManager.Instance.ActionQueueSet.IsEmpty;
    }

    private static List<DeathLinkActionMessage.TargetPlan> BuildTargetPlans(
        Player slotOwner,
        int damagePercent)
    {
        int damage = Mathf.RoundToInt(slotOwner.Creature.MaxHp * (damagePercent / 100.0f));
        return new List<DeathLinkActionMessage.TargetPlan>
        {
            new()
            {
                NetId = slotOwner.NetId,
                NewHp = slotOwner.Creature.IsDead
                    ? 0
                    : Math.Max(0, slotOwner.Creature.CurrentHp - damage),
            },
        };
    }

    private static DeathLinkActionMessage DeserializeActionMessage(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<DeathLinkActionMessage>(bytes) ?? new();
        }
        catch (JsonException ex)
        {
            LogUtility.Warn($"Could not deserialize managed DeathLink payload: {ex.Message}");
            return new DeathLinkActionMessage();
        }
    }

    private static async Task ExecuteDamageAction(
        RitsuLibManagedNetActionContext<DeathLinkActionMessage> context)
    {
        DeathLinkActionMessage message = context.Message;
        bool hostOwnsAdmission = RunManager.Instance.NetService.Type == NetGameType.Host;
        try
        {
            if (!TryValidateAction(message, context.Player, out RunState runState))
            {
                LogUtility.Warn(
                    $"Consumed invalid managed DeathLink {message.EventId} owned by "
                        + $"{context.Player.NetId}."
                );
                return;
            }

            lock (StateLock)
            {
                if (!HandledInboundEvents.Add(message.EventId))
                    return;
            }

            var plans = new List<(Player Target, int NewHp)>();
            foreach (DeathLinkActionMessage.TargetPlan plan in message.Targets.OrderBy(
                         target => target.NetId
                     ))
            {
                Player target = runState.GetPlayer(plan.NetId)
                    ?? throw new InvalidOperationException(
                        $"DeathLink target {plan.NetId} was absent from the run."
                    );
                if (target.Creature.IsDead)
                {
                    LogUtility.Info(
                        $"Consumed managed DeathLink {message.EventId} for already-dead target "
                            + $"{target.NetId}."
                    );
                    continue;
                }

                int localExpectedDamage = Mathf.RoundToInt(
                    target.Creature.MaxHp * (message.DamagePercent / 100.0f)
                );
                int localExpectedHp = Math.Max(
                    0,
                    target.Creature.CurrentHp - localExpectedDamage
                );
                if (localExpectedHp != plan.NewHp)
                {
                    LogUtility.Warn(
                        $"DeathLink {message.EventId} observed pre-application HP divergence for "
                            + $"{target.NetId}: host={plan.NewHp}, local={localExpectedHp}; applying "
                            + "the host value."
                    );
                }

                if (LocalContext.IsMe(target))
                {
                    string cause = message.Cause ?? $"{message.Source} died";
                    try
                    {
                        NotificationUtility.ShowDeathLink(new DeathLink(message.Source, cause));
                    }
                    catch (Exception ex)
                    {
                        // Presentation is secondary to the host-authored HP mutation.
                        LogUtility.Error(
                            $"Could not show DeathLink {message.EventId} notification: "
                                + ex.Message
                        );
                    }
                }
                plans.Add((target, plan.NewHp));
            }

            // Mark every target before changing the first one. A death callback can synchronously
            // affect another target, and all deaths caused by this incoming event must be silent.
            lock (StateLock)
            {
                foreach ((Player target, int newHp) in plans)
                {
                    EventLedger.BeginDamage(target.NetId, newHp == 0, DateTime.UtcNow);
                }
            }

            try
            {
                foreach ((Player target, int newHp) in plans)
                {
                    LogUtility.Info(
                        $"Applying host-ordered DeathLink {message.EventId} to {target.NetId}: "
                            + $"{target.Creature.CurrentHp}->{newHp} HP."
                    );
                    await CreatureCmd.SetCurrentHp(target.Creature, newHp);
                }
            }
            finally
            {
                lock (StateLock)
                {
                    foreach ((Player target, _) in plans)
                    {
                        EventLedger.EndDamage(target.NetId, target.Creature.IsDead);
                    }
                }
            }
        }
        finally
        {
            if (hostOwnsAdmission)
                CompleteInboundAdmission(message.EventId);
        }
    }

    private static bool TryValidateInboundRequest(
        DeathLinkInboundRequestMessage request,
        out RunState runState,
        out Player owner,
        out ArchipelagoSettings settings)
    {
        runState = null!;
        owner = null!;
        settings = null!;
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.DeathLink)
            || request.SchemaVersion != SchemaVersion
            || request.RunId == Guid.Empty
            || request.EventId == Guid.Empty
            || request.TimestampTicks <= 0 || request.TimestampTicks > DateTime.MaxValue.Ticks
            || request.Source is null
            || request.Source.Length > 1024
            || request.Cause?.Length > 2048
            || RunManager.Instance.DebugOnlyGetState() is not RunState current
            || !ApRunData.TryGetSharedState(current, out ApRunSharedState shared)
            || shared.RunId != request.RunId
            || current.GetPlayer(request.OwnerNetId) is not Player currentOwner
            || !ApRunData.TryGetPlayerState(
                current,
                request.OwnerNetId,
                out ApPlayerRunState ownerState
            )
            || ownerState.Participation != ApParticipationKind.OwnApSlot
            || ownerState.SlotSettings is not ArchipelagoSettings ownerSettings
            || !ownerSettings.IsDeathLinkEnabled
            || ownerSettings.DeathLinkDamagePercent is < 0 or > 100)
        {
            return false;
        }

        runState = current;
        owner = currentOwner;
        settings = ownerSettings;
        return true;
    }

    private static bool TryValidateAction(
        DeathLinkActionMessage message,
        Player actionOwner,
        out RunState runState)
    {
        runState = null!;
        if (!BetaMainCompatibility.TryGetHostNetId(
                RunManager.Instance.NetService,
                out ulong hostNetId
            )
            || actionOwner.NetId != hostNetId
            || message.SchemaVersion != SchemaVersion
            || message.RunId == Guid.Empty
            || message.EventId == Guid.Empty
            || message.Source is null
            || message.Source.Length > 1024
            || message.Cause?.Length > 2048
            || message.DamagePercent is < 0 or > 100
            || message.Targets is null
            || RunManager.Instance.DebugOnlyGetState() is not RunState current
            || !ApRunData.TryGetSharedState(current, out ApRunSharedState shared)
            || shared.RunId != message.RunId
            || current.GetPlayer(message.SlotOwnerNetId) is not Player slotOwner
            || !ApRunData.TryGetPlayerState(
                current,
                message.SlotOwnerNetId,
                out ApPlayerRunState ownerState
            )
            || ownerState.Participation != ApParticipationKind.OwnApSlot
            || ownerState.SlotSettings is not ArchipelagoSettings settings
            || !settings.IsDeathLinkEnabled
            || settings.DeathLinkDamagePercent != message.DamagePercent)
        {
            return false;
        }

        if (message.Targets.Count != 1 || message.Targets[0].NetId != slotOwner.NetId)
        {
            return false;
        }

        foreach (DeathLinkActionMessage.TargetPlan plan in message.Targets)
        {
            Player? target = current.GetPlayer(plan.NetId);
            if (target == null || plan.NewHp < 0 || plan.NewHp > target.Creature.MaxHp)
                return false;
        }

        runState = current;
        return true;
    }

    private static bool TryGetLocalOwnSlotContext(
        out RunState runState,
        out ApRunSharedState shared,
        out Player owner,
        out ArchipelagoSettings settings)
    {
        runState = null!;
        shared = null!;
        owner = null!;
        settings = null!;
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.IsLocalOwnApSlot
            || !MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.DeathLink)
            || GameUtility.CurrentPlayer is not Player localOwner
            || !MultiplayerLocationChecks.IsLocalProgressOwner(localOwner)
            || localOwner.RunState is not RunState current
            || !ApRunData.TryGetSharedState(current, out ApRunSharedState currentShared)
            || currentShared.RunId == Guid.Empty
            || !ApRunData.TryGetPlayerState(
                current,
                localOwner.NetId,
                out ApPlayerRunState ownerState
            )
            || ownerState.Participation != ApParticipationKind.OwnApSlot
            || ownerState.SlotSettings is not ArchipelagoSettings ownerSettings
            || !ownerSettings.IsDeathLinkEnabled)
        {
            return false;
        }

        runState = current;
        shared = currentShared;
        owner = localOwner;
        settings = ownerSettings;
        return true;
    }

    private static void OnOutboundInstructionReceived(
        RitsuLibSidecarTypedDispatchContext<DeathLinkSendInstructionMessage> context)
    {
        bool posted = RitsuLibSidecarGodotMainLoopScheduling.TryPostToMainLoop(
            () => ExecuteOutboundInstruction(context.Message, context.SenderNetId)
        );
        if (!posted)
        {
            LogUtility.Error(
                $"Discarded host-authorized DeathLink {context.Message.EventId}; the local "
                    + "main loop was unavailable."
            );
        }
    }

    private static void ExecuteOutboundInstruction(
        DeathLinkSendInstructionMessage message,
        ulong senderNetId)
    {
        if (!TryValidateOutboundInstruction(message, senderNetId))
        {
            LogUtility.Warn(
                $"Rejected host-authorized DeathLink instruction {message.EventId} from "
                    + $"{senderNetId}."
            );
            return;
        }

        lock (StateLock)
        {
            if (!HandledOutboundInstructions.Add(message.EventId))
                return;
        }

        SendLocalAuthorizedDeathLink(
            message.EventId,
            message.OwnerNetId,
            message.CharacterName,
            message.FloorCause
        );
    }

    private static bool TryValidateOutboundInstruction(
        DeathLinkSendInstructionMessage message,
        ulong senderNetId)
    {
        INetGameService netService = RunManager.Instance.NetService;
        return netService.Type == NetGameType.Client
            && BetaMainCompatibility.TryGetHostNetId(netService, out ulong hostNetId)
            && senderNetId == hostNetId
            && message.SchemaVersion == SchemaVersion
            && message.RunId != Guid.Empty
            && message.EventId != Guid.Empty
            && message.OwnerNetId == netService.NetId
            && !string.IsNullOrEmpty(message.CharacterName)
            && message.CharacterName.Length <= 1024
            && !string.IsNullOrEmpty(message.FloorCause)
            && message.FloorCause.Length <= 1024
            && RunManager.Instance.DebugOnlyGetState() is RunState runState
            && ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
            && shared.RunId == message.RunId
            && ApRunData.TryGetPlayerState(
                runState,
                message.OwnerNetId,
                out ApPlayerRunState ownerState
            )
            && ownerState.Participation == ApParticipationKind.OwnApSlot
            && ownerState.SlotSettings?.IsDeathLinkEnabled == true;
    }

    private static void SendLocalAuthorizedDeathLink(
        Guid eventId,
        ulong playerNetId,
        string characterName,
        string floorCause)
    {
        DeathLinkService? deathLinkController = ArchipelagoClient.DeathLinkController;
        if (!ArchipelagoClient.IsConnected || deathLinkController == null)
        {
            LogUtility.Warn(
                $"Discarded host-authorized DeathLink {eventId} for {playerNetId}; that AP "
                    + "connection is unavailable."
            );
            return;
        }

        string apPlayerName = ArchipelagoClient.PlayerName ?? "AP player";
        string cause = $"{apPlayerName} ({characterName}) was Slain on {floorCause}";
        try
        {
            var deathLink = new DeathLink(apPlayerName, cause);
            // Match the SDK's wire timestamp round-trip, including its subsecond precision.
            // Remember every send, not just the SDK's last-send entry, across AP reconnects.
            long wireTimestampTicks = UnixTimeConverter.UnixTimeStampToDateTime(
                deathLink.Timestamp.ToUnixTimeStamp()).Ticks;
            lock (StateLock)
                EventLedger.RecordSent(deathLink.Source, wireTimestampTicks);
            deathLinkController.SendDeathLink(deathLink);
            LogUtility.Info(
                $"Sent host-authorized DeathLink {eventId} for player {playerNetId}."
            );
        }
        catch (Exception ex)
        {
            LogUtility.Error(
                $"Discarded host-authorized DeathLink {eventId} for {playerNetId}: "
                    + ex.Message
            );
        }
    }

    private static bool ShouldSuppressOutgoing(ulong playerNetId, out string reason)
    {
        lock (StateLock)
        {
            return EventLedger.ShouldSuppressOutgoing(playerNetId, DateTime.UtcNow, out reason);
        }
    }

    private static void CompleteInboundAdmission(Guid eventId)
    {
        bool hasPending;
        lock (StateLock)
        {
            if (_inboundActionInFlight == eventId)
                _inboundActionInFlight = null;
            hasPending = PendingInbound.Count > 0;
        }

        if (hasPending)
            EnsureProcessFrameHook();
        else
            UnhookProcessFrame();
    }

    private static bool EnsureProcessFrameHook()
    {
        if (_processFrameHooked)
            return true;
        if (Engine.GetMainLoop() is not SceneTree sceneTree)
            return false;

        _sceneTree = sceneTree;
        _sceneTree.ProcessFrame += ProcessPendingInbound;
        _processFrameHooked = true;
        return true;
    }

    private static void UnhookProcessFrame()
    {
        if (_processFrameHooked && _sceneTree != null)
            _sceneTree.ProcessFrame -= ProcessPendingInbound;
        _sceneTree = null;
        _processFrameHooked = false;
    }
}
