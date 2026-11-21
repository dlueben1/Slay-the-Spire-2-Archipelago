using System.Text.Json;
using System.Text.Json.Nodes;
using StS2AP.Persistence;
using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ApStorageMigrationTests : IDisposable
{
    private readonly string _user = Path.Combine(Path.GetTempPath(), "ap-storage-migration-" + Guid.NewGuid().ToString("N"));
    private string Account => Path.Combine(_user, "default", "1");
    private string Storage => Path.Combine(Account, "mod_data", "Archipelago");
    private string SlotFolder => Path.Combine(Storage, "slots", "123_t0_s1");
    private readonly List<string> _warnings = new();

    private static readonly ApSessionIdentity Session = ApSessionIdentity.Create("localhost:38281", "123", 0, 1);
    private static readonly SingleplayerCheckpointBank.BankKey BankKey = new(new("123", 0, 1), "IRONCLAD");

    [Fact]
    public void EveryOldLocationMovesIntoItsSlotFolder()
    {
        WriteLegacyLayout();

        Migrate();

        Assert.Empty(_warnings);
        Assert.Equal("""{"SlotName":"Alice"}""", File.ReadAllText(Path.Combine(Storage, "connection.json")));
        Assert.False(File.Exists(Path.Combine(_user, "ap.connection")));

        var outbox = JsonSerializer.Deserialize<PendingCheckOutbox>(File.ReadAllText(
            Path.Combine(SlotFolder, "pending-checks", Session.GetFileKey() + ".json")))!;
        Assert.Equal(Session, outbox.Identity);
        Assert.Equal(new long[] { 1, 2 }, outbox.LocationIds);
        Assert.Empty(Directory.GetFiles(_user, "sts_ap_pending_checks_v2_*"));

        var bank = new SingleplayerCheckpointBank(Storage);
        Assert.Equal("profile 2 boss", bank.Load(BankKey, "1-boss").Payload);
        Assert.Equal("profile 1 treasure", bank.Load(BankKey, "1-treasure").Payload);

        string campaign = Path.Combine(SlotFolder, "campaigns", CampaignId);
        Assert.True(File.Exists(Path.Combine(campaign, "metadata.json")));
        Assert.True(File.Exists(Path.Combine(campaign, "run-AA.save")));

        var info = ApStorageLayout.ReadSlotInfo(SlotFolder)!;
        Assert.Equal(("123", 0, 1), (info.Seed, info.Team!.Value, info.Slot!.Value));
        Assert.Equal("Alice", info.SlotName);
        Assert.Equal("localhost:38281", info.Server);

        // An old log joins the only slot with its seed and learns its profile.
        var log = JsonNode.Parse(File.ReadAllText(Path.Combine(SlotFolder, "run-graphs", "100.json")))!;
        Assert.Equal(1, log["profile_id"]!.GetValue<int>());
        Assert.Equal(7, log["events_kept"]!.GetValue<int>());
        Assert.True(File.Exists(Path.Combine(SlotFolder, "run-graphs", "100.last")));
        // A log whose seed has no other data gets a folder for the seed.
        string unknown = Path.Combine(Storage, "slots", "999_unknown-slot");
        Assert.True(File.Exists(Path.Combine(unknown, "run-graphs", "200.json")));
        Assert.Equal("Bob", ApStorageLayout.ReadSlotInfo(unknown)!.SlotName);
        Assert.True(File.Exists(Path.Combine(Storage, "exports", "100-recap.png")));
        Assert.Equal(2, new ApRunGraphArchive(Storage).ListStartTimes().Count);

        Assert.False(Directory.Exists(Path.Combine(_user, ApStorageMigration.LegacyCheckpointsFolder)));
        Assert.False(Directory.Exists(Path.Combine(_user, ApStorageMigration.LegacyRunGraphsFolder)));
        Assert.False(Directory.Exists(Path.Combine(Account, "modded", "profile1", "saves", ApStorageMigration.LegacyCampaignsFolder)));
        Assert.True(File.Exists(Path.Combine(Account, "modded", "profile1", "saves", "current_run_mp.save")));
    }

    [Fact]
    public void RunningAgainChangesNothingAndLaterOldFilesMerge()
    {
        WriteLegacyLayout();
        Migrate();
        string[] before = SnapshotFiles();
        Migrate();
        Assert.Equal(before, SnapshotFiles());

        // An older mod version wrote another check for the same session after the move.
        WriteOutbox(Session, 3);
        Migrate();
        var outbox = JsonSerializer.Deserialize<PendingCheckOutbox>(File.ReadAllText(
            Path.Combine(SlotFolder, "pending-checks", Session.GetFileKey() + ".json")))!;
        Assert.Equal(new long[] { 1, 2, 3 }, outbox.LocationIds);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void UnreadableItemsStayInPlaceWithoutBlockingTheRest()
    {
        WriteLegacyLayout();
        string broken = Path.Combine(_user, "sts_ap_pending_checks_v2_BROKEN.json");
        File.WriteAllText(broken, "{");

        Migrate();

        Assert.True(File.Exists(broken));
        Assert.Single(_warnings);
        Assert.True(File.Exists(Path.Combine(SlotFolder, "pending-checks", Session.GetFileKey() + ".json")));
    }

    [Fact]
    public void ForgettingIsOneFolderAndListingCountsUnsentChecks()
    {
        WriteLegacyLayout();
        Migrate();

        var slots = ApStorageLayout.ListSlots(Storage);
        var alice = slots.Single(slot => slot.Directory == SlotFolder);
        Assert.Equal(2, alice.UnsentChecks);
        Assert.Equal("Alice (seed 123)", alice.DisplayName);
        Assert.True(alice.Bytes > 0);
    }

    [Theory]
    [InlineData("18754431030632685709", "18754431030632685709")]
    [InlineData("IRONCLAD", "IRONCLAD")]
    [InlineData("SLIME_BOSS", "SLIME_BOSS")]
    public void SafeNamesStayReadable(string value, string expected) =>
        Assert.Equal(expected, ApStorageLayout.SafeSegment(value));

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("Mod:Character")]
    [InlineData(".hidden")]
    [InlineData("")]
    public void UnsafeNamesAreHashed(string value)
    {
        string segment = ApStorageLayout.SafeSegment(value);
        Assert.StartsWith("h", segment);
        Assert.Equal(25, segment.Length);
        Assert.All(segment, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
    }

    private const string CampaignId = "0123456789abcdef0123456789abcdef";

    private void Migrate() =>
        ApStorageMigration.Run(_user, Account, Storage, _ => { }, _warnings.Add);

    private string[] SnapshotFiles() => Directory.GetFiles(_user, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(_user, file) + ":" + File.ReadAllText(file).Length)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private void WriteOutbox(ApSessionIdentity identity, params long[] ids)
    {
        Directory.CreateDirectory(_user);
        File.WriteAllText(Path.Combine(_user, $"sts_ap_pending_checks_v2_{identity.GetFileKey()}.json"),
            JsonSerializer.Serialize(new PendingCheckOutbox
            {
                SchemaVersion = PendingCheckOutbox.CurrentSchemaVersion,
                Identity = identity,
                LocationIds = new SortedSet<long>(ids),
            }));
    }

    /// <summary>The files an older mod version left, as found on a real install.</summary>
    private void WriteLegacyLayout()
    {
        Directory.CreateDirectory(_user);
        File.WriteAllText(Path.Combine(_user, "ap.connection"), """{"SlotName":"Alice"}""" + "\n");
        WriteOutbox(Session, 1, 2);

        // The same slot and character banked on two profiles; profile 2's boss save is newer.
        WriteLegacyBank(1, ("1-boss", "profile 1 boss", -2), ("1-treasure", "profile 1 treasure", -3));
        WriteLegacyBank(2, ("1-boss", "profile 2 boss", -1));

        string campaign = Path.Combine(Account, "modded", "profile1", "saves", ApStorageMigration.LegacyCampaignsFolder, CampaignId);
        Directory.CreateDirectory(campaign);
        File.WriteAllText(Path.Combine(campaign, "metadata.json"), $$"""
            {"SchemaVersion":2,"CampaignId":"{{CampaignId}}","ApRoomSeed":"123","ApTeamId":0,"ApSlotId":1,
             "ApSlotName":"Alice","LastSavedAtUtc":"2026-11-01T10:00:00+00:00"}
            """);
        File.WriteAllText(Path.Combine(campaign, "run-AA.save"), "payload");
        File.WriteAllText(Path.Combine(Account, "modded", "profile1", "saves", "current_run_mp.save"), "native");

        string graphs = Path.Combine(_user, ApStorageMigration.LegacyRunGraphsFolder, "profile-1");
        Directory.CreateDirectory(Path.Combine(graphs, "exports"));
        File.WriteAllText(Path.Combine(graphs, "100.json"), """{"version":1,"start_time":100,"seed":"123","slot_name":"Alice","events_kept":7}""");
        File.WriteAllText(Path.Combine(graphs, "100.last"), "snapshot");
        File.WriteAllText(Path.Combine(graphs, "200.json"), """{"version":1,"start_time":200,"seed":"999","slot_name":"Bob"}""");
        File.WriteAllText(Path.Combine(graphs, "300.last"), "orphan snapshot");
        File.WriteAllText(Path.Combine(graphs, "exports", "100-recap.png"), "png");
    }

    private void WriteLegacyBank(int profile, params (string Key, string Payload, int DaysAgo)[] checkpoints)
    {
        string scratch = Path.Combine(_user, "scratch-" + profile);
        var store = new SingleplayerCheckpointBank(scratch);
        foreach (var (key, payload, _) in checkpoints)
            store.Save(BankKey, key, payload, 10, false);
        string built = store.DirectoryFor(BankKey);
        string manifest = Path.Combine(built, "metadata.json");
        var json = JsonNode.Parse(File.ReadAllText(manifest))!;
        foreach (var (key, _, daysAgo) in checkpoints)
            json["Checkpoints"]![key]!["SavedAt"] = DateTimeOffset.UtcNow.AddDays(daysAgo);
        File.WriteAllText(manifest, json.ToJsonString());

        string legacy = Path.Combine(_user, ApStorageMigration.LegacyCheckpointsFolder, $"profile-{profile}", "ABC" + profile);
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        Directory.Move(built, legacy);
        Directory.Delete(scratch, recursive: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_user))
            Directory.Delete(_user, recursive: true);
    }
}
