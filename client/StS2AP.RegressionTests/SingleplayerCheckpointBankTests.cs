using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class SingleplayerCheckpointBankTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ap-checkpoint-bank-" + Guid.NewGuid().ToString("N"));
    private readonly SingleplayerCheckpointBank.BankKey _key = new(new("seed", 0, 1), "IRONCLAD");
    private SingleplayerCheckpointBank Store => new(_root);
    private string BankDirectory => Directory.GetDirectories(_root).Single();

    [Fact]
    public void FreshAttemptUsesSameSixPositionsAndOnlyOverwritesReachedBoundary()
    {
        foreach (string key in SingleplayerCheckpointBank.Milestones)
            Store.Save(_key, key, "old " + key, 1, true);
        // A new attempt/restart reconstructs the key; there is no playthrough identity.
        var nextAttempt = new SingleplayerCheckpointBank.BankKey(new("seed", 0, 1), "IRONCLAD");
        var restarted = new SingleplayerCheckpointBank(_root);
        Assert.Equal(6, restarted.Read(nextAttempt).Checkpoints.Count);
        restarted.Save(nextAttempt, "1-ancient", "new start", 0, false);
        Assert.Equal("new start", Store.Load(_key, "1-ancient").Payload);
        Assert.Equal("old 1-boss", Store.Load(_key, "1-boss").Payload);
        Assert.Equal("old 3-treasure", Store.Load(_key, "3-treasure").Payload);
        Assert.Single(Directory.GetDirectories(_root));
        Assert.Equal(6, Directory.GetFiles(BankDirectory, "run-*.save").Length);
    }

    [Fact]
    public void LoadingEarlierCheckpointDoesNotWriteOrRemoveLaterPositions()
    {
        Store.Save(_key, "1-boss", "early", 17, true);
        Store.Save(_key, "3-treasure", "late", 40, true);
        string manifest = Path.Combine(BankDirectory, "metadata.json");
        string before = File.ReadAllText(manifest);
        Assert.Equal("early", Store.Load(_key, "1-boss").Payload);
        Assert.Equal(before, File.ReadAllText(manifest));
        Store.Save(_key, "1-boss", "replayed", 17, true);
        Assert.Equal("replayed", Store.Load(_key, "1-boss").Payload);
        Assert.Equal("late", Store.Load(_key, "3-treasure").Payload);
    }

    [Fact]
    public void AncientPolicyBelongsToEachSnapshotAcrossMixedAttempts()
    {
        Store.Save(_key, "2-treasure", "start of act attempt", 25, true);
        Store.Save(_key, "1-boss", "anytime attempt", 17, false);
        Assert.True(Store.Load(_key, "2-treasure").Snapshot.StartOfAct);
        Assert.False(Store.Load(_key, "1-boss").Snapshot.StartOfAct);
        Store.Save(_key, "2-treasure", "reached again", 25, false);
        Assert.False(Store.Load(_key, "2-treasure").Snapshot.StartOfAct);
    }

    [Fact]
    public void SeedTeamSlotPlayerAndCharacterEachHaveIndependentBanks()
    {
        Store.Save(_key, "1-boss", "original", 17, true);
        var others = new[]
        {
            _key with { Owner = _key.Owner with { Seed = "other" } },
            _key with { Owner = _key.Owner with { Team = 1 } },
            _key with { Owner = _key.Owner with { Slot = 2 } },
            _key with { Character = "SILENT" },
            _key with { Owner = _key.Owner with { PlayerNumber = 2 } },
        };
        foreach (var other in others)
        {
            Assert.Empty(Store.Read(other).Checkpoints);
            Assert.Throws<InvalidDataException>(() => Store.Load(other, "1-boss"));
            Store.Save(other, "1-boss", "other", 17, false);
        }
        Assert.Equal(6, Directory.GetDirectories(_root).Length);
        Assert.Equal("original", Store.Load(_key, "1-boss").Payload);
    }

    [Fact]
    public void PlayerOneCanReadCheckpointMetadataWrittenBeforeNumberedPlayers()
    {
        Store.Save(_key, "1-boss", "existing checkpoint", 17, true);
        // The old bank hash used precisely this shape, without PlayerNumber.
        var oldKey = new { Owner = new { Seed = "seed", Team = 0, Slot = 1 }, Character = "IRONCLAD" };
        string oldDirectory = Path.Combine(_root, Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(oldKey))));
        Assert.Equal(oldDirectory, BankDirectory);
        string manifest = Path.Combine(oldDirectory, "metadata.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifest))!;
        json["Identity"]!["Owner"]!.AsObject().Remove("PlayerNumber");
        File.WriteAllText(manifest, json.ToJsonString());
        Assert.Equal("existing checkpoint", new SingleplayerCheckpointBank(_root).Load(_key, "1-boss").Payload);
        Assert.Empty(Store.Read(_key with { Owner = _key.Owner with { PlayerNumber = 2 } }).Checkpoints);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void InvalidPlayerNumberCannotCreateCheckpoint(int number)
    {
        var invalid = _key with { Owner = _key.Owner with { PlayerNumber = number } };
        Assert.Throws<InvalidDataException>(() => Store.Save(invalid, "1-boss", "invalid", 17, true));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void MissingBankReadCreatesNothingAndNativeSaveRemainsUntouched()
    {
        Assert.Empty(Store.Read(_key).Checkpoints);
        Assert.False(Directory.Exists(_root));
        Directory.CreateDirectory(_root);
        string native = Path.Combine(_root, "current_run.save");
        File.WriteAllText(native, "unrelated modded save");
        Store.Save(_key, "1-boss", "AP", 17, true);
        Assert.Equal("unrelated modded save", File.ReadAllText(native));
    }

    [Fact]
    public void CorruptCheckpointDoesNotDamageOtherPositions()
    {
        Store.Save(_key, "1-boss", "boss", 17, true);
        Store.Save(_key, "1-treasure", "treasure", 8, true);
        var snapshot = Store.Read(_key).Checkpoints["1-boss"];
        File.WriteAllText(Path.Combine(BankDirectory, snapshot.FileName), "broken");
        Assert.Throws<InvalidDataException>(() => Store.Load(_key, "1-boss"));
        Assert.Equal("treasure", Store.Load(_key, "1-treasure").Payload);
    }

    [Fact]
    public void CorruptManifestCannotBeSilentlyReplacedByNextAttempt()
    {
        Store.Save(_key, "1-boss", "boss", 17, true);
        var snapshot = Store.Read(_key).Checkpoints["1-boss"];
        string manifest = Path.Combine(BankDirectory, "metadata.json");
        File.WriteAllText(manifest, "{");
        Assert.Throws<System.Text.Json.JsonException>(() => Store.Save(_key, "1-ancient", "new", 0, false));
        Assert.Equal("{", File.ReadAllText(manifest));
        Assert.Equal("boss", File.ReadAllText(Path.Combine(BankDirectory, snapshot.FileName)));
    }

    [Theory]
    [InlineData("1-ancient", true, 0, true)]
    [InlineData("1-boss", true, 1, true)]
    [InlineData("2-treasure", true, 1, false)]
    [InlineData("2-boss", true, 1, false)]
    [InlineData("2-treasure", true, 2, true)]
    [InlineData("3-treasure", true, 2, false)]
    [InlineData("3-treasure", true, 3, true)]
    [InlineData("3-treasure", false, 0, true)]
    [InlineData("2-ancient", false, 3, false)]
    [InlineData("3-ancient", false, 3, false)]
    [InlineData("3-boss", false, 3, false)]
    public void MilestonesPreserveBossPriorityAndAncientGates(string key, bool start, int unlocked, bool expected) =>
        Assert.Equal(expected, SingleplayerCheckpointBank.IsAllowed(key, start, unlocked));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
