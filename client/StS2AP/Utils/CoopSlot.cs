using StS2AP.Data;

namespace StS2AP.Utils;

/// <summary>
/// Player number and name prefix from the removed shared-slot co-op. Connected slots are
/// always Player 1 now; persisted identities and multiplayer peers still carry the number.
/// </summary>
internal static class CoopSlot
{
    public static int PlayerNumber => ArchipelagoClient.Settings?.PlayerNumber ?? 1;

    public static string Name(string name) => ArchipelagoIdCodec.PlayerName(name, PlayerNumber);
    public static string StorageKey(string key) => Name(key);
}
