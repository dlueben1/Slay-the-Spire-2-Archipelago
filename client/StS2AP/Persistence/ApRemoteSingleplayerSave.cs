using Archipelago.MultiClient.Net.Enums;
using Newtonsoft.Json.Linq;
using StS2AP.Utils;

namespace StS2AP.Persistence;

/// <summary>
/// Stores one opaque latest checkpoint per AP slot and character in DataStorage.
/// Local checkpoint-bank persistence remains independent from this best-effort remote copy.
/// </summary>
internal static class ApRemoteSingleplayerSave
{
    internal static bool IsEnabled =>
        ArchipelagoClient.LocalSettings.Value.EnableRemoteSingleplayerSaves;

    internal static void Upload(string character, string payload)
    {
        try
        {
            if (!IsEnabled) return;

            var session = ArchipelagoClient.Session;
            if (!ArchipelagoClient.IsConnected || session == null)
            {
                LogUtility.Warn(
                    $"Remote AP checkpoint was not uploaded for {character}: the AP slot is disconnected."
                );
                return;
            }

            string storageKey = RemoteSingleplayerSaveKey.For(character);
            session.DataStorage[Scope.Slot, storageKey] = payload;
            LogUtility.Info(
                $"Queued remote AP checkpoint upload: character={character}, compressedBytes={payload.Length}"
            );
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Failed to queue remote AP checkpoint upload for {character}: {ex}");
        }
    }

    internal static async Task<string> Download(string character)
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Enable AP Server Cloud Saves in the mod settings first.");

        var session = ArchipelagoClient.Session
            ?? throw new InvalidOperationException("Connect to the AP slot before loading its remote save.");
        if (!ArchipelagoClient.IsConnected)
            throw new InvalidOperationException("The AP slot is not connected.");

        string storageKey = RemoteSingleplayerSaveKey.For(character);
        var storage = session.DataStorage[Scope.Slot, storageKey];
        storage.Initialize(new JValue(string.Empty));
        string? payload = await storage.GetAsync<string>();

        if (!ReferenceEquals(session, ArchipelagoClient.Session) || !ArchipelagoClient.IsConnected)
            throw new InvalidOperationException("The connected AP slot changed while downloading the remote save.");
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidDataException("No remote save exists for this character.");

        LogUtility.Info(
            $"Downloaded remote AP checkpoint: character={character}, compressedBytes={payload.Length}"
        );
        return payload;
    }
}
