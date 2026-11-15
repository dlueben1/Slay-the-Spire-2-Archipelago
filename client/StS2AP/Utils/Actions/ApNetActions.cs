using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Utils;

/// <summary>
/// Describes one kind of AP multiplayer action that runs through MegaCrit's native action queue.
/// The owning peer serializes the message; every peer deserializes and executes it in queue order.
/// </summary>
public sealed record ApNetActionDescriptor<T>(
    string ActionKey,
    Func<T, byte[]> Serialize,
    Func<ReadOnlySpan<byte>, T> Deserialize,
    Func<ApNetActionContext<T>, Task> Execute,
    GameActionType ActionType
);

public readonly record struct ApNetActionContext<T>(
    T Message,
    Player Player,
    GameActionPlayerChoiceContext PlayerChoiceContext
);

/// <summary>
/// Queues AP actions as a net action type of our own, which the game registers like its own
/// action types because the AP assembly is a mod assembly.
/// </summary>
/// <remarks>
/// These actions used RitsuLib's managed actions until 2.1.0. RitsuLib 0.6.13 sends those through
/// the game's message serializers and relies on Harmony prefixes to skip the action-type lookup
/// for its runtime-generated carrier type. Once those small serializers are hot, the JIT inlines
/// them into the callers and the prefixes stop running, so every later request failed with
/// "The given key 'STS2RitsuLib.Runtime.ManagedNetActionCarrier' was not present", usually on
/// some peers and not others. A registered type needs no serialization patch at all.
/// </remarks>
public static class ApNetActions
{
    /// <summary>Upper bound on one action's payload; also guards reads of malformed packets.</summary>
    public const int MaxPayloadBytes = 64 * 1024;

    private static readonly Dictionary<string, Registration> Registrations = new(StringComparer.Ordinal);

    public static void Register<T>(ApNetActionDescriptor<T> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ActionKey);
        if (descriptor.ActionType == GameActionType.None || !Enum.IsDefined(descriptor.ActionType))
            throw new ArgumentException($"AP action {descriptor.ActionKey} needs a native action type.");

        if (Registrations.TryGetValue(descriptor.ActionKey, out Registration? existing))
        {
            if (ReferenceEquals(existing.Descriptor, descriptor))
                return;
            throw new InvalidOperationException($"AP action key {descriptor.ActionKey} is already registered.");
        }

        Registrations[descriptor.ActionKey] = new Registration(
            descriptor,
            descriptor.ActionType,
            async (action, choiceContext) =>
            {
                T message = descriptor.Deserialize(action.Payload);
                await descriptor.Execute(new ApNetActionContext<T>(message, action.Player, choiceContext));
            }
        );
    }

    /// <summary>
    /// Asks the host to enqueue <paramref name="message"/> for the local player. Returns true when
    /// the request was handed to the network, which does not mean it has executed yet.
    /// </summary>
    public static bool Request<T>(ApNetActionDescriptor<T> descriptor, T message, ulong ownerNetId)
    {
        if (!Registrations.TryGetValue(descriptor.ActionKey, out Registration? registration)
            || !ReferenceEquals(registration.Descriptor, descriptor))
        {
            throw new InvalidOperationException($"AP action {descriptor.ActionKey} was not registered.");
        }

        RunManager? manager = RunManager.Instance;
        var net = manager?.NetService;
        if (manager == null || net == null || manager.DebugOnlyGetState() is not RunState state)
            return false;
        if (net.Type != NetGameType.Singleplayer && !net.IsConnected)
            return false;

        // The host attributes every request to the peer that sent it.
        if (ownerNetId != net.NetId)
            return false;
        Player? owner = state.Players.FirstOrDefault(player => player.NetId == ownerNetId);
        if (owner == null)
            return false;

        byte[] payload = descriptor.Serialize(message);
        if (payload.Length > MaxPayloadBytes)
        {
            LogUtility.Warn(
                $"Refusing AP action {descriptor.ActionKey}: its payload is {payload.Length} bytes, "
                    + $"over the {MaxPayloadBytes}-byte limit."
            );
            return false;
        }

        manager.ActionQueueSynchronizer.RequestEnqueue(
            new ArchipelagoGameAction(owner, descriptor.ActionKey, descriptor.ActionType, payload)
        );
        return true;
    }

    internal static Task Execute(ArchipelagoGameAction action, GameActionPlayerChoiceContext choiceContext)
    {
        if (!Registrations.TryGetValue(action.ActionKey, out Registration? registration))
            throw new InvalidOperationException($"Received unknown AP action {action.ActionKey}.");
        if (registration.ActionType != action.ActionType)
        {
            throw new InvalidOperationException(
                $"AP action {action.ActionKey} arrived as {action.ActionType}, "
                    + $"but is registered as {registration.ActionType}."
            );
        }
        return registration.Execute(action, choiceContext);
    }

    private sealed record Registration(
        object Descriptor,
        GameActionType ActionType,
        Func<ArchipelagoGameAction, GameActionPlayerChoiceContext, Task> Execute
    );
}

/// <summary>
/// Wire form of an <see cref="ArchipelagoGameAction"/>. The game finds this type when it builds its
/// net action table, so every peer gives it the same type id. Keep the name unique among net
/// action types: the game orders the table by type name.
/// </summary>
public sealed class ArchipelagoNetAction : INetAction
{
    public string ActionKey { get; private set; } = string.Empty;
    public GameActionType ActionType { get; private set; }
    public byte[] Payload { get; private set; } = [];

    // The game creates incoming actions through the parameterless constructor, then deserializes.
    public ArchipelagoNetAction() { }

    public ArchipelagoNetAction(string actionKey, GameActionType actionType, byte[] payload)
    {
        ActionKey = actionKey;
        ActionType = actionType;
        Payload = payload;
    }

    public GameAction ToGameAction(Player player) =>
        new ArchipelagoGameAction(player, ActionKey, ActionType, Payload);

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(ActionKey);
        writer.WriteEnum(ActionType);
        writer.WriteInt(Payload.Length);
        writer.WriteBytes(Payload, Payload.Length);
    }

    public void Deserialize(PacketReader reader)
    {
        ActionKey = reader.ReadString();
        ActionType = reader.ReadEnum<GameActionType>();
        int length = reader.ReadInt();
        if (length < 0 || length > ApNetActions.MaxPayloadBytes)
            throw new InvalidOperationException($"AP action {ActionKey} has an invalid payload length {length}.");
        Payload = new byte[length];
        reader.ReadBytes(Payload, length);
    }

    public override string ToString() =>
        $"ArchipelagoNetAction {ActionKey} type {ActionType} payload {Payload.Length}";
}

/// <summary>Queued form of an AP action; runs the registered executor on every peer.</summary>
public sealed class ArchipelagoGameAction(
    Player player,
    string actionKey,
    GameActionType actionType,
    byte[] payload
) : GameAction
{
    public Player Player { get; } = player;
    public string ActionKey { get; } = actionKey;
    public byte[] Payload { get; } = payload;

    public override ulong OwnerId => Player.NetId;
    public override GameActionType ActionType { get; } = actionType;

    protected override Task ExecuteAction() =>
        ApNetActions.Execute(this, new GameActionPlayerChoiceContext(this));

    public override INetAction ToNetAction() => new ArchipelagoNetAction(ActionKey, ActionType, Payload);

    public override string ToString() =>
        $"ArchipelagoGameAction {ActionKey} player {OwnerId} type {ActionType}";
}
