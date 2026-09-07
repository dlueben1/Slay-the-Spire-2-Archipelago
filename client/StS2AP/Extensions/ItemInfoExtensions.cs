using Archipelago.MultiClient.Net.Models;
using StS2AP.Data;
using static StS2AP.Data.ItemTable;

namespace StS2AP
{
    public static class ItemInfoExtensions
    {
        /// <summary>
        /// Extracts the character item type from a character-specific item ID.
        /// Example: An item with ID 20003 represents an item from the Silent (Character ID: 2)
        /// where the character-specific item ID is (3).
        /// </summary>
        public static APItem GetCharacterItemType(this ItemInfo item)
        {
            if (item is null || !ArchipelagoIdCodec.IsCharacterItemId(item.ItemId))
            {
                LogUtility.Error($"Could not Parse Raw Item ID for Item #{item?.ItemId}");
                return 0L;
            }
            return (APItem)ArchipelagoIdCodec.GetCharacterItemTypeId(item.ItemId);
        }

        /// <summary>
        /// Extracts the one-based AP character number from a character-specific item ID.
        /// </summary>
        public static long GetAPCharacterNumber(this ItemInfo item)
        {
            if (item is null || !ArchipelagoIdCodec.IsCharacterItemId(item.ItemId))
            {
                LogUtility.Error(
                    $"Could not parse AP character number from item #{item?.ItemId}"
                );
                return 0L;
            }
            return ArchipelagoIdCodec.GetAPCharacterNumberFromItemId(item.ItemId);
        }

        /// <summary>
        /// Reads a complete universal item ID without applying character-block decoding.
        /// </summary>
        public static APItem GetUniversalItemId(this ItemInfo item)
        {
            if (item is null || !ArchipelagoIdCodec.IsUniversalItemId(item.ItemId))
            {
                LogUtility.Error($"Could not parse universal item ID from item #{item?.ItemId}");
                return 0L;
            }
            return (APItem)ArchipelagoIdCodec.WithoutPlayer(item.ItemId);
        }

        public static bool Advancement(this ItemInfo info)
        {
            return (info.Flags & Archipelago.MultiClient.Net.Enums.ItemFlags.Advancement) > 0;
        }

        public static bool Useful(this ItemInfo info)
        {
            return (info.Flags & Archipelago.MultiClient.Net.Enums.ItemFlags.NeverExclude) > 0;
        }

        public static bool Trap(this ItemInfo info)
        {
            return (info.Flags & Archipelago.MultiClient.Net.Enums.ItemFlags.Trap) > 0;
        }
    }
}
