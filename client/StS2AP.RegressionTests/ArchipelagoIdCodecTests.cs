using StS2AP.Data;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ArchipelagoIdCodecTests
{
    // Blocks above 99 put IDs at or above 1,000,000, where the removed shared-slot
    // co-op partition used to read them as another player's IDs.
    [Theory]
    [InlineData(1)] // Ironclad
    [InlineData(11)] // Custom Character 1
    [InlineData(100)]
    [InlineData(199)] // Last reserved generic block
    [InlineData(200)] // First predefined modded character
    [InlineData(999)] // Last reserved predefined block
    public void CharacterBlocksDecodeAtEveryReservedPosition(long block)
    {
        long relic = block * ArchipelagoIdCodec.BlockSize + 3;
        Assert.False(ArchipelagoIdCodec.IsUniversalItemId(relic));
        Assert.True(ArchipelagoIdCodec.IsCharacterItemId(relic));
        Assert.Equal(block, ArchipelagoIdCodec.GetAPCharacterNumberFromItemId(relic));
        Assert.Equal(3, ArchipelagoIdCodec.GetCharacterItemTypeId(relic));
        Assert.True(ArchipelagoIdCodec.TryComposeLocationId(88, block, out long location));
        Assert.Equal((block - 1) * ArchipelagoIdCodec.BlockSize + 88, location);
        Assert.Equal(88, ArchipelagoIdCodec.GetLocationIdOffset(location));
    }

    [Fact]
    public void UniversalAndArchipelagoReservedIdsAreNotCharacterItems()
    {
        foreach (long id in new[] { 0L, 500L, 600L, 708L, ArchipelagoIdCodec.BlockSize - 1 })
        {
            Assert.True(ArchipelagoIdCodec.IsUniversalItemId(id));
            Assert.False(ArchipelagoIdCodec.IsCharacterItemId(id));
            Assert.False(ArchipelagoIdCodec.IsArchipelagoReservedId(id));
        }

        // Archipelago's own "Nothing" item.
        Assert.True(ArchipelagoIdCodec.IsArchipelagoReservedId(-1));
        Assert.False(ArchipelagoIdCodec.IsUniversalItemId(-1));
        Assert.False(ArchipelagoIdCodec.IsCharacterItemId(-1));
    }
}
