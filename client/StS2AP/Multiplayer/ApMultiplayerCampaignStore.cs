using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;
using StS2AP.Extensions;
using StS2AP.Utils;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StS2AP.Multiplayer;

/// <summary>
/// Keeps floor recovery and AP checkpoint saves in a local campaign bank while leaving MegaCrit's
/// current_run_mp.save as the one active save consumed by the native load lobby.
/// </summary>
public static class ApMultiplayerCampaignStore
{
    private const int MetadataSchemaVersion = 2;
    private const string CampaignRootName = "ArchipelagoMultiplayerCampaigns";
    private const string MetadataFileName = "metadata.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string? _selectedCampaignId;
    private static readonly SemaphoreSlim SaveLock = new(1, 1);

    public static bool IsStartingNewCampaign { get; private set; }

    internal enum CampaignStatus
    {
        Active,
        Completed,
        Archived,
    }

    internal enum SaveKind
    {
        FloorRecovery,
        ApCheckpoint,
    }

    internal sealed class CampaignSnapshot
    {
        public string FileName { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public DateTimeOffset SavedAtUtc { get; set; }
        public int Act { get; set; }
        public int CompletedFloorCount { get; set; }
    }

    internal sealed class CampaignMetadata
    {
        public int SchemaVersion { get; set; } = MetadataSchemaVersion;
        public string CampaignId { get; set; } = string.Empty;
        public Guid RunId { get; set; }
        public CampaignStatus Status { get; set; } = CampaignStatus.Active;
        public string ApRoomSeed { get; set; } = string.Empty;
        public int ApTeamId { get; set; }
        public int ApSlotId { get; set; }
        public int PlayerNumber { get; set; } = 1;
        public string ApSlotName { get; set; } = string.Empty;
        public string HostCharacterId { get; set; } = string.Empty;
        public long? HostCharacterOffset { get; set; }
        public ulong HostNetId { get; set; }
        public List<CampaignRosterEntry> Roster { get; set; } = new();
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset LastSavedAtUtc { get; set; }
        public int Act { get; set; }
        public int CompletedFloorCount { get; set; }
        public CampaignSnapshot? FloorRecovery { get; set; }
        public CampaignSnapshot? ApCheckpoint { get; set; }
    }

    internal sealed class CampaignRosterEntry
    {
        public ulong NetId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string CharacterId { get; set; } = string.Empty;
        public ApParticipationKind Participation { get; set; }
        public string? ApRoomSeed { get; set; }
        public int? ApTeamId { get; set; }
        public int? ApSlotId { get; set; }
        public int PlayerNumber { get; set; } = 1;
    }

    internal sealed record CampaignEntry(
        string CampaignId,
        CampaignMetadata? Metadata,
        string? Error)
    {
        public bool IsUsable => Metadata != null && Error == null;
    }

    public static void BeginNewCampaign()
    {
        _selectedCampaignId = null;
        IsStartingNewCampaign = true;
    }

    public static void CancelPendingNewCampaign() => IsStartingNewCampaign = false;

    internal static IReadOnlyList<CampaignEntry> ListCampaigns()
    {
        var result = new List<CampaignEntry>();
        string root = GetCampaignRoot();
        if (!Directory.Exists(root))
            return result;

        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            string campaignId = Path.GetFileName(directory);
            if (!Guid.TryParseExact(campaignId, "N", out _))
                continue;

            try
            {
                CampaignMetadata metadata = ReadMetadata(campaignId);
                string? error = ValidateStoredCampaign(metadata, requirePayload: metadata.Status == CampaignStatus.Active);
                result.Add(new CampaignEntry(campaignId, metadata, error));
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                result.Add(new CampaignEntry(campaignId, null, ex.GetBaseException().Message));
            }
        }

        return result
            .OrderBy(entry => entry.Metadata?.Status != CampaignStatus.Active)
            .ThenByDescending(entry => entry.Metadata?.LastSavedAtUtc ?? DateTimeOffset.MinValue)
            .ToArray();
    }

