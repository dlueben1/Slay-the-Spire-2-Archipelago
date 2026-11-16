using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Utils;

/// <summary>
/// Describes one kind of AP multiplayer message carried by <see cref="ArchipelagoNetMessage"/>.
/// The handler runs on the game's main thread, from the run's network update.
/// </summary>
public sealed record ApNetMessageDescriptor<T>(
    string MessageKey,
    Func<T, byte[]> Serialize,
    Func<ReadOnlySpan<byte>, T> Deserialize,
    Action<ApNetMessageContext<T>> Handle
);

/// <summary>
/// One received AP message. <see cref="SenderNetId"/> is the id the sending game wrote into the
/// packet, as for the game's own messages: the host relays with the original sender's id.
/// </summary>
public readonly record struct ApNetMessageContext<T>(T Message, ulong SenderNetId);

/// <summary>
/// AP's own multiplayer messages, as a native message type of ours: AP's only network transport
/// besides queued actions (RitsuLib managed actions). Today it carries each owner's progress
/// checkpoint to the host (ApRunData).
/// </summary>
/// <remarks>
/// The game registers <see cref="ArchipelagoNetMessage"/> like its own message types because the
/// AP assembly is a mod assembly. Native messages
/// travel on the game's reliable channel, ordered with its own messages and actions. Clients can
/// only send to the host, and the host sends to one client or to every client ready for
/// broadcasts. Nothing is relayed automatically: a host that relays a client's message does so
/// explicitly, after validating it.
/// </remarks>
public static class ApNetMessages
{
    /// <summary>Upper bound on one message's payload; also guards reads of malformed packets.</summary>
    public const int MaxPayloadBytes = 256 * 1024;

    private static readonly Dictionary<string, Registration> Registrations = new(StringComparer.Ordinal);
    private static INetGameService? _attached;

    public static void Register<T>(ApNetMessageDescriptor<T> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.MessageKey);
        if (Registrations.TryGetValue(descriptor.MessageKey, out Registration? existing))
        {
            if (ReferenceEquals(existing.Descriptor, descriptor))
                return;
            throw new InvalidOperationException($"AP message key {descriptor.MessageKey} is already registered.");
        }

        Registrations[descriptor.MessageKey] = new Registration(
            descriptor,
            (payload, senderNetId) =>
                descriptor.Handle(new ApNetMessageContext<T>(descriptor.Deserialize(payload), senderNetId))
        );
    }

    /// <summary>Client only: sends <paramref name="message"/> to the host.</summary>
    public static bool SendToHost<T>(ApNetMessageDescriptor<T> descriptor, T message) =>
        TrySend(descriptor, message, NetGameType.Client, peerNetId: null);

    /// <summary>Host only: sends <paramref name="message"/> to one client.</summary>
    public static bool SendToPeer<T>(ApNetMessageDescriptor<T> descriptor, ulong peerNetId, T message) =>
        TrySend(descriptor, message, NetGameType.Host, peerNetId);

    /// <summary>Host only: sends <paramref name="message"/> to every client ready for broadcasts.</summary>
    public static bool Broadcast<T>(ApNetMessageDescriptor<T> descriptor, T message) =>
        TrySend(descriptor, message, NetGameType.Host, peerNetId: null);

    private static bool TrySend<T>(
        ApNetMessageDescriptor<T> descriptor,
        T message,
        NetGameType requiredRole,
        ulong? peerNetId)
    {
        if (!Registrations.TryGetValue(descriptor.MessageKey, out Registration? registration)
            || !ReferenceEquals(registration.Descriptor, descriptor))
        {
            throw new InvalidOperationException($"AP message {descriptor.MessageKey} was not registered.");
        }

        INetGameService? net = RunManager.Instance?.NetService;
        if (net == null || net.Type != requiredRole || !net.IsConnected)
            return false;

        byte[] payload = descriptor.Serialize(message);
        if (payload.Length > MaxPayloadBytes)
        {
            LogUtility.Warn(
                $"Refusing AP message {descriptor.MessageKey}: its payload is {payload.Length} bytes, "
                    + $"over the {MaxPayloadBytes}-byte limit."
            );
            return false;
        }

        var wire = new ArchipelagoNetMessage(descriptor.MessageKey, payload);
        if (peerNetId is ulong peer)
            net.SendMessage(wire, peer);
        else
            net.SendMessage(wire);
        return true;
    }

    /// <summary>
    /// Listens on a run's net service. Called when the run sets up its synchronizers, before the
    /// game releases the messages it buffered while loading, so none arrive without a handler.
    /// </summary>
    internal static void Attach(INetGameService netService)
    {
        Detach();
        netService.RegisterMessageHandler<ArchipelagoNetMessage>(OnMessage);
        _attached = netService;
    }

    internal static void Detach()
    {
        _attached?.UnregisterMessageHandler<ArchipelagoNetMessage>(OnMessage);
        _attached = null;
    }

    private static void OnMessage(ArchipelagoNetMessage message, ulong senderNetId)
    {
        if (!Registrations.TryGetValue(message.MessageKey, out Registration? registration))
        {
            LogUtility.Error($"Received unknown AP message {message.MessageKey} from {senderNetId}.");
            return;
        }

        try
        {
            registration.Dispatch(message.Payload, senderNetId);
        }
        catch (Exception ex)
        {
            LogUtility.Error($"AP message {message.MessageKey} from {senderNetId} failed: {ex}");
        }
    }

    private sealed record Registration(object Descriptor, Action<byte[], ulong> Dispatch);
}

/// <summary>
/// Wire form of every <see cref="ApNetMessages"/> message. The game finds this type when it builds
/// its message table, so every peer with this mod gives it the same id. Keep the name unique among
/// message types: the game orders the table by type name.
/// </summary>
public sealed class ArchipelagoNetMessage : INetMessage
{
    public string MessageKey { get; private set; } = string.Empty;
    public byte[] Payload { get; private set; } = [];

    // The host relays explicitly, after validating, never on receipt.
    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    // Held while the game loads a run, like its own run messages, and released at launch.
    public bool ShouldBuffer => true;

    // The game creates incoming messages through the parameterless constructor, then deserializes.
    public ArchipelagoNetMessage() { }

    public ArchipelagoNetMessage(string messageKey, byte[] payload)
    {
        MessageKey = messageKey;
        Payload = payload;
    }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(MessageKey);
        writer.WriteInt(Payload.Length);
        writer.WriteBytes(Payload, Payload.Length);
    }

    public void Deserialize(PacketReader reader)
    {
        MessageKey = reader.ReadString();
        int length = reader.ReadInt();
        if (length < 0 || length > ApNetMessages.MaxPayloadBytes)
            throw new InvalidOperationException($"AP message {MessageKey} has an invalid payload length {length}.");
        Payload = new byte[length];
        reader.ReadBytes(Payload, length);
    }

    public override string ToString() => $"ArchipelagoNetMessage {MessageKey} payload {Payload.Length}";
}
