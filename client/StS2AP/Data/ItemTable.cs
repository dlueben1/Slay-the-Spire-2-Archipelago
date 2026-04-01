using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Data
{
    public static class ItemTable
    {
        public enum APItem
        {
            CAWCAW = 0,
            CardReward = 1,
            RareCardReward = 2,
            Relic = 3,
            BossRelic = 4,
            OneGold = 5,
            FiveGold = 6,
            ProgressiveRest = 7,
            ProgressiveSmith = 8,
            ShopCardSlot = 9,
            NeutralShopCardSlot = 10,
            ShopRelicSlot = 11,
            ShopPotionSlot = 12,
            ProgressiveShopRemove = 13,
            Unlock = 14,
            _15Gold = 15,
            _30Gold = 16,
            BossGold = 17,
            Potion = 18,
            AscensionDown = 19
        }

        public static Dictionary<int, string> Items = new Dictionary<int, string>
        {
            { 0, "CAW CAW" },
            { 1, "Card Reward" },
            { 2, "Rare Card Reward" },
            { 3, "Relic" },
            { 4, "Boss Relic" },
            { 5, "One Gold" },
            { 6, "Five Gold" },
            { 7, "Progressive Rest" },
            { 8, "Progressive Smith" },
            { 9, "Shop Card Slot" },
            { 10, "Neutral Shop Card Slot" },
            { 11, "Shop Relic Slot" },
            { 12, "Shop Potion Slot" },
            { 13, "Progressive Shop Remove" },
            { 14, "Unlock" },
            { 15, "15 Gold" },
            { 16, "30 Gold" },
            { 17, "Boss Gold" },
            { 18, "Potion" },
            { 19, "Ascension Down" }
        };

        /// <summary>
        /// Maps AP Items to the amount of gold they give
        /// </summary>
        public static Dictionary<APItem, int> GoldItemAmounts = new Dictionary<APItem, int>
        {
            { APItem.OneGold, 1 },
            { APItem.FiveGold, 5 },
            { APItem._15Gold, 15 },
            { APItem._30Gold, 30 },
            { APItem.BossGold, 100 }
        };
    }
}