using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Text.RegularExpressions;
using Archipelago.MultiClient.Net.Models;
using static StS2AP.Data.ItemTable;
using static StS2AP.Data.CharTable;

namespace StS2AP
{
    public static class ItemInfoExtensions
    {
        /// <summary>
        /// Extracts the ID of the Item from the ItemInfo object.
        /// It's a simple algorithm: only the last four digits of the number matter for the Item ID, the rest of the digits represent the Character ID of the item.
        /// Example: An item with ID 20003 represents an item from the Silent (Character ID: 2) where the raw ID of the item is (3).
        /// </summary>
        public static APItem GetRawItemID(this ItemInfo item)
        {
            if (item is null || item.ItemId < 0)
            {
                LogUtility.Error($"Could not Parse Raw Item ID for Item #{item?.ItemId}");
                return 0L;
            }
            return (APItem)(item.ItemId % 10000L);
        }

        /// <summary>
        /// Similar to the function above, but it extracts the Character ID of the Item from the ItemInfo object.
        /// </summary>
        public static APItemCharID GetStSCharID(this ItemInfo item)
        {
            if (item is null || item.ItemId < 0)
            {
                LogUtility.Error($"Could not Parse Raw Character ID for Item #{item?.ItemId}");
                return 0L;
            }
            return (APItemCharID)(Math.Abs(item.ItemId) / 10000L);
        }
    }
}
