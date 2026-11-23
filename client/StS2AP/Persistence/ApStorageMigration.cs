using System.Text.Json;
using System.Text.Json.Nodes;
using StS2AP.Utils;

namespace StS2AP.Persistence;

/// <summary>
/// Moves AP data from the old layout into <see cref="ApStorageLayout"/>. Before it, files were
/// spread over the top of the game's user folder (connection, pending checks, checkpoints and run
/// graphs, split by game profile) and beside each profile's multiplayer save (campaigns).
/// </summary>
/// <remarks>
/// It runs whenever the storage root is first used in a session and only finds work while old
/// files exist, so an older mod version writing them again is picked up on the next launch.
/// Every item is moved on its own: one that cannot be read stays where it is and is retried next
/// time, and nothing is deleted before its copy is in place. The old top-level folders are not
/// per Steam account, so the first account to start claims them.
/// </remarks>
internal static class ApStorageMigration
{
    internal const string LegacyConnectionFile = "ap.connection";
    internal const string LegacyPendingCheckPattern = "sts_ap_pending_checks_v2_*.json";
    internal const string LegacyCheckpointsFolder = "ArchipelagoSingleplayerCheckpoints";
    internal const string LegacyRunGraphsFolder = "ArchipelagoRunGraphs";
    internal const string LegacyCampaignsFolder = "ArchipelagoMultiplayerCampaigns";

    private static readonly JsonSerializerOptions RunGraphJson = new() { WriteIndented = true };

    internal static void Run(string userRoot, string accountRoot, string storageRoot,
        Action<string> info, Action<string> warn)
    {
        var migration = new Migration(storageRoot, warn);
        try
        {
            migration.Connection(Path.Combine(userRoot, LegacyConnectionFile));
            migration.PendingChecks(userRoot);
            migration.Checkpoints(Path.Combine(userRoot, LegacyCheckpointsFolder));
            migration.Campaigns(accountRoot);
            // Last: old run graph logs find their slot through what the steps above moved.
            migration.RunGraphs(Path.Combine(userRoot, LegacyRunGraphsFolder));
        }
        catch (Exception ex)
        {
            warn($"[AP storage] Moving old local AP data stopped early; it will be retried next launch: {ex}");
        }
        if (migration.Moved > 0 || migration.Failed > 0)
            info($"[AP storage] Moved {migration.Moved} old local AP item(s) into {storageRoot}; "
                + $"{migration.Failed} could not be moved and stay in place.");
    }

    private sealed class Migration(string storageRoot, Action<string> warn)
    {
        public int Moved { get; private set; }
        public int Failed { get; private set; }

        public void Connection(string legacyFile)
        {
            if (!File.Exists(legacyFile))
                return;
            Attempt(legacyFile, () =>
            {
                string destination = ApStorageLayout.ConnectionFile(storageRoot);
                if (!File.Exists(destination))
                    ApStorageLayout.WriteAtomically(destination, File.ReadAllText(legacyFile).Trim());
                File.Delete(legacyFile);
            });
        }

        public void PendingChecks(string userRoot)
        {
            if (!Directory.Exists(userRoot))
                return;
            foreach (string file in Directory.EnumerateFiles(userRoot, LegacyPendingCheckPattern))
            {
                Attempt(file, () =>
                {
                    PendingCheckOutbox outbox = JsonSerializer.Deserialize<PendingCheckOutbox>(File.ReadAllText(file))
                        ?? throw new InvalidDataException("The pending-check outbox was empty.");
                    if (outbox.SchemaVersion != PendingCheckOutbox.CurrentSchemaVersion)
                        throw new InvalidDataException($"Unsupported pending-check schema {outbox.SchemaVersion}.");
                    ApSessionIdentity identity = outbox.Identity;
                    string slotDirectory = ApStorageLayout.SlotDirectory(
                        storageRoot, identity.RoomSeed, identity.ApTeamId, identity.ApSlotId);
                    string destination = Path.Combine(ApStorageLayout.PendingChecksDirectory(slotDirectory),
                        $"{identity.GetFileKey()}.json");

                    var merged = new SortedSet<long>(outbox.LocationIds);
                    if (File.Exists(destination))
                    {
                        PendingCheckOutbox existing = JsonSerializer.Deserialize<PendingCheckOutbox>(File.ReadAllText(destination))
                            ?? throw new InvalidDataException("The existing pending-check outbox was empty.");
                        if (existing.Identity != identity)
                            throw new InvalidDataException("The existing pending-check outbox belongs to another session.");
                        merged.UnionWith(existing.LocationIds);
                    }
                    if (merged.Count > 0)
                    {
                        ApStorageLayout.WriteAtomically(destination, JsonSerializer.Serialize(new PendingCheckOutbox
                        {
                            SchemaVersion = PendingCheckOutbox.CurrentSchemaVersion,
                            Identity = identity,
                            LocationIds = merged,
                        }));
                    }
                    NoteSlot(identity.RoomSeed, identity.ApTeamId, identity.ApSlotId, null,
                        identity.ServerAuthority, new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero));
                    File.Delete(file);
                });
            }
        }

