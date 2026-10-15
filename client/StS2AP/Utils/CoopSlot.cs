using StS2AP.Data;

namespace StS2AP.Utils;

/// <summary>The selected numbered player is frozen in authenticated slot settings.</summary>
internal static class CoopSlot
{
    public static int PlayerNumber => ArchipelagoClient.Settings?.PlayerNumber
        ?? ArchipelagoClient.LocalSettings.Value.MultiplayerPlayerNumber;

    public static bool Owns(long id) => ArchipelagoIdCodec.GetPlayerNumber(id) == PlayerNumber;
    public static string Name(string name) => ArchipelagoIdCodec.PlayerName(name, PlayerNumber);
    public static long Location(long id) => ArchipelagoIdCodec.ForPlayer(id, PlayerNumber);
    public static string StorageKey(string key) => Name(key);
}
