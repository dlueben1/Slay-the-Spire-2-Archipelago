using System.Text.Json;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Converters;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Networking.ManagedActions;

namespace StS2AP.Utils;

/// <summary>
/// Lets each AP owner retain external DeathLinks until a stable combat play-phase boundary, then
/// submits them through the native action synchronizer. Local owners also report their own deaths.
/// </summary>
public static class DeathLinkMultiplayer
{
    private sealed class PendingDeathLink
    {
        public Guid RunId { get; init; }
        public Guid EventId { get; init; }
        public ulong OwnerNetId { get; init; }
        public long TimestampTicks { get; init; }
        public string Source { get; init; } = string.Empty;
        public string? Cause { get; init; }
    }

    private const string CombatActionKey = "death_link_combat_damage";
    private static readonly object StateLock = new();

    private static readonly Queue<PendingDeathLink> PendingInbound = new();
    private static readonly DeathLinkEventLedger EventLedger = new();
    private static readonly HashSet<Guid> HandledInboundEvents = new();

    private static readonly RitsuLibManagedNetActionDescriptor<DeathLinkActionMessage>
        CombatActionDescriptor = new(
            ModuleId: ModEntry.ModId,
            ActionKey: CombatActionKey,
            Serialize: static message => JsonSerializer.SerializeToUtf8Bytes(message),
            Deserialize: DeserializeActionMessage,
            Execute: ExecuteDamageAction,
            ActionType: GameActionType.CombatPlayPhaseOnly
        );
    private static bool _initialized;
    private static SceneTree? _sceneTree;
    private static bool _processFrameHooked;
    private static Guid? _inboundActionInFlight;
    private static Guid? _lastBlockedInboundEvent;
    private static string? _lastInboundBlockReason;

    public static void Initialize()
    {
        if (_initialized)
            return;

        RitsuLibManagedNetActions.Register(CombatActionDescriptor);
        _initialized = true;
    }

    public static void EndRun()
    {
        lock (StateLock)
        {
            PendingInbound.Clear();
            HandledInboundEvents.Clear();
            EventLedger.Clear();
            _inboundActionInFlight = null;
            _lastBlockedInboundEvent = null;
            _lastInboundBlockReason = null;
        }
        UnhookProcessFrame();
    }