        public void Checkpoints(string legacyRoot)
        {
            if (!Directory.Exists(legacyRoot))
                return;
            var banks = new SingleplayerCheckpointBank(storageRoot);
            foreach (string profile in Directory.EnumerateDirectories(legacyRoot, "profile-*"))
            {
                foreach (string bank in Directory.EnumerateDirectories(profile))
                {
                    if (!File.Exists(Path.Combine(bank, "metadata.json")))
                    {
                        RemoveIfEmpty(bank);
                        continue;
                    }
                    Attempt(bank, () =>
                    {
                        var (key, lastSaved) = banks.ImportLegacyBank(bank);
                        NoteSlot(key.Owner.Seed, key.Owner.Team, key.Owner.Slot, null, null, lastSaved);
                    });
                }
                RemoveIfEmpty(profile);
            }
            RemoveIfEmpty(legacyRoot);
        }

        /// <summary>Campaigns sat beside each profile's multiplayer save, modded or not.</summary>
        public void Campaigns(string accountRoot)
        {
            var profileRoots = new[] { accountRoot, Path.Combine(accountRoot, "modded") }
                .Where(Directory.Exists)
                .SelectMany(root => Directory.EnumerateDirectories(root, "profile*"));
            foreach (string profile in profileRoots)
            {
                string legacyRoot = Path.Combine(profile, "saves", LegacyCampaignsFolder);
                if (!Directory.Exists(legacyRoot))
                    continue;
                foreach (string campaign in Directory.EnumerateDirectories(legacyRoot))
                    Attempt(campaign, () => MoveCampaign(campaign));
                RemoveIfEmpty(legacyRoot);
            }
        }

