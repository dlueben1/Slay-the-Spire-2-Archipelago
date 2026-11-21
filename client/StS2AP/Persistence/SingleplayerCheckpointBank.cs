using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StS2AP.Persistence;

/// <summary>One checkpoint bank per AP slot and character, shared across all attempts.</summary>
/// <param name="root">The local storage root; each bank lives in its slot's folder (see <see cref="ApStorageLayout"/>).</param>
internal sealed class SingleplayerCheckpointBank(string root)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    internal sealed record Identity(string Seed, int Team, int Slot);
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
        ["1-ancient", "1-treasure", "1-campfire", "1-boss", "2-treasure", "2-campfire", "2-boss", "3-treasure", "3-campfire"];

    internal static bool IsAllowed(string key, bool startOfAct, int unlockedAct) =>
        Milestones.Contains(key) && (!startOfAct || key[0] - '0' <= Math.Max(1, unlockedAct));

    // The manifest's identity is still checked on every read, so a folder name can never hand
    // one slot's or character's checkpoints to another.
    internal string DirectoryFor(BankKey key) => ApStorageLayout.CheckpointDirectory(
        ApStorageLayout.SlotDirectory(root, key.Owner.Seed, key.Owner.Team, key.Owner.Slot), key.Character);
    private string Manifest(BankKey key) => Path.Combine(DirectoryFor(key), "metadata.json");

    public Bank Read(BankKey key)
    {
        if (key.Owner == null || string.IsNullOrWhiteSpace(key.Owner.Seed)
            || key.Owner.Team < 0 || key.Owner.Slot < 0
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

    /// <summary>
    /// Moves a bank from the old per-profile layout into this root, then deletes the old folder.
    /// When two profiles banked the same slot and character, each milestone keeps the newer save.
    /// A damaged old save is skipped rather than allowed to replace a good one. Returns the bank's
    /// identity and when its newest checkpoint was saved.
    /// </summary>
    internal (BankKey Key, DateTimeOffset? LastSaved) ImportLegacyBank(string legacyDirectory)
    {
        string legacyManifest = Path.Combine(legacyDirectory, "metadata.json");
        Bank legacy = JsonSerializer.Deserialize<Bank>(File.ReadAllText(legacyManifest), Options)
            ?? throw new InvalidDataException("Empty AP checkpoint metadata.");
        if (legacy.Version != 1 || legacy.Checkpoints == null)
            throw new InvalidDataException("Invalid or unsupported AP checkpoint metadata.");

        Bank target = Read(legacy.Identity);
        string destination = DirectoryFor(legacy.Identity);
        foreach (var (key, snapshot) in legacy.Checkpoints)
        {
            if (snapshot == null || snapshot.Key != key || !Milestones.Contains(key))
                continue;
            if (target.Checkpoints.TryGetValue(key, out Snapshot? existing) && existing.SavedAt >= snapshot.SavedAt)
                continue;
            if (CampaignSaveFiles.Verify(legacyDirectory, snapshot.FileName, snapshot.Hash) != null)
                continue;
            CampaignSaveFiles.StoreBytes(destination, File.ReadAllBytes(
                CampaignSaveFiles.GetPath(legacyDirectory, snapshot.FileName, snapshot.Hash)));
            target.Checkpoints[key] = snapshot;
        }
        Write(target);

        // Payloads replaced by a newer profile's save are no longer referenced.
        var kept = target.Checkpoints.Values.Select(s => s.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(destination, "run-*.save"))
            if (!kept.Contains(Path.GetFileName(file)))
                File.Delete(file);
        Directory.Delete(legacyDirectory, recursive: true);
        return (target.Identity, target.Checkpoints.Values.Select(s => (DateTimeOffset?)s.SavedAt).Max());
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