    internal static bool IsCurrentApIdentity(CampaignMetadata metadata) =>
        TryGetCurrentIdentity(out string roomSeed, out int teamId, out int slotId)
        && string.Equals(metadata.ApRoomSeed, roomSeed, StringComparison.Ordinal)
        && metadata.ApTeamId == teamId
        && metadata.ApSlotId == slotId
        && metadata.PlayerNumber == CoopSlot.PlayerNumber;

    internal static bool TryGetActiveCampaignForRoster(
        StartRunLobby lobby,
        out CampaignMetadata metadata)
    {
        IReadOnlyList<(ulong NetId, string CharacterId)> roster =
            BetaMainCompatibility.GetLobbyPlayerCharacters(lobby);
        metadata = ListCampaigns()
            .Where(entry => entry.IsUsable && entry.Metadata != null)
            .Select(entry => entry.Metadata!)
            .FirstOrDefault(candidate =>
                candidate.Status == CampaignStatus.Active
                && IsCurrentApIdentity(candidate)
                && HasSameRoster(candidate.Roster, roster))!;
        return metadata != null;
    }

    internal static void ActivateCampaign(CampaignMetadata metadata, SaveKind kind)
    {
        string? validationError = ValidateStoredCampaign(metadata, requirePayload: true);
        if (validationError != null)
            throw new InvalidDataException(validationError);
        if (metadata.Status != CampaignStatus.Active)
            throw new InvalidOperationException("Only active campaigns can be continued.");
        if (!IsCurrentApIdentity(metadata))
            throw new InvalidOperationException("This campaign belongs to a different Archipelago slot.");

        string? saveError = GetSnapshotError(metadata, kind);
        if (saveError != null)
            throw new InvalidDataException(saveError);
        CampaignSnapshot snapshot = GetSnapshot(metadata, kind)!;
        AtomicCopy(GetSnapshotPath(metadata.CampaignId, snapshot), GetActiveSavePath());
        _selectedCampaignId = metadata.CampaignId;
        IsStartingNewCampaign = false;
        LogUtility.Info(
            $"Activated AP multiplayer campaign {metadata.CampaignId}: "
                + $"kind={kind}, act={snapshot.Act}, floors={snapshot.CompletedFloorCount}, "
                + $"character={metadata.HostCharacterId}, runId={metadata.RunId}"
        );
    }

    internal static void ArchiveCampaign(string campaignId)
    {
        CampaignMetadata metadata = ReadMetadata(campaignId);
        metadata.Status = CampaignStatus.Archived;
        WriteMetadata(metadata);
        if (string.Equals(_selectedCampaignId, campaignId, StringComparison.Ordinal))
        {
            DeleteCanonicalSaveIfPresent();
            _selectedCampaignId = null;
        }
    }

    internal static void DeleteCampaign(string campaignId)
    {
        string directory = GetCampaignDirectory(campaignId);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        if (string.Equals(_selectedCampaignId, campaignId, StringComparison.Ordinal))
        {
            DeleteCanonicalSaveIfPresent();
            _selectedCampaignId = null;
        }
    }

