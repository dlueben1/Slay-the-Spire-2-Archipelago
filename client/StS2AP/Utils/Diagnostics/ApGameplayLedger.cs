using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StS2AP.Utils;

// Kept outside run checkpoints: restoring a save must not restore permission to count an event again.
internal sealed class ApGameplayLedger
{
    [JsonRequired]
    public string CampaignId { get; set; } = string.Empty;
    [JsonRequired]
    public Dictionary<Guid, string> Runs { get; set; } = new();
    [JsonRequired]
    public HashSet<string> Events { get; set; } = new();

    internal static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static ApGameplayLedger Load(string path)
    {
        if (!File.Exists(path)) return new() { CampaignId = Guid.NewGuid().ToString("N") };
        var ledger = JsonSerializer.Deserialize<ApGameplayLedger>(File.ReadAllText(path));
        // Fail closed on damaged state rather than silently generating duplicate observations.
        if (ledger == null || !Guid.TryParseExact(ledger.CampaignId, "N", out _) ||
            ledger.Runs == null || ledger.Events == null ||
            ledger.Runs.Any(pair => pair.Key == Guid.Empty || !Guid.TryParseExact(pair.Value, "N", out _)))
            throw new InvalidDataException("Invalid gameplay telemetry ledger.");
        return ledger;
    }

    internal string RunId(Guid savedRunId)
    {
        if (savedRunId == Guid.Empty) throw new ArgumentException("Missing saved run identity.");
        if (!Runs.TryGetValue(savedRunId, out string? id))
            Runs[savedRunId] = id = Guid.NewGuid().ToString("N");
        return id;
    }

    internal string EventId(string key) => Hash(CampaignId + ":" + key)[..32];

    internal bool Reserve(string path, string eventId)
    {
        if (!Events.Add(eventId)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            Events.Remove(eventId);
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        // ponytail: reserve before RitsuLib's void capture API; a crash/queue failure can lose
        // one report. Use an acknowledged durable outbox if exact delivery becomes necessary.
        return true;
    }
}
