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
            SwarmingElites = 19,
            WearyTraveler = 20,
            Poverty = 21,
            TightBelt = 22,
            AscenderBane = 23,
            Inflation = 24,
            Scarcity = 25,
            ToughEnemies = 26,
            DeadlyEnemies = 27,
            DoubleBoss = 28,

            /// ── Ephemeral Buff items (universal / character-agnostic) ──────────────────
            /// These are one-time-use filler items that apply a temporary in-combat buff
            /// when received. Unlike run rewards (gold, cards, relics), buffs are never
            /// reapplied on subsequent runs. Consumption is tracked permanently in the
            /// Archipelago server's DataStorage. IDs match universal_items in items.py.
            FreeAttack = 500,
            FreePower = 501,
            FreeSkill = 502,
            Dexterity = 503,
            Strength = 504,
            Plating = 505,
            Friendship = 506,
            PostCombatCardUpgrade = 507,
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
            { 19, "Swarming Elites" },
            { 20, "Weary Traveler" },
            { 21, "Poverty" },
            { 22, "Tight Belt" },
            { 23, "Ascender's Bane" },
            { 24, "Inflation" },
            { 25, "Scarcity" },
            { 26, "Tough Enemies" },
            { 27, "Deadly Enemies" },
            { 28, "Double Boss" },
            { 500, "Free Attack" },
            { 501, "Free Power" },
            { 502, "Free Skill" },
            { 503, "Dexterity" },
            { 504, "Strength" },
            { 505, "Plating" },
            { 506, "Friendship" },
            { 507, "Post-Combat Card Upgrade" },
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
            { APItem.BossGold, 100 },
        };
    }
}
