namespace StS2AP.Data
{
    /// <summary>
    /// Defines the 10,000-ID block layout shared by character items and locations.
    /// Character items use one-based blocks; character locations use zero-based blocks.
    /// </summary>
    public static class ArchipelagoIdCodec
    {
        public const long BlockSize = 10000L;
        public const long PlayerBlockSize = 1000000L;

        public static int GetPlayerNumber(long id) => id < 0 ? 0 : checked((int)(id / PlayerBlockSize) + 1);
        public static long WithoutPlayer(long id) => id < 0 ? id : id % PlayerBlockSize;
        public static long ForPlayer(long id, int playerNumber)
        {
            if (playerNumber is < 1 or > 4)
                throw new ArgumentOutOfRangeException(nameof(playerNumber));
            return id < 0 ? id : WithoutPlayer(id) + (playerNumber - 1) * PlayerBlockSize;
        }
        public static string PlayerName(string name, int playerNumber) =>
            playerNumber == 1 ? name : $"P{playerNumber} {name}";

        public static bool IsUniversalItemId(long itemId)
        {
            return itemId >= 0 && WithoutPlayer(itemId) < BlockSize;
        }

        public static bool IsCharacterItemId(long itemId)
        {
            return WithoutPlayer(itemId) >= BlockSize;
        }

        public static long GetCharacterItemTypeId(long itemId)
        {
            return itemId % BlockSize;
        }

        public static long GetAPCharacterNumberFromItemId(long itemId)
        {
            return WithoutPlayer(itemId) / BlockSize;
        }

        public static long GetBaseLocationId(long locationId)
        {
            return locationId % BlockSize;
        }

        public static bool TryComposeLocationId(
            long baseLocationId,
            long apCharacterNumber,
            out long locationId
        )
        {
            if (
                baseLocationId < 0
                || baseLocationId >= BlockSize
                || apCharacterNumber < 1
            )
            {
                locationId = -1;
                return false;
            }

            locationId = ((apCharacterNumber - 1) * BlockSize) + baseLocationId;
            return true;
        }
    }
}
