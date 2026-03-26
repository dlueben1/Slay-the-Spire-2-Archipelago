using Archipelago.MultiClient.Net.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Models
{
    public class IndexedItemInfo
    {
        /// <summary>
        /// The Item Info from Archipelago
        /// </summary>
        public ItemInfo Item { get; set; }

        /// <summary>
        /// The received Index of the Item, the only true unique way to handle this
        /// </summary>
        public int Index { get; set; }

        public IndexedItemInfo(ItemInfo item, int index)
        {
            Item = item;
            Index = index;
        }
    }
}
