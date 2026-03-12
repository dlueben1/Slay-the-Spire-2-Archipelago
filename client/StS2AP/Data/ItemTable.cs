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
            _2Gold = 900001,
            _5Gold = 900002,
            _25Gold = 900003,
            CardReward = 900004,
            RareCardReward = 900005,
            RelicReward = 900006,
            PotionFiller = 900007,
            PotionReward = 900008,
            Ironclad = 900009,
            Silent = 900010,
            Defect = 900011,
            Regent = 900012,
            Necrobinder = 900013,
            Victory = 900014
        }

        public static Dictionary<int, string> Items = new Dictionary<int, string>
        {
            { 900001, "2 Gold" },
            { 900002, "5 Gold" },
            { 900003, "25 Gold" },
            { 900004, "Card Reward" },
            { 900005, "Rare Card Reward" },
            { 900006, "Relic Reward" },
            { 900007, "Potion Filler" },
            { 900008, "Potion Reward" },
            { 900009, "Ironclad" },
            { 900010, "Silent" },
            { 900011, "Defect" },
            { 900012, "Regent" },
            { 900013, "Necrobinder" },
            { 900014, "Victory" }
        };

    }
}