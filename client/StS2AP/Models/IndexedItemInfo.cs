using Archipelago.MultiClient.Net.Models;

namespace StS2AP.Models
{
    public sealed class IndexedItemInfo
    {
        /// <summary>
        /// The Item Info from Archipelago
        /// </summary>
        public ItemInfo Item { get; }

        /// <summary>
        /// The SDK receipt index, unique only within the owning AP session's item history.
        /// Repeated copies of the same item have different indexes.
        /// </summary>
        public int Index { get; }

        public IndexedItemInfo(ItemInfo item, int index)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            Item = item;
            Index = index;
        }
    }
}