    /// <summary>Imports the pre-feature canonical host save once so it is not stranded.</summary>
    internal static void ImportCanonicalSave()
    {
        string activeSavePath = GetActiveSavePath();
        if (!File.Exists(activeSavePath))
            return;

        ReadSaveResult<SerializableRun> read = SaveManager.Instance
            .LoadAndCanonicalizeMultiplayerRunSave(PlatformUtil.GetLocalPlayerId(GetVanillaPlatform()));
        if (!read.Success || read.SaveData == null)
            return;

        ulong localNetId = PlatformUtil.GetLocalPlayerId(read.SaveData.PlatformType);
        RunState importedRun = RunState.FromSerializable(read.SaveData);
        Player? hostPlayer = importedRun.Players.FirstOrDefault(player => player.NetId == localNetId);
        SerializablePlayer? hostSave = read.SaveData.Players.FirstOrDefault(
            player => player.NetId == localNetId
        );
        if (hostPlayer == null || hostSave == null
            || !ApRunData.TryGetSharedState(importedRun, out ApRunSharedState shared)
            || !ApRunData.TryGetPlayerState(importedRun, localNetId, out ApPlayerRunState hostState)
            || hostState.Participation != ApParticipationKind.OwnApSlot
            || string.IsNullOrWhiteSpace(hostState.ApRoomSeed)
            || !hostState.ApTeamId.HasValue
            || !hostState.ApSlotId.HasValue)
        {
            LogUtility.Warn(
                "The existing multiplayer save was not imported because its embedded AP host identity could not be verified."
            );
            return;
        }

        string roomSeed = hostState.ApRoomSeed;
        int teamId = hostState.ApTeamId.Value;
        int slotId = hostState.ApSlotId.Value;
        string characterId = hostPlayer.getInternalName();
        List<CampaignRosterEntry> importedRoster = BuildImportedRoster(
            importedRun,
            read.SaveData.PlatformType
        );
        CampaignMetadata? existing = ListCampaigns()
            .Where(entry => entry.Metadata != null)
            .Select(entry => entry.Metadata!)
            .FirstOrDefault(metadata =>
                shared.RunId != Guid.Empty
                    ? metadata.RunId == shared.RunId
                    : string.Equals(metadata.ApRoomSeed, roomSeed, StringComparison.Ordinal)
                        && metadata.ApTeamId == teamId
                        && metadata.ApSlotId == slotId
                        && metadata.PlayerNumber == (hostState.SlotSettings?.PlayerNumber ?? 1)
                        && HasSameRoster(metadata.Roster, importedRoster));
        if (existing != null)
        {
            if (existing.Status == CampaignStatus.Active)
                _selectedCampaignId = existing.CampaignId;
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string campaignId = shared.RunId == Guid.Empty
            ? Guid.NewGuid().ToString("N")
            : shared.RunId.ToString("N");
        var metadata = new CampaignMetadata
        {
            CampaignId = campaignId,
            RunId = shared.RunId,
            Status = CampaignStatus.Active,
            ApRoomSeed = roomSeed,
            ApTeamId = teamId,
            ApSlotId = slotId,
            PlayerNumber = hostState.SlotSettings?.PlayerNumber ?? 1,
            ApSlotName = TryGetCurrentIdentity(
                    out string currentRoomSeed,
                    out int currentTeamId,
                    out int currentSlotId)
                && string.Equals(currentRoomSeed, roomSeed, StringComparison.Ordinal)
                && currentTeamId == teamId
                && currentSlotId == slotId
                    ? ArchipelagoClient.PlayerName ?? $"AP Slot {slotId}"
                    : $"AP Slot {slotId}",
            HostCharacterId = characterId,
            HostCharacterOffset = TryGetAPCharacterNumber(characterId),
            HostNetId = hostPlayer.NetId,
            Roster = importedRoster,
            CreatedAtUtc = now,
            LastSavedAtUtc = now,
            Act = read.SaveData.CurrentActIndex + 1,
            CompletedFloorCount = read.SaveData.MapPointHistory?.Sum(act => act.Count) ?? 0,
        };

        Directory.CreateDirectory(GetCampaignDirectory(campaignId));
        metadata.ApCheckpoint = StoreSnapshot(campaignId, activeSavePath, now,
            metadata.Act, metadata.CompletedFloorCount);
        WriteMetadata(metadata);
        LogUtility.Info($"Imported existing multiplayer host save as AP campaign {campaignId}");
    }

    internal static async Task SaveHostSnapshot(
        RunSaveManager saves, SerializableRun snapshot, bool isApCheckpoint)
    {
        CampaignMetadata? captured = null;
        try
        {
            captured = CaptureSaveMetadata(snapshot);
        }
        catch (Exception ex)
        {
            LogCampaignSaveFailure(ex);
        }

        // Event saves can overlap travel saves. Keep the native write and its campaign copy
        // together so a later floor cannot be mistaken for an earlier AP checkpoint.
        await SaveLock.WaitAsync();
        try
        {
            await saves.SaveRun(snapshot, isMultiplayer: true);
            if (captured != null)
            {
                try { SyncSavedSnapshot(captured, isApCheckpoint); }
                catch (Exception ex) { LogCampaignSaveFailure(ex); }
            }
        }
        finally
        {
            SaveLock.Release();
        }
    }

    private static void LogCampaignSaveFailure(Exception ex)
    {
        LogUtility.Error($"Failed to update AP multiplayer campaign saves: {ex}");
        Callable.From(() => NotificationUtility.ShowRawText(
            "The AP campaign copy could not be updated. Check the log; the native save is kept separately."
        )).CallDeferred();
    }

    internal static void MarkCurrentCampaignCompleted()
    {
        if (string.IsNullOrWhiteSpace(_selectedCampaignId))
            return;

        try
        {
            CampaignMetadata metadata = ReadMetadata(_selectedCampaignId);
            metadata.Status = CampaignStatus.Completed;
            metadata.LastSavedAtUtc = DateTimeOffset.UtcNow;
            WriteMetadata(metadata);
            LogUtility.Info($"Completed AP multiplayer campaign {metadata.CampaignId}");
            _selectedCampaignId = null;
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Failed to mark the AP multiplayer campaign completed: {ex}");
        }
    }

    internal static bool ValidateSelectedCampaignRoster(
        IReadOnlyCollection<ulong> connectedPlayerIds,
        out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(_selectedCampaignId))
            return true;

        CampaignMetadata metadata;
        try
        {
            metadata = ReadMetadata(_selectedCampaignId);
        }
        catch (Exception ex)
        {
            reason = $"The selected campaign metadata could not be read: {ex.GetBaseException().Message}";
            return false;
        }

        HashSet<ulong> originalIds = metadata.Roster.Select(player => player.NetId).ToHashSet();
        ulong[] replacements = connectedPlayerIds.Where(id => !originalIds.Contains(id)).ToArray();
        if (replacements.Length == 0)
            return true;

        reason = "This campaign only accepts its original STS multiplayer players. "
            + $"Unrecognized player IDs: {string.Join(", ", replacements)}";
        return false;
    }

