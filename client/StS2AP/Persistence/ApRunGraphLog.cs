using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace StS2AP.Persistence;

public enum ApRunGraphEventKind
{
    CheckSent,
    ItemClaimed,
    GoldClaimed,
    DeathLinkSent,
    DeathLinkReceived,
}

public enum ApRunGraphItemType
{
    Filler,
    Useful,
    Progression,
    Trap,
}

public enum ApRunOutcome
{
    Won,
    Lost,
}

/// <summary>One thing that happened on a floor of an AP run, as the end-of-run graph shows it.</summary>
public sealed class ApRunGraphEvent
{
    public ApRunGraphEventKind Kind { get; set; }
    /// <summary>The game's TotalFloor when the event happened.</summary>
    public int Floor { get; set; }
    public int ActIndex { get; set; }
    // Display text is stored resolved: Run History may show a seed we are no longer connected to.
    public string ItemName { get; set; } = "";
    public string OtherPlayer { get; set; } = "";
    public bool IsSelf { get; set; }
    public ApRunGraphItemType Classification { get; set; }
    public int Amount { get; set; }
    public long? LocationId { get; set; }
    /// <summary>Sent on a branch of the run that a checkpoint reload later undid.</summary>
    public bool Abandoned { get; set; }
}

/// <summary>
/// Per-run record of AP activity by floor. It lives outside the checkpointed run state so a
/// checkpoint reload cannot erase checks that really were sent.
/// </summary>
public sealed class ApRunGraphLog
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public long StartTime { get; set; }
    public string Seed { get; set; } = "";
    public string SlotName { get; set; } = "";
    public string Character { get; set; } = "";
    public ulong LocalNetId { get; set; }
    public int SlotChecksAtStart { get; set; }
    public int? SlotChecksAtEnd { get; set; }
    public int SlotLocationCount { get; set; }
    public ApRunOutcome? Outcome { get; set; }
    public List<ApRunGraphEvent> Events { get; set; } = new();

    /// <summary>
    /// Marks events after the restored floor as abandoned. Events on the restored floor itself stay:
    /// the checkpoint is taken on that floor, and its checks will not be sent a second time.
    /// </summary>
    public int MarkAbandonedAfter(int completedFloors)
    {
        int marked = 0;
        foreach (var e in Events.Where(e => !e.Abandoned && e.Floor > completedFloors))
        {
            e.Abandoned = true;
            marked++;
        }
        return marked;
    }

    public static ApRunGraphItemType Classify(bool progression, bool useful, bool trap) =>
        progression ? ApRunGraphItemType.Progression
        : useful ? ApRunGraphItemType.Useful
        : trap ? ApRunGraphItemType.Trap
        : ApRunGraphItemType.Filler;
}

/// <summary>Stores one JSON file per run, named by the game's run StartTime.</summary>
public sealed class ApRunGraphStore(string root)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    // Export bundle identifiers, for tools that read exported runs.
    public const string ExportFormat = "sts2-ap-run";
    public const string ExportAllFormat = "sts2-ap-runs";
    public const int ExportVersion = 1;

    public string RootDirectory => root;

    public string PathFor(long startTime) => Path.Combine(root, $"{startTime}.json");

    /// <summary>Exports go in a subfolder, so <see cref="ListStartTimes"/> never sees them.</summary>
    public string ExportsDirectory => Path.Combine(root, "exports");

    public bool Exists(long startTime) => File.Exists(PathFor(startTime));

    /// <summary>Start times of every logged run, newest first.</summary>
    public IReadOnlyList<long> ListStartTimes()
    {
        if (!Directory.Exists(root))
            return Array.Empty<long>();
        return Directory.EnumerateFiles(root, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => long.TryParse(name, out long startTime) ? startTime : (long?)null)
            .OfType<long>()
            .OrderByDescending(startTime => startTime)
            .ToList();
    }

    /// <summary>Returns null when the run has no log or the file is unreadable; <paramref name="error"/> says which.</summary>
    public ApRunGraphLog? Load(long startTime, out string? error)
    {
        error = null;
        string path = PathFor(startTime);
        if (!File.Exists(path))
            return null;
        try
        {
            var log = JsonSerializer.Deserialize<ApRunGraphLog>(File.ReadAllText(path), Options);
            if (log == null || log.Version != ApRunGraphLog.CurrentVersion || log.StartTime != startTime)
            {
                error = $"Run graph log {path} is empty, from another run, or an unsupported version.";
                return null;
            }
            log.Events ??= new();
            return log;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"Run graph log {path} could not be read: {ex.Message}";
            return null;
        }
    }

    /// <summary>Deletes the run's graph log. The game's own run history is left alone.</summary>
    public void Delete(long startTime) => File.Delete(PathFor(startTime));

    /// <summary>
    /// One run as a self-contained export: the AP log plus the game's run history JSON, kept
    /// whole so other tools can use fields the graph does not. The history is null for an
    /// unfinished run, which the game has not saved yet.
    /// </summary>
    public static JsonObject ExportBundle(ApRunGraphLog log, string? runHistoryJson) => new()
    {
        ["format"] = ExportFormat,
        ["version"] = ExportVersion,
        ["ap"] = JsonSerializer.SerializeToNode(log, Options),
        ["run"] = runHistoryJson == null ? null : JsonNode.Parse(runHistoryJson),
    };

    public static JsonObject ExportAllBundle(IEnumerable<JsonObject> runs) => new()
    {
        ["format"] = ExportAllFormat,
        ["version"] = ExportVersion,
        ["runs"] = new JsonArray(runs.Cast<JsonNode>().ToArray()),
    };

    /// <summary>Writes an export to <see cref="ExportsDirectory"/> and returns its full path.</summary>
    public string WriteExport(string fileName, JsonObject bundle)
    {
        Directory.CreateDirectory(ExportsDirectory);
        string path = Path.Combine(ExportsDirectory, fileName);
        File.WriteAllText(path, bundle.ToJsonString(Options));
        return path;
    }

    public void Save(ApRunGraphLog log)
    {
        Directory.CreateDirectory(root);
        string destination = PathFor(log.StartTime);
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(log, Options));
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
