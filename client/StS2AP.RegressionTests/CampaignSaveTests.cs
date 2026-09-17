using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class CampaignSaveTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "StS2AP-save-tests-" + Guid.NewGuid().ToString("N"));

    public CampaignSaveTests() => Directory.CreateDirectory(_directory);

    private (string FileName, string Hash) Store(string contents)
    {
        string source = Path.Combine(_directory, "native.save");
        File.WriteAllText(source, contents);
        return CampaignSaveFiles.Store(_directory, source);
    }

    [Fact]
    public void SavePayloadRoundTripsWithItsChecksum()
    {
        var save = Store("floor one");
        Assert.Null(CampaignSaveFiles.Verify(_directory, save.FileName, save.Hash));
        Assert.Equal("floor one", File.ReadAllText(Path.Combine(_directory, save.FileName)));
    }

    [Fact]
    public void NewRecoveryPayloadDoesNotOverwriteCheckpoint()
    {
        var checkpoint = Store("treasure checkpoint");
        var recovery = Store("next floor");
        Assert.NotEqual(checkpoint.FileName, recovery.FileName);
        Assert.Equal("treasure checkpoint", File.ReadAllText(Path.Combine(_directory, checkpoint.FileName)));
        Assert.Null(CampaignSaveFiles.Verify(_directory, checkpoint.FileName, checkpoint.Hash));
        Assert.Null(CampaignSaveFiles.Verify(_directory, recovery.FileName, recovery.Hash));
    }

    [Fact]
    public void UnpublishedPayloadLeavesPriorSaveReferenceUsable()
    {
        var previous = Store("old payload");
        Store("unpublished payload");
        Assert.Null(CampaignSaveFiles.Verify(_directory, previous.FileName, previous.Hash));
    }

    [Fact]
    public void IdenticalPayloadsShareImmutableBytesAndLeaveNoTemporaryFiles()
    {
        var first = Store("same payload");
        var second = Store("same payload");
        Assert.Equal(first, second);
        Assert.Single(Directory.GetFiles(_directory, "run-*.save"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp-*"));
    }

    [Fact]
    public void CorruptRecoveryDoesNotInvalidateCheckpoint()
    {
        var checkpoint = Store("checkpoint");
        var recovery = Store("recovery");
        File.WriteAllText(Path.Combine(_directory, recovery.FileName), "corrupt");
        Assert.NotNull(CampaignSaveFiles.Verify(_directory, recovery.FileName, recovery.Hash));
        Assert.Null(CampaignSaveFiles.Verify(_directory, checkpoint.FileName, checkpoint.Hash));
    }

    [Fact]
    public void MissingSnapshotIsReported()
    {
        string hash = new('A', 64);
        Assert.NotNull(CampaignSaveFiles.Verify(_directory, $"run-{hash}.save", hash));
    }

    [Theory]
    [InlineData("../outside.save")]
    [InlineData("..\\outside.save")]
    [InlineData("C:\\outside.save")]
    [InlineData("anything.save")]
    [InlineData("multiplayer_run.save")]
    public void SnapshotNamesCannotEscapeOrReplaceAnotherFile(string name) =>
        Assert.Throws<InvalidDataException>(() =>
            CampaignSaveFiles.GetPath(_directory, name, new string('A', 64)));

    public void Dispose()
    {
        foreach (string file in Directory.GetFiles(_directory))
            File.Delete(file);
        Directory.Delete(_directory, recursive: false);
    }
}
