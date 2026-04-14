using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using StS2AP.Data;
using StS2AP.Extensions;
using StS2AP.Models;
using StS2AP.UI;
using StS2AP.UI.Components;

namespace StS2AP.Patches
{
    public static class Patches_APProgressOnCharSelect
    {
        /// <summary>
        /// When the Player selects a character, update the Archipelago Progres panels with information on that character
        /// </summary>
        [HarmonyPatch(typeof(NCharacterSelectScreen))]
        public static class UpdateCharTrackerUI
        {
            [HarmonyPatch("SelectCharacter")]
            [HarmonyPostfix]
            public static void Postfix(NCharacterSelectScreen __instance, NCharacterSelectButton charSelectButton, CharacterModel characterModel)
            {
                ArchipelagoCharTrackerUI.Show();
                ArchipelagoGoalTrackerUI.Show();
                UpdateReceivedItems(characterModel);
                UpdateCheckedLocations(characterModel);
                ArchipelagoGoalTrackerUI.UpdateGoalProgress();
            }

            /// <summary>
            /// Updates the Found/Checked Locations in the UI for the currently selected character.
            /// Shows/Hides items based on what settings you are using for this run.
            /// </summary>
            public static void UpdateCheckedLocations(CharacterModel character)
            {
                // Update Card Locations
                var cardLocations = LocationData.GetCardRewardLocations(character);
                SetCheckedLocation(ArchipelagoCharTrackerUI.CardChecks, cardLocations, ArchipelagoProgress._maxCardRewards / (ArchipelagoClient.Settings.ShouldShuffleAllCards ? 1 : 2));

                // Update Rare Card Locations
                var rareCardLocations = LocationData.GetRareCardRewardLocations(character);
                SetCheckedLocation(ArchipelagoCharTrackerUI.RareCardChecks, rareCardLocations, ArchipelagoProgress._maxRareCardRewards);

                // Update Relic Locations
                var relicLocations = LocationData.GetRelicRewardLocations(character);
                SetCheckedLocation(ArchipelagoCharTrackerUI.RelicChecks, relicLocations, ArchipelagoProgress._maxRelicRewards);

                // Update Floorsanity Locations
                if (ArchipelagoClient.Settings.Floorsanity)
                {
                    var floorLocations = LocationData.GetFloorsanityLocations(character);
                    SetCheckedLocation(ArchipelagoCharTrackerUI.FloorsanityChecks, floorLocations, ArchipelagoProgress._maxFloorRewards);
                }

                // Update Campfiresanity Locations
                if (ArchipelagoClient.Settings.CampfireSanity)
                {
                    var campfireLocations = LocationData.GetCampfiresanityLocations(character);
                    SetCheckedLocation(ArchipelagoCharTrackerUI.CampfiresanityChecks, campfireLocations, ArchipelagoProgress._maxCampfireChecks);
                }

                // Update Goldsanity Locations
                if (ArchipelagoClient.Settings.GoldSanity)
                {
                    var goldLocations = LocationData.GetGoldsanityLocations(character);
                    SetCheckedLocation(ArchipelagoCharTrackerUI.GoldsanityChecks, goldLocations, ArchipelagoProgress._maxGoldRewards);
                }

                // Update Potionsanity Locations
                if (ArchipelagoClient.Settings.PotionSanity)
                {
                    var potionLocations = LocationData.GetPotionsanityLocations(character);
                    SetCheckedLocation(ArchipelagoCharTrackerUI.PotionsanityChecks, potionLocations, ArchipelagoProgress._maxPotionRewards);
                }

                // Update Press Start State
                var hasPressStart = LocationData.DoesThisCharacterHavePressStartLocation(character);
                var hasStarted = ArchipelagoClient.CheckedLocations.Contains(LocationData.GetPressStartLocation(character));
                var pressStartText = hasPressStart ? (hasStarted ? "[green][sine]✓[/sine][/green]" : "[red]—[/red]") : "N/A";
                ArchipelagoCharTrackerUI.PressStartCheck?.SetText(pressStartText);

                // Update Goal State
                ArchipelagoCharTrackerUI.ClearedCheck?.SetText(character.HasCleared() ? "[green][sine]✓[/sine][/green]" : "[red]—[/red]");
            }

            /// <summary>
            /// Updates the text of the given ItemCountLabel to show how many of the given locations have
            /// been checked off by the player, out of the total number of those locations for this character.
            /// </summary>
            /// <param name="component">The UI component to update.</param>
            /// <param name="locations">The list of locations to check against what we've found so far.</param>
            /// <param name="totalCount">The total number of locations for this character.</param>
            private static void SetCheckedLocation(ItemCountLabel? component, List<long> locations, int totalCount)
            {
                var checkedLocations = ArchipelagoClient.CheckedLocations.Intersect(locations).ToList();
                var label = $"({checkedLocations.Count} / {totalCount})";

                // If the user has found all of the checks, mark the label as green/sine to celebrate!
                if (checkedLocations.Count >= totalCount)
                {
                    label = $"[green][sine]{label}[/sine][/green]";
                }

                component?.SetText(label);
            }

            /// <summary>
            /// Updates the Received Items in the UI for the currently selected character, 
            /// including gold rewards, card/relic/potion rewards, and progressive smith/rest levels.
            /// </summary>
            public static void UpdateReceivedItems(CharacterModel characterModel)
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
