using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using StS2AP.Data;
using StS2AP.Extensions;
using StS2AP.UI;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    public static class Patches_APProgressOnCharSelect
    {
        /// <summary>
        /// When the Player selects a character, update the Archipelago Progres panel with information on that character
        /// </summary>
        [HarmonyPatch(typeof(NCharacterSelectScreen))]
        public static class UpdateCharTrackerUI
        {
            [HarmonyPatch("SelectCharacter")]
            [HarmonyPostfix]
            public static void Postfix(NCharacterSelectScreen __instance, NCharacterSelectButton charSelectButton, CharacterModel characterModel)
            {
                // Get Character ID
                var id = characterModel.GetAPItemCharID();
                LogUtility.Info($"Selected Character: {characterModel.APName()}, AP Char ID: {(id.HasValue ? id.Value.ToString() : "null")}");

                // If (somehow) the character ID is null, stop
                if (!id.HasValue) return;

                // Update Gold Rewards
                LogUtility.Info($"Checking for gold rewards for character ID {id.Value}");
                if (ArchipelagoClient.Progress.GoldReceived.TryGetValue(id.Value, out int gold))
                {
                    LogUtility.Info($"Found gold rewards for character ID {id.Value}: {gold}");
                    ArchipelagoCharTrackerUI.GoldRewards?.SetText(gold.ToString());
                }
                else
                {
                    LogUtility.Error($"No gold rewards found for character ID {id.Value}");
                    ArchipelagoCharTrackerUI.GoldRewards?.SetText("0");
                }

                // Update Progressive Smiths/Rests
                ArchipelagoCharTrackerUI.ProgressiveRestLabel?.SetText($"({ArchipelagoClient.Progress.MaxRestLevel(id.Value) ?? 0} / 3)");
                ArchipelagoCharTrackerUI.ProgressiveSmithLabel?.SetText($"({ArchipelagoClient.Progress.MaxSmithLevel(id.Value) ?? 0} / 3)");

                // Count Card/Relic/Potion/Progressive Rewards
                var itemCounts = ArchipelagoClient.Progress.AllReceivedItems
                    .Where(i => i.Item.GetStSCharID() == id.Value)
                    .GroupBy(i => i.Item.GetRawItemID())
                    .ToDictionary(
                        g => g.Key,
                        g => g.Count());

                // Update Card Rewards
                if (itemCounts.TryGetValue(ItemTable.APItem.CardReward, out int cardCount))
                {
                    ArchipelagoCharTrackerUI.CardRewards?.SetText(cardCount.ToString());
                }
                else
                {
                    ArchipelagoCharTrackerUI.CardRewards?.SetText("0");
                }

                // Update Rare Card Rewards
                if (itemCounts.TryGetValue(ItemTable.APItem.RareCardReward, out int rareCardCount))
                {
                    ArchipelagoCharTrackerUI.RareCardRewards?.SetText(rareCardCount.ToString());
                }
                else
                {
                    ArchipelagoCharTrackerUI.RareCardRewards?.SetText("0");
                }

                // Update Relic Rewards (both regular and boss relics)
                var relicCount = (itemCounts.TryGetValue(ItemTable.APItem.Relic, out int relicStandard) ? relicStandard : 0) +
                                 (itemCounts.TryGetValue(ItemTable.APItem.BossRelic, out int relicBoss) ? relicBoss : 0);
                ArchipelagoCharTrackerUI.RelicRewards?.SetText(relicCount.ToString());

                // Update Potion Rewards
                if (itemCounts.TryGetValue(ItemTable.APItem.Potion, out int potionCount))
                {
                    ArchipelagoCharTrackerUI.PotionRewards?.SetText(potionCount.ToString());
                }
                else
                {
                    ArchipelagoCharTrackerUI.PotionRewards?.SetText("0");
                }
            }
        }
    }
}
