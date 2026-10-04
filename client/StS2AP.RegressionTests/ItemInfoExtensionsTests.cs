using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using StS2AP.Data;
using Xunit;

namespace StS2AP.RegressionTests
{
    public sealed class ItemInfoExtensionsTests
    {
        [Fact]
        public void MixedReceiptsDecodeUniversalItemsWithoutLoggingErrors()
        {
            foreach (int player in new[] { 1, 2, 3, 4 })
            {
                foreach (long id in new[] { 500L, 600L })
                    Assert.Equal(0L, Receipt(ArchipelagoIdCodec.ForPlayer(id, player)).GetAPCharacterNumber());
                Assert.Equal(1L, Receipt(ArchipelagoIdCodec.ForPlayer(10018, player)).GetAPCharacterNumber());
                Assert.Equal(2L, Receipt(ArchipelagoIdCodec.ForPlayer(20003, player)).GetAPCharacterNumber());
            }
            Assert.Throws<InvalidOperationException>(() => Receipt(-1).GetAPCharacterNumber());
            Assert.Throws<InvalidOperationException>(() => ItemInfoExtensions.GetAPCharacterNumber(null!));
        }

        private static ItemInfo Receipt(long id) => new(
            new NetworkItem { Item = id, Location = 50, Player = 1 },
            "Slay the Spire 2", "Slay the Spire 2", null!, new PlayerInfo());
    }
}

// Fail on unexpected production error logging without loading the game's logger.
internal static class LogUtility
{
    public static void Error(string message) => throw new InvalidOperationException(message);
}
