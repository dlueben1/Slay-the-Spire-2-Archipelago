using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Models;
using static StS2AP.Data.CharTable;
using static StS2AP.Data.ItemTable;

namespace StS2AP
{
    public static class ItemInfoExtensions
    {
        /// <summary>
        /// Extracts the character-specific item ID from the ItemInfo object by stripping the character offset.
        /// Only valid for character-specific items (ItemId > 10000). For universal items (ItemId < 10000),
        /// cast ItemId directly to APItem instead — there is no character offset to strip.
        /// Example: An item with ID 20003 represents an item from the Silent (Character ID: 2)
        /// where the character-specific item ID is (3).
        /// </summary>
        public static APItem GetCharacterSpecificItemID(this ItemInfo item)
        {
            if (item is null || item.ItemId < 0)
            {
                LogUtility.Error($"Could not Parse Raw Item ID for Item #{item?.ItemId}");
                return 0L;
            }
            return (APItem)(item.ItemId % 10000L);
        }

        // /// <summary>
        // /// Similar to the function above, but it extracts the Character ID of the Item from the ItemInfo object.
        // /// </summary>
        // public static APItemCharID GetStSCharID(this ItemInfo item)
        // {
        //     if (item is null || item.ItemId < 0)
        //     {
        //         LogUtility.Error($"Could not Parse Raw Character ID for Item #{item?.ItemId}");
        //         return 0L;
        //     }
        //     return (APItemCharID)(Math.Abs(item.ItemId) / 10000L);
        // }

        public static long GetCharacterOffset(this ItemInfo item)
        {
            if (item is null || item.ItemId < 0)
            {
                LogUtility.Error(
                    $"Could not Parse Raw Character offset ID for Item #{item?.ItemId}"
                );
                return 0L;
            }
            return (Math.Abs(item.ItemId) / 10000L);
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
