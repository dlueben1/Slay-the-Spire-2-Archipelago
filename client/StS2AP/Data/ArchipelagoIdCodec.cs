namespace StS2AP.Data
{
    /// <summary>
    /// Defines the 10,000-ID block layout shared by character items and locations.
    /// Character items use one-based blocks; character locations use zero-based blocks.
    /// Blocks have no upper bound here: the APWorld places predefined modded characters
    /// from block 200, so IDs of 1,000,000 or more are ordinary character IDs.
    /// </summary>
    public static class ArchipelagoIdCodec
    {
        public const long BlockSize = 10000L;

        /// <summary>
        /// Archipelago reserves negative IDs for its own "Nothing" item and server locations.
        /// </summary>
        public static bool IsArchipelagoReservedId(long id) => id < 0;

        public static bool IsUniversalItemId(long itemId)
        {
            return itemId >= 0 && itemId < BlockSize;
        }

        public static bool IsCharacterItemId(long itemId)
        {
            return itemId >= BlockSize;
        }

        public static long GetCharacterItemTypeId(long itemId)
        {
            return itemId % BlockSize;
        }

        public static long GetAPCharacterNumberFromItemId(long itemId)
        {
            return itemId / BlockSize;
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