    private static CampaignMetadata CaptureSaveMetadata(SerializableRun snapshot)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host
            || RunManager.Instance.DebugOnlyGetState() is not RunState runState
            || !ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
            || shared.RunId == Guid.Empty
            || !TryGetCurrentIdentity(out string roomSeed, out int teamId, out int slotId))
        {
            throw new InvalidOperationException("The authoritative AP host run identity is unavailable.");
        }

        ulong hostNetId = RunManager.Instance.NetService.NetId;
        Player host = runState.Players.FirstOrDefault(player => player.NetId == hostNetId)
            ?? throw new InvalidOperationException("The host player is not present in the run snapshot.");
        string characterId = host.getInternalName();

        CampaignMetadata? selected = null;
        if (!string.IsNullOrWhiteSpace(_selectedCampaignId))
        {
            // Do not replace unreadable metadata with a new record and lose its other save slot.
            selected = ReadMetadata(_selectedCampaignId);
        }

        string campaignId = selected != null
            && selected.Status == CampaignStatus.Active
            && IsCurrentApIdentity(selected)
            && selected.RunId == shared.RunId
                ? selected.CampaignId
                : shared.RunId.ToString("N");
        return new CampaignMetadata
        {
            CampaignId = campaignId,
            RunId = shared.RunId,
            ApRoomSeed = roomSeed,
            ApTeamId = teamId,
            ApSlotId = slotId,
            PlayerNumber = CoopSlot.PlayerNumber,
            ApSlotName = ArchipelagoClient.PlayerName ?? string.Empty,
            HostCharacterId = characterId,
            HostCharacterOffset = GameUtility.CurrentConfig?.CharOffset ?? TryGetAPCharacterNumber(characterId),
            HostNetId = hostNetId,
            Roster = BuildRuntimeRoster(runState),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastSavedAtUtc = DateTimeOffset.UtcNow,
            Act = snapshot.CurrentActIndex + 1,
            CompletedFloorCount = snapshot.MapPointHistory?.Sum(act => act.Count) ?? 0,
        };
    }

    private static void SyncSavedSnapshot(CampaignMetadata metadata, bool isApCheckpoint)
    {
        string campaignId = metadata.CampaignId;
        CampaignMetadata? previous = File.Exists(GetMetadataPath(campaignId))
            ? ReadMetadata(campaignId) : null;
        if (previous != null)
        {
            if (previous.RunId != metadata.RunId
                || previous.ApRoomSeed != metadata.ApRoomSeed
                || previous.ApTeamId != metadata.ApTeamId || previous.ApSlotId != metadata.ApSlotId
                || previous.PlayerNumber != metadata.PlayerNumber)
                throw new InvalidDataException("The saved campaign identity changed during the native save.");
            // An asynchronous save completing after run cleanup must not reactivate history.
            if (previous.Status != CampaignStatus.Active)
                return;
            metadata.CreatedAtUtc = previous.CreatedAtUtc;
            metadata.ApCheckpoint = previous.ApCheckpoint;
        }
        CampaignSnapshot? previousRecovery = previous?.FloorRecovery;
        CampaignSnapshot? previousCheckpoint = previous?.ApCheckpoint;

        Directory.CreateDirectory(GetCampaignDirectory(campaignId));
        CampaignSnapshot snapshot = StoreSnapshot(campaignId, GetActiveSavePath(), metadata.LastSavedAtUtc,
            metadata.Act, metadata.CompletedFloorCount);
        metadata.FloorRecovery = snapshot;
        if (isApCheckpoint)
            metadata.ApCheckpoint = snapshot;
        WriteMetadata(metadata);
        DeleteSupersededSnapshot(metadata, previousRecovery);
        DeleteSupersededSnapshot(metadata, previousCheckpoint);
        foreach (CampaignMetadata conflict in ListCampaigns()
            .Where(entry => entry.IsUsable && entry.Metadata != null)
            .Select(entry => entry.Metadata!)
            .Where(other => other.Status == CampaignStatus.Active
                && other.CampaignId != campaignId
                && other.ApRoomSeed == metadata.ApRoomSeed
                && other.ApTeamId == metadata.ApTeamId && other.ApSlotId == metadata.ApSlotId
                && other.PlayerNumber == metadata.PlayerNumber
                && HasSameRoster(other.Roster, metadata.Roster)))
        {
            conflict.Status = CampaignStatus.Archived;
            WriteMetadata(conflict);
        }
        _selectedCampaignId = campaignId;
        IsStartingNewCampaign = false;
        LogUtility.Info(
            $"Updated AP multiplayer campaign {campaignId}: character={metadata.HostCharacterId}, "
                + $"act={metadata.Act}, floors={metadata.CompletedFloorCount}, roster={metadata.Roster.Count}, "
                + $"floorRecovery=updated, apCheckpoint={(isApCheckpoint ? "updated" : "preserved")}"
        );
    }

    private static List<CampaignRosterEntry> BuildRuntimeRoster(RunState runState)
    {
        var roster = new List<CampaignRosterEntry>();
        foreach (Player player in runState.Players)
        {
            ApRunData.TryGetPlayerState(runState, player.NetId, out ApPlayerRunState playerState);
            roster.Add(new CampaignRosterEntry
            {
                NetId = player.NetId,
                DisplayName = ResolvePlayerName(
                    RunManager.Instance.NetService.Platform,
                    player.NetId
                ),
                CharacterId = player.getInternalName(),
                Participation = playerState?.Participation ?? ApParticipationKind.VanillaGuest,
                ApRoomSeed = playerState?.ApRoomSeed,
                ApTeamId = playerState?.ApTeamId,
                ApSlotId = playerState?.ApSlotId,
                PlayerNumber = playerState?.SlotSettings?.PlayerNumber ?? 1,
            });
        }
        return roster;
    }

    private static bool HasSameRoster(
        IReadOnlyCollection<CampaignRosterEntry> savedRoster,
        IReadOnlyCollection<(ulong NetId, string CharacterId)> candidateRoster)
    {
        if (savedRoster.Count != candidateRoster.Count)
            return false;

        Dictionary<ulong, string> candidateByNetId = candidateRoster
            .ToDictionary(player => player.NetId, player => player.CharacterId);
        return savedRoster.All(saved =>
            candidateByNetId.TryGetValue(saved.NetId, out string? characterId)
            && string.Equals(
                saved.CharacterId,
                characterId,
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    private static bool HasSameRoster(
        IReadOnlyCollection<CampaignRosterEntry> first,
        IReadOnlyCollection<CampaignRosterEntry> second) =>
        HasSameRoster(
            first,
            second.Select(player => (player.NetId, player.CharacterId)).ToArray()
        );

    private static List<CampaignRosterEntry> BuildSerializableRoster(SerializableRun run) =>
        run.Players.Select(player => new CampaignRosterEntry
        {
            NetId = player.NetId,
            DisplayName = ResolvePlayerName(run.PlatformType, player.NetId),
            CharacterId = player.CharacterId?.Entry ?? "Unknown Character",
            Participation = ApParticipationKind.VanillaGuest,
        }).ToList();

    private static List<CampaignRosterEntry> BuildImportedRoster(
        RunState runState,
        PlatformType platform)
    {
        var roster = new List<CampaignRosterEntry>();
        foreach (Player player in runState.Players)
        {
            ApRunData.TryGetPlayerState(runState, player.NetId, out ApPlayerRunState state);
            roster.Add(new CampaignRosterEntry
            {
                NetId = player.NetId,
                DisplayName = ResolvePlayerName(platform, player.NetId),
                CharacterId = player.getInternalName(),
                Participation = state?.Participation ?? ApParticipationKind.VanillaGuest,
                ApRoomSeed = state?.ApRoomSeed,
                ApTeamId = state?.ApTeamId,
                ApSlotId = state?.ApSlotId,
                PlayerNumber = state?.SlotSettings?.PlayerNumber ?? 1,
            });
        }
        return roster;
    }

    private static string ResolvePlayerName(PlatformType platform, ulong netId)
    {
        try
        {
            string name = PlatformUtil.GetPlayerName(platform, netId);
            return string.IsNullOrWhiteSpace(name) ? netId.ToString() : name;
        }
        catch
        {
            return netId.ToString();
        }
    }

    private static long? TryGetAPCharacterNumber(string characterId)
    {
        if (ArchipelagoClient.Settings?.Characters.TryGetValue(characterId, out CharacterConfig? config) == true)
            return config.CharOffset;
        return null;
    }

    private static string? ValidateStoredCampaign(CampaignMetadata metadata, bool requirePayload)
    {
        if (metadata.SchemaVersion != MetadataSchemaVersion)
            return $"Unsupported campaign metadata schema {metadata.SchemaVersion}.";
        if (!Guid.TryParseExact(metadata.CampaignId, "N", out _))
            return "Campaign ID is invalid.";
        if (string.IsNullOrWhiteSpace(metadata.ApRoomSeed)
            || string.IsNullOrWhiteSpace(metadata.HostCharacterId)
            || metadata.Roster is not { Count: > 0 })
            return "Campaign identity metadata is incomplete.";
        if (requirePayload
            && GetSnapshotError(metadata, SaveKind.FloorRecovery) is string recoveryError
            && GetSnapshotError(metadata, SaveKind.ApCheckpoint) is string checkpointError)
        {
            return $"Floor recovery: {recoveryError} AP checkpoint: {checkpointError}";
        }
        return null;
    }

    private static CampaignMetadata ReadMetadata(string campaignId)
    {
        ValidateCampaignId(campaignId);
        string json = File.ReadAllText(GetMetadataPath(campaignId));
        CampaignMetadata metadata = JsonSerializer.Deserialize<CampaignMetadata>(json, JsonOptions)
            ?? throw new InvalidDataException("Campaign metadata was empty.");
        if (!string.Equals(metadata.CampaignId, campaignId, StringComparison.Ordinal))
            throw new InvalidDataException("Campaign directory and metadata IDs do not match.");
        if (metadata.SchemaVersion != MetadataSchemaVersion)
            throw new InvalidDataException($"Unsupported campaign metadata schema {metadata.SchemaVersion}.");
        return metadata;
    }

    private static void WriteMetadata(CampaignMetadata metadata)
    {
        ValidateCampaignId(metadata.CampaignId);
        Directory.CreateDirectory(GetCampaignDirectory(metadata.CampaignId));
        AtomicWriteText(
            GetMetadataPath(metadata.CampaignId),
            JsonSerializer.Serialize(metadata, JsonOptions)
        );
    }

    private static bool TryGetCurrentIdentity(out string roomSeed, out int teamId, out int slotId)
    {
        roomSeed = MultiplayerSupport.PreparedApRoomSeed ?? string.Empty;
        teamId = MultiplayerSupport.PreparedApTeamId ?? -1;
        slotId = MultiplayerSupport.PreparedApSlotId ?? -1;
        return !string.IsNullOrWhiteSpace(roomSeed) && teamId >= 0 && slotId >= 0;
    }

    private static string GetCampaignRoot()
    {
        string activeDirectory = Path.GetDirectoryName(GetActiveSavePath())
            ?? throw new InvalidOperationException("The active multiplayer save directory is unavailable.");
        return Path.Combine(activeDirectory, CampaignRootName);
    }

    private static string GetCampaignDirectory(string campaignId)
    {
        ValidateCampaignId(campaignId);
        return Path.Combine(GetCampaignRoot(), campaignId);
    }

    private static string GetMetadataPath(string campaignId) =>
        Path.Combine(GetCampaignDirectory(campaignId), MetadataFileName);

    internal static CampaignSnapshot? GetSnapshot(CampaignMetadata metadata, SaveKind kind) =>
        kind == SaveKind.FloorRecovery ? metadata.FloorRecovery : metadata.ApCheckpoint;

    internal static string? GetSnapshotError(CampaignMetadata metadata, SaveKind kind)
    {
        CampaignSnapshot? snapshot = GetSnapshot(metadata, kind);
        if (snapshot == null)
            return "No save has been recorded yet.";
        try
        {
            return CampaignSaveFiles.Verify(GetCampaignDirectory(metadata.CampaignId),
                snapshot.FileName, snapshot.Sha256);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return ex.Message;
        }
    }

    private static string GetSnapshotPath(string campaignId, CampaignSnapshot snapshot) =>
        CampaignSaveFiles.GetPath(GetCampaignDirectory(campaignId), snapshot.FileName, snapshot.Sha256);

    private static CampaignSnapshot StoreSnapshot(
        string campaignId, string source, DateTimeOffset savedAt, int act, int floors)
    {
        var stored = CampaignSaveFiles.Store(GetCampaignDirectory(campaignId), source);
        return new CampaignSnapshot
        {
            FileName = stored.FileName,
            Sha256 = stored.Hash,
            SavedAtUtc = savedAt,
            Act = act,
            CompletedFloorCount = floors,
        };
    }

    private static void DeleteSupersededSnapshot(CampaignMetadata metadata, CampaignSnapshot? old)
    {
        if (old == null || old.FileName == metadata.FloorRecovery?.FileName
            || old.FileName == metadata.ApCheckpoint?.FileName)
            return;
        try
        {
            File.Delete(GetSnapshotPath(metadata.CampaignId, old));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogUtility.Warn($"Could not remove superseded campaign save: {ex.Message}");
        }
    }

    private static void ValidateCampaignId(string campaignId)
    {
        if (!Guid.TryParseExact(campaignId, "N", out _))
            throw new ArgumentException("Campaign ID must be a 32-digit GUID.", nameof(campaignId));
    }

    private static string GetActiveSavePath()
    {
        string storePath = BetaMainCompatibility.GetRunSavePath(
            SaveManager.Instance.CurrentProfileId,
            "current_run_mp.save"
        );
        ISaveStore? saveStore = SaveManager.Instance._saveStore;
        string? fullPath = saveStore?.GetFullPath(storePath);
        string path = fullPath ?? storePath;
        return path.Contains("://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);
    }

    private static PlatformType GetVanillaPlatform() =>
        SteamInitializer.Initialized && !CommandLineHelper.HasArg("fastmp")
            ? (PlatformType)1
            : (PlatformType)0;

    private static void AtomicCopy(string source, string destination)
    {
        string? directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Campaign destination directory is unavailable.");
        Directory.CreateDirectory(directory);
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temporary, overwrite: true);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void AtomicWriteText(string destination, string contents)
    {
        string? directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Campaign metadata directory is unavailable.");
        Directory.CreateDirectory(directory);
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, contents);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void DeleteCanonicalSaveIfPresent()
    {
        if (SaveManager.Instance.HasMultiplayerRunSave)
        {
            SaveManager.Instance.DeleteCurrentMultiplayerRun();
            return;
        }

        string activeSavePath = GetActiveSavePath();
        if (File.Exists(activeSavePath))
            File.Delete(activeSavePath);
    }

}