        private void MoveCampaign(string campaign)
        {
            string campaignId = Path.GetFileName(campaign);
            if (!Guid.TryParseExact(campaignId, "N", out _))
                throw new InvalidDataException("The folder is not an AP campaign.");
            using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(campaign, "metadata.json")));
            JsonElement root = metadata.RootElement;
            string seed = root.GetProperty("ApRoomSeed").GetString() ?? "";
            int team = root.GetProperty("ApTeamId").GetInt32();
            int slot = root.GetProperty("ApSlotId").GetInt32();
            string? slotName = root.TryGetProperty("ApSlotName", out JsonElement name) ? name.GetString() : null;
            DateTimeOffset saved = LastSaved(root);

            string destination = Path.Combine(ApStorageLayout.CampaignsDirectory(
                ApStorageLayout.SlotDirectory(storageRoot, seed, team, slot)), campaignId);
            if (Directory.Exists(destination))
            {
                // The same campaign in two profiles: keep the copy saved last.
                using JsonDocument existing = JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, "metadata.json")));
                if (LastSaved(existing.RootElement) >= saved)
                {
                    Directory.Delete(campaign, recursive: true);
                    return;
                }
                Directory.Delete(destination, recursive: true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            // Both folders are in the game's user folder, so this is a rename on one drive.
            Directory.Move(campaign, destination);
            // "AP Slot N" is the placeholder an imported save got when the real name was unknown.
            bool realName = slotName is { Length: > 0 } && slotName != $"AP Slot {slot}";
            NoteSlot(seed, team, slot, realName ? slotName : null, null, saved);
        }

        private static DateTimeOffset LastSaved(JsonElement metadata) =>
            metadata.TryGetProperty("LastSavedAtUtc", out JsonElement value) && value.TryGetDateTimeOffset(out var saved)
                ? saved
                : DateTimeOffset.MinValue;

        public void RunGraphs(string legacyRoot)
        {
            if (!Directory.Exists(legacyRoot))
                return;
            foreach (string profile in Directory.EnumerateDirectories(legacyRoot, "profile-*"))
            {
                int? profileId = int.TryParse(Path.GetFileName(profile)["profile-".Length..], out int id) ? id : null;
                foreach (string log in Directory.EnumerateFiles(profile, "*.json"))
                    Attempt(log, () => MoveRunGraph(log, profileId));

                string exports = Path.Combine(profile, "exports");
                if (Directory.Exists(exports))
                {
                    foreach (string export in Directory.EnumerateFiles(exports))
                        Attempt(export, () => MoveExport(export));
                    RemoveIfEmpty(exports);
                }
                // A snapshot or temporary file without a log cannot open a recap.
                foreach (string orphan in Directory.EnumerateFiles(profile)
                    .Where(file => !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        && !File.Exists(Path.ChangeExtension(StripTemporary(file), ".json"))))
                    File.Delete(orphan);
                RemoveIfEmpty(profile);
            }
            RemoveIfEmpty(legacyRoot);
        }

        private static string StripTemporary(string file)
        {
            int marker = file.IndexOf(".tmp-", StringComparison.Ordinal);
            return marker < 0 ? file : file[..marker];
        }

        private void MoveRunGraph(string log, int? profileId)
        {
            if (!long.TryParse(Path.GetFileNameWithoutExtension(log), out _))
                throw new InvalidDataException("The file is not a run graph log.");
            JsonObject node = JsonNode.Parse(File.ReadAllText(log))?.AsObject()
                ?? throw new InvalidDataException("The run graph log was empty.");
            string seed = node["seed"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(seed))
                throw new InvalidDataException("The run graph log names no seed.");
            string? slotName = node["slot_name"]?.GetValue<string>();
            if (node["profile_id"] == null && profileId != null)
                node["profile_id"] = profileId;

            string slotDirectory = FindSlotForRunGraph(seed, slotName);
            // The log knows the slot's name and when it was played, which a slot built from
            // pending checks or a checkpoint bank does not.
            DateTimeOffset? started = node["start_time"]?.GetValue<long>() is long startTime
                ? DateTimeOffset.FromUnixTimeSeconds(startTime) : null;
            ApStorageLayout.NoteSlot(slotDirectory, seed, null, null, slotName, null, started);

            string directory = ApStorageLayout.RunGraphsDirectory(slotDirectory);
            string destination = Path.Combine(directory, Path.GetFileName(log));
            if (!File.Exists(destination))
                ApStorageLayout.WriteAtomically(destination, node.ToJsonString(RunGraphJson));
            string snapshot = Path.ChangeExtension(log, ".last");
            string snapshotDestination = Path.Combine(directory, Path.GetFileName(snapshot));
            if (File.Exists(snapshot) && !File.Exists(snapshotDestination))
                File.Move(snapshot, snapshotDestination);
            if (File.Exists(snapshot))
                File.Delete(snapshot);
            File.Delete(log);
        }

        /// <summary>
        /// Old logs record the seed and slot name but not the team and slot. They join the one
        /// slot folder with that seed (and name, when there are several), or a folder for the seed.
        /// </summary>
        private string FindSlotForRunGraph(string seed, string? slotName)
        {
            var sameSeed = ApStorageLayout.EnumerateSlotDirectories(storageRoot)
                .Select(directory => (Directory: directory, Info: ApStorageLayout.ReadSlotInfo(directory)))
                .Where(slotFolder => slotFolder.Info?.Seed == seed && slotFolder.Info.Slot != null)
                .ToList();
            if (sameSeed.Count > 1 && !string.IsNullOrEmpty(slotName))
                sameSeed = sameSeed.Where(slotFolder => slotFolder.Info!.SlotName == slotName).ToList();
            if (sameSeed.Count == 1)
                return sameSeed[0].Directory;

            return ApStorageLayout.UnknownSlotDirectory(storageRoot, seed);
        }

        private void MoveExport(string export)
        {
            string directory = ApStorageLayout.ExportsDirectory(storageRoot);
            Directory.CreateDirectory(directory);
            string name = Path.GetFileNameWithoutExtension(export);
            string extension = Path.GetExtension(export);
            string destination = Path.Combine(directory, name + extension);
            for (int copy = 2; File.Exists(destination); copy++)
                destination = Path.Combine(directory, $"{name}-{copy}{extension}");
            File.Move(export, destination);
        }

        private void NoteSlot(string seed, int team, int slot, string? slotName, string? server, DateTimeOffset? played) =>
            ApStorageLayout.NoteSlot(ApStorageLayout.SlotDirectory(storageRoot, seed, team, slot),
                seed, team, slot, slotName, server, played);

        private void Attempt(string item, Action move)
        {
            try
            {
                move();
                Moved++;
            }
            catch (Exception ex)
            {
                Failed++;
                warn($"[AP storage] Left {item} in place: {ex.Message}");
            }
        }

        private static void RemoveIfEmpty(string directory)
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }
}