    /// <summary>
    /// Queues one AP SDK callback on its local owner. The callback belongs only to that connected
    /// player, even when other players use the same AP slot.
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
    /// this callback, but only the process that owns the dead player and their AP connection sends.
    /// </summary>
    public static void PlayerDied(Player player)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.DeathLink)
            || !LocalContext.IsMe(player)
            || !TryGetLocalOwnSlotContext(
                out RunState runState,
                out _,
                out Player localOwner,
                out _
            )
            || localOwner.NetId != player.NetId
            || runState.CurrentRoom?.IsVictoryRoom == true
        )
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
        SendLocalDeathLink(Guid.NewGuid(), player.NetId, characterName, floorCause);
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

        var pending = new PendingDeathLink
        {
            RunId = shared.RunId,
            EventId = Guid.NewGuid(),
            OwnerNetId = owner.NetId,
            TimestampTicks = timestampTicks,
            Source = source,
            Cause = cause,
        };

        lock (StateLock)
        {
            // A duplicate SDK callback gets a different local GUID. Deduplicate its AP identity
            // per recipient, never per shared AP slot or across all recipients.
            if (!EventLedger.TryAcceptInbound(
                    pending.OwnerNetId, pending.Source, pending.TimestampTicks))
            {
                LogUtility.Info($"Ignored duplicate DeathLink for AP owner {pending.OwnerNetId}.");
                return;
            }
            PendingInbound.Enqueue(pending);
        }

        LogUtility.Info(
            $"AP owner {pending.OwnerNetId} queued incoming DeathLink {pending.EventId}; "
                + $"{DescribeAdmissionState()}."
        );
        if (!EnsureProcessFrameHook())
        {
            LogUtility.Error(
                $"Incoming DeathLink {pending.EventId} is pending, but the Godot process-frame "
                    + "signal is unavailable."
            );
            return;
        }
        ProcessPendingInbound();
    }

    private static void ProcessPendingInbound()
    {
        PendingDeathLink pending;
        lock (StateLock)
        {
            if (_inboundActionInFlight.HasValue || PendingInbound.Count == 0)
            {
                if (!_inboundActionInFlight.HasValue)
                    UnhookProcessFrame();
                return;
            }
            pending = PendingInbound.Peek();
        }

        if (!TryValidatePendingInbound(
                pending,
                out Player slotOwner,
                out ArchipelagoSettings settings
            ))
        {
            lock (StateLock)
                PendingInbound.Dequeue();
            ClearAdmissionBlocker(pending.EventId);
            LogUtility.Warn(
                $"Consumed stale incoming DeathLink {pending.EventId} before admission."
            );
            ProcessPendingInbound();
            return;
        }

        if (!CanAdmitCombatAction(out string blockedReason))
        {
            LogAdmissionBlocked(pending.EventId, blockedReason);
            return;
        }

        var message = new DeathLinkActionMessage
        {
            RunId = pending.RunId,
            EventId = pending.EventId,
            SlotOwnerNetId = pending.OwnerNetId,
            DamagePercent = settings.DeathLinkDamagePercent,
            Source = pending.Source,
            Cause = pending.Cause,
            Targets = BuildTargetPlans(slotOwner, settings.DeathLinkDamagePercent),
        };

        lock (StateLock)
            _inboundActionInFlight = pending.EventId;

        bool requested;
        try
        {
            requested = RitsuLibManagedNetActions.Request(
                RunManager.Instance,
                CombatActionDescriptor,
                message,
                slotOwner.NetId
            );
        }
        catch (Exception ex)
        {
            lock (StateLock)
                _inboundActionInFlight = null;
            LogAdmissionBlocked(
                pending.EventId,
                $"managed-action request threw {ex.GetType().Name}: {ex.Message}"
            );
            return;
        }

        if (!requested)
        {
            lock (StateLock)
                _inboundActionInFlight = null;
            LogAdmissionBlocked(
                pending.EventId,
                "managed-action request returned false; transport, peer capability, or run "
                    + "context is not ready"
            );
            return;
        }

        lock (StateLock)
            PendingInbound.Dequeue();
        ClearAdmissionBlocker(pending.EventId);

        LogUtility.Info(
            $"AP owner {pending.OwnerNetId} submitted queued DeathLink {pending.EventId} "
                + "through the native combat action synchronizer; "
                + $"{DescribeAdmissionState()}."
        );
    }

    private static bool CanAdmitCombatAction(out string blockedReason)
    {
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
            // enqueue a CombatPlayPhaseOnly action. A DeathLink received anywhere else stays at
            // the head of the FIFO until this phase is stable and the native queues are empty.
            return true;
        }

        blockedReason = "DeathLinks wait for the combat play phase "
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
                Damage = slotOwner.Creature.IsDead ? 0 : damage,
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
        bool localOwnerOwnsAdmission = LocalContext.IsMe(context.Player);
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

            var plans = new List<(Player Target, int Damage)>();
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
                if (localExpectedDamage != plan.Damage)
                {
                    LogUtility.Warn(
                        $"DeathLink {message.EventId} observed raw-damage divergence for "
                            + $"{target.NetId}: owner={plan.Damage}, local={localExpectedDamage}; "
                            + "applying the owner-authored amount."
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
                        // Presentation is secondary to the owner-authored damage action.
                        LogUtility.Error(
                            $"Could not show DeathLink {message.EventId} notification: "
                                + ex.Message
                        );
                    }
                }
                plans.Add((target, plan.Damage));
            }

            // Mark every target before changing the first one. A death callback can synchronously
            // affect another target, and all deaths caused by this incoming event must be silent.
            lock (StateLock)
            {
                foreach ((Player target, int damage) in plans)
                {
                    EventLedger.BeginDamage(
                        target.NetId,
                        lethal: damage >= target.Creature.CurrentHp,
                        DateTime.UtcNow
                    );
                }
            }

            try
            {
                foreach ((Player target, int damage) in plans)
                {
                    int hpBefore = target.Creature.CurrentHp;
                    LogUtility.Info(
                        $"Applying native-ordered DeathLink {message.EventId} to {target.NetId}: "
                            + $"{damage} raw unblockable damage at {hpBefore} HP."
                    );
                    if (damage > 0)
                    {
                        await CreatureCmd.Damage(
                            context.PlayerChoiceContext,
                            target.Creature,
                            damage,
                            ValueProp.Unblockable | ValueProp.Unpowered,
                            null,
                            null
                        );
                    }
                    LogUtility.Info(
                        $"Completed DeathLink {message.EventId} for {target.NetId}: "
                            + $"{hpBefore}->{target.Creature.CurrentHp} HP after damage hooks."
                    );
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
            if (localOwnerOwnsAdmission)
                CompleteInboundAdmission(message.EventId);
        }
    }

    private static bool TryValidatePendingInbound(
        PendingDeathLink pending,
        out Player owner,
        out ArchipelagoSettings settings)
    {
        owner = null!;
        settings = null!;
        if (pending.RunId == Guid.Empty
            || pending.EventId == Guid.Empty
            || pending.TimestampTicks <= 0 || pending.TimestampTicks > DateTime.MaxValue.Ticks
            || pending.Source.Length > 1024
            || pending.Cause?.Length > 2048
            || !TryGetLocalOwnSlotContext(
                out RunState current,
                out ApRunSharedState shared,
                out Player currentOwner,
                out ArchipelagoSettings ownerSettings
            )
            || shared.RunId != pending.RunId
            || currentOwner.NetId != pending.OwnerNetId
            || ownerSettings.DeathLinkDamagePercent is < 0 or > 100)
        {
            return false;
        }

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
        if (actionOwner.NetId != message.SlotOwnerNetId
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
            if (target == null || plan.Damage < 0 || plan.Damage > target.Creature.MaxHp)
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

    private static void SendLocalDeathLink(
        Guid eventId,
        ulong playerNetId,
        string characterName,
        string floorCause)
    {
        DeathLinkService? deathLinkController = ArchipelagoClient.DeathLinkController;
        if (!ArchipelagoClient.IsConnected || deathLinkController == null)
        {
            LogUtility.Warn(
                $"Discarded local-owner DeathLink {eventId} for {playerNetId}; that AP "
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
                $"Sent local-owner DeathLink {eventId} for player {playerNetId}."
            );
        }
        catch (Exception ex)
        {
            LogUtility.Error(
                $"Discarded local-owner DeathLink {eventId} for {playerNetId}: "
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
