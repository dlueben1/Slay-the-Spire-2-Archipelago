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

    [Theory]
    [InlineData(1, "IRONCLAD", "StS2AP_RemoteSingleplayerSave_v1_P1_IRONCLAD")]
    [InlineData(2, "IRONCLAD", "StS2AP_RemoteSingleplayerSave_v1_P2_IRONCLAD")]
    [InlineData(1, "SILENT", "StS2AP_RemoteSingleplayerSave_v1_P1_SILENT")]
    public void CharacterAndPlayerHaveExactlyOneIndependentKey(
        int playerNumber,
        string character,
        string expected)
    {
        Assert.Equal(expected, RemoteSingleplayerSaveKey.For(character, playerNumber));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void InvalidPlayerNumberIsRejected(int playerNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RemoteSingleplayerSaveKey.For("IRONCLAD", playerNumber)
        );
    }
}
