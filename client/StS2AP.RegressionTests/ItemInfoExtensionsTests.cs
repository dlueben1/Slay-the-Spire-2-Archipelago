using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Xunit;

namespace StS2AP.RegressionTests
{
    public sealed class ItemInfoExtensionsTests
    {
        [Fact]
        public void MixedReceiptsDecodeUniversalItemsWithoutLoggingErrors()
        {
            foreach (long id in new[] { 500L, 600L })
                Assert.Equal(0L, Receipt(id).GetAPCharacterNumber());
            Assert.Equal(1L, Receipt(10018).GetAPCharacterNumber());
            Assert.Equal(2L, Receipt(20003).GetAPCharacterNumber());
            // Predefined modded characters start at block 200, above 1,000,000.
            Assert.Equal(211L, Receipt(2110004).GetAPCharacterNumber());
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
