using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StS2AP.Persistence;

/// <summary>One checkpoint bank per AP slot and character, shared across all attempts.</summary>
internal sealed class SingleplayerCheckpointBank(string root)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    internal sealed record Identity(string Seed, int Team, int Slot, int PlayerNumber = 1);
    internal sealed record BankKey(Identity Owner, string Character);
    internal sealed record Snapshot(string Key, string FileName, string Hash, DateTimeOffset SavedAt,
        int Floor, bool StartOfAct);
    internal sealed class Bank
    {
        public int Version { get; set; } = 1;
        public BankKey Identity { get; set; } = new(new("", -1, -1), "");
        public Dictionary<string, Snapshot> Checkpoints { get; set; } = new();
    }

    internal static readonly string[] Milestones =
        ["1-ancient", "1-treasure", "1-boss", "2-treasure", "2-boss", "3-treasure"];

    internal static bool IsAllowed(string key, bool startOfAct, int unlockedAct) =>
        Milestones.Contains(key) && (!startOfAct || key[0] - '0' <= Math.Max(1, unlockedAct));

    // Hash the full identity rather than sanitizing names: different slots/characters must
    // never alias, and AP-provided names must never become filesystem paths.
    private string DirectoryFor(BankKey key)
    {
        // Preserve existing Player 1 bank paths and metadata from before shared slots.
        object identity = key.Owner.PlayerNumber == 1
            ? new { Owner = new { key.Owner.Seed, key.Owner.Team, key.Owner.Slot }, key.Character }
            : key;
        return Path.Combine(root,
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity))));
    }
    private string Manifest(BankKey key) => Path.Combine(DirectoryFor(key), "metadata.json");

    public Bank Read(BankKey key)
    {
        if (key.Owner == null || string.IsNullOrWhiteSpace(key.Owner.Seed)
            || key.Owner.Team < 0 || key.Owner.Slot < 0 || key.Owner.PlayerNumber is < 1 or > 4
            || string.IsNullOrWhiteSpace(key.Character))
            throw new InvalidDataException("Invalid AP checkpoint bank identity.");
        if (!File.Exists(Manifest(key))) return new() { Identity = key };
        Bank bank = JsonSerializer.Deserialize<Bank>(File.ReadAllText(Manifest(key)), Options)
            ?? throw new InvalidDataException("Empty AP checkpoint metadata.");
        if (bank.Version != 1 || bank.Identity != key || bank.Checkpoints == null
            || bank.Checkpoints.Any(pair => pair.Value == null || pair.Key != pair.Value.Key
                || !Milestones.Contains(pair.Key)))
            throw new InvalidDataException("Invalid or unsupported AP checkpoint metadata.");
        return bank;
    }

    public void Save(BankKey bank, string key, string payload, int floor, bool startOfAct)
    {
        if (!Milestones.Contains(key)) throw new ArgumentException("Unknown AP checkpoint.", nameof(key));
        // Re-read the shared bank, even for a new attempt. Only the reached milestone changes.
        Bank next = Read(bank);
        var stored = CampaignSaveFiles.StoreBytes(DirectoryFor(bank), Encoding.UTF8.GetBytes(payload));
        next.Checkpoints.TryGetValue(key, out Snapshot? previous);
        next.Checkpoints[key] = new(key, stored.FileName, stored.Hash, DateTimeOffset.UtcNow, floor, startOfAct);
        Write(next);
        // Only prune after metadata publication. A failed write never removes an older payload.
        if (previous != null && next.Checkpoints.Values.All(s => s.FileName != previous.FileName))
        {
            try { File.Delete(CampaignSaveFiles.GetPath(DirectoryFor(bank), previous.FileName, previous.Hash)); }
            catch (IOException) { /* An orphan is preferable to failing a published save. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    public (string Payload, Snapshot Snapshot) Load(BankKey bank, string key)
    {
        Bank saved = Read(bank);
        if (!saved.Checkpoints.TryGetValue(key, out Snapshot? snapshot))
            throw new InvalidDataException("The checkpoint is missing.");
        byte[] bytes = File.ReadAllBytes(CampaignSaveFiles.GetPath(DirectoryFor(bank), snapshot.FileName, snapshot.Hash));
        if (!string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            snapshot.Hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The checkpoint checksum does not match.");
        return (Encoding.UTF8.GetString(bytes), snapshot);
    }

    private void Write(Bank bank)
    {
        Directory.CreateDirectory(DirectoryFor(bank.Identity));
        string destination = Manifest(bank.Identity);
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(bank, Options));
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
