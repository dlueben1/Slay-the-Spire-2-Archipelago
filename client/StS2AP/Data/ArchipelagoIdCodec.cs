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

        /// <summary>
        /// First ID of the bingo square locations. They sit in block 1000, above every character
        /// block (modded characters are reserved through block 999), so they are never a
        /// character's location even though the block arithmetic below would accept them.
        /// </summary>
        public const long BingoLocationBase = 9_990_000L;

        public static bool IsBingoLocationId(long locationId) =>
            locationId >= BingoLocationBase && locationId < BingoLocationBase + BlockSize;

        /// <summary>
        /// Whether a location belongs to a character's block. Only these may be mapped to a
        /// character with <see cref="GetAPCharacterNumberFromLocationId"/> or
        /// <see cref="GetLocationIdOffset"/>.
        /// </summary>
        public static bool IsCharacterLocationId(long locationId) =>
            locationId >= 0 && locationId < BingoLocationBase;

        public static long GetLocationIdOffset(long locationId)
        {
            return locationId % BlockSize;
        }

        public static long GetAPCharacterNumberFromLocationId(long locationId)
        {
            return (locationId / BlockSize) + 1;
        }

        public static bool TryComposeLocationId(
            long locationIdOffset,
            long apCharacterNumber,
            out long locationId
        )
        {
            if (
                locationIdOffset < 0
                || locationIdOffset >= BlockSize
                || apCharacterNumber < 1
            )
            {
                locationId = -1;
                return false;
            }

            locationId = ((apCharacterNumber - 1) * BlockSize) + locationIdOffset;
            return true;
        }
    }
}
