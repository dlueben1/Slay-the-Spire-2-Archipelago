using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class RemoteSingleplayerSaveKeyTests
{
    [Fact]
    public void RemoteSavesAreOptInByDefault()
    {
        Assert.False(new ClientSettings().EnableRemoteSingleplayerSaves);
    }

    // Keys must match those already stored on AP servers, including the legacy "_P1" segment.
    [Theory]
    [InlineData("IRONCLAD", "StS2AP_RemoteSingleplayerSave_v1_P1_IRONCLAD")]
    [InlineData("SILENT", "StS2AP_RemoteSingleplayerSave_v1_P1_SILENT")]
    public void EachCharacterHasExactlyOneExistingKey(string character, string expected)
    {
        Assert.Equal(expected, RemoteSingleplayerSaveKey.For(character));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankCharacterIsRejected(string character)
    {
        Assert.Throws<ArgumentException>(() => RemoteSingleplayerSaveKey.For(character));
    }
}
