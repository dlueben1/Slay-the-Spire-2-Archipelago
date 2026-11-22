using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StS2AP.Persistence;

/// <summary>
/// Where the mod keeps its local data under one root, the Steam account's
/// <c>mod_data/Archipelago</c> folder. Everything that belongs to one AP slot lives in that slot's
/// folder, so forgetting a finished seed is one folder delete. Pure path logic, so tests and the
/// one-time migration can use it without Godot.
/// </summary>
/// <remarks>
/// <code>
/// connection.json                       last server, slot and password typed in
/// exports/                              recap images and run exports
/// slots/&lt;seed&gt;_t&lt;team&gt;_s&lt;slot&gt;/
///   slot.json                           readable identity and when it was last played
///   pending-checks/&lt;session key&gt;.json  checks not yet confirmed by that server
///   checkpoints/&lt;CHARACTER&gt;/          singleplayer checkpoint bank
///   campaigns/&lt;campaign id&gt;/          multiplayer campaign bank
///   run-graphs/&lt;start time&gt;.json      end-of-run graph logs
/// </code>
/// </remarks>
internal static class ApStorageLayout
{
    internal const string ConnectionFileName = "connection.json";
    internal const string ExportsFolder = "exports";
    internal const string SlotsFolder = "slots";
    internal const string SlotInfoFileName = "slot.json";
    internal const string PendingChecksFolder = "pending-checks";
    internal const string CheckpointsFolder = "checkpoints";
    internal const string CampaignsFolder = "campaigns";
    internal const string RunGraphsFolder = "run-graphs";

    private static readonly JsonSerializerOptions SlotInfoOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static string ConnectionFile(string root) => Path.Combine(root, ConnectionFileName);

    internal static string ExportsDirectory(string root) => Path.Combine(root, ExportsFolder);

    internal static string SlotsDirectory(string root) => Path.Combine(root, SlotsFolder);

    internal static string SlotDirectory(string root, string seed, int team, int slot)
    {
        if (string.IsNullOrWhiteSpace(seed) || team < 0 || slot < 0)
            throw new InvalidDataException("Invalid AP slot identity.");
        return Path.Combine(SlotsDirectory(root), $"{SafeSegment(seed)}_t{team}_s{slot}");
    }

    /// <summary>
    /// For run graph logs from before team and slot were recorded, when no other data names the
    /// slot they were played on.
    /// </summary>
    internal static string UnknownSlotDirectory(string root, string seed) =>
        Path.Combine(SlotsDirectory(root), $"{SafeSegment(seed)}_unknown-slot");

    internal static string PendingChecksDirectory(string slotDirectory) =>
        Path.Combine(slotDirectory, PendingChecksFolder);

    internal static string CheckpointDirectory(string slotDirectory, string character) =>
        Path.Combine(slotDirectory, CheckpointsFolder, SafeSegment(character));

    internal static string CampaignsDirectory(string slotDirectory) =>
        Path.Combine(slotDirectory, CampaignsFolder);

    internal static string RunGraphsDirectory(string slotDirectory) =>
        Path.Combine(slotDirectory, RunGraphsFolder);

    internal static IEnumerable<string> EnumerateSlotDirectories(string root)
    {
        string slots = SlotsDirectory(root);
        return Directory.Exists(slots)
            ? Directory.EnumerateDirectories(slots)
            : Enumerable.Empty<string>();
    }

    /// <summary>
    /// Keeps a name readable when it is already a safe folder name (AP seeds are long decimal
    /// strings, character IDs are upper-case words) and hashes anything else, so text from an AP
    /// server or another mod can never become a path or alias a different name.
    /// </summary>
    internal static string SafeSegment(string value)
    {
        bool safe = value.Length is > 0 and <= 64
            && char.IsAsciiLetterOrDigit(value[0])
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        return safe
            ? value
            : "h" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
    }

    /// <summary>The readable description of a slot folder. Its folder name stays the authority.</summary>
    internal sealed class SlotInfo
    {
        public string Seed { get; set; } = "";
        /// <summary>Null for a folder of old run graphs whose slot is not known.</summary>
        public int? Team { get; set; }
        public int? Slot { get; set; }
        public string? SlotName { get; set; }
        public string? Server { get; set; }
        public DateTimeOffset? LastPlayedUtc { get; set; }
    }

    internal static SlotInfo? ReadSlotInfo(string slotDirectory)
    {
        string path = Path.Combine(slotDirectory, SlotInfoFileName);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<SlotInfo>(File.ReadAllText(path), SlotInfoOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Records what is known about a slot in its slot.json, keeping what earlier writes knew:
    /// null or empty values leave the stored one alone, and the later play date wins.
    /// </summary>
    internal static void NoteSlot(string slotDirectory, string seed, int? team, int? slot,
        string? slotName, string? server, DateTimeOffset? played)
    {
        SlotInfo info = ReadSlotInfo(slotDirectory) ?? new SlotInfo();
        info.Seed = seed;
        info.Team = team ?? info.Team;
        info.Slot = slot ?? info.Slot;
        if (!string.IsNullOrWhiteSpace(slotName))
            info.SlotName = slotName;
        if (!string.IsNullOrWhiteSpace(server))
            info.Server = server;
        if (played > (info.LastPlayedUtc ?? DateTimeOffset.MinValue))
            info.LastPlayedUtc = played;
        WriteAtomically(Path.Combine(slotDirectory, SlotInfoFileName), JsonSerializer.Serialize(info, SlotInfoOptions));
    }

    /// <summary>What one slot folder holds, for the cleanup buttons.</summary>
    internal sealed record SlotSummary(
        string Directory,
        SlotInfo? Info,
        long Bytes,
        bool HasUnsentChecks,
        DateTimeOffset LastActivityUtc)
    {
        public string DisplayName
        {
            get
            {
                string seed = Info?.Seed is { Length: > 0 } s ? s : Path.GetFileName(Directory);
                string tail = seed.Length > 6 ? "…" + seed[^6..] : seed;
                return Info?.SlotName is { Length: > 0 } name ? $"{name} (seed {tail})" : $"Seed {tail}";
            }
        }
    }

    internal static IReadOnlyList<SlotSummary> ListSlots(string root)
    {
        var result = new List<SlotSummary>();
        foreach (string directory in EnumerateSlotDirectories(root))
        {
            SlotInfo? info = ReadSlotInfo(directory);
            long bytes = 0;
            DateTime newestWrite = DateTime.MinValue;
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                bytes += file.Length;
                if (file.Name != SlotInfoFileName && file.LastWriteTimeUtc > newestWrite)
                    newestWrite = file.LastWriteTimeUtc;
            }
            // File times say when data was last written, which a move between folders resets; the
            // recorded date says when the slot was last played, so it wins when there is one.
            DateTimeOffset lastActivity = info?.LastPlayedUtc
                ?? new DateTimeOffset(DateTime.SpecifyKind(newestWrite, DateTimeKind.Utc));
            result.Add(new SlotSummary(directory, info, bytes, HasUnsentChecks(directory), lastActivity));
        }
        return result.OrderByDescending(slot => slot.LastActivityUtc).ToArray();
    }

    // An outbox file is deleted once the server has every check in it, so any file means unsent checks.
    private static bool HasUnsentChecks(string slotDirectory)
    {
        string directory = PendingChecksDirectory(slotDirectory);
        return Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.json").Any();
    }

    internal static void WriteAtomically(string destination, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
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
}
