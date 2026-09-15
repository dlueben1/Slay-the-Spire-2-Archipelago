using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using StS2AP.Data;
using StS2AP.Extensions;
using StS2AP.UI;
using StS2AP.UI.Components;

namespace StS2AP.Patches
{
    public static class Patches_APProgressOnCharSelect
    {
        /// <summary>
        /// When the Player selects a character, update the Archipelago Progress panels with information on that character
        /// </summary>
        [HarmonyPatch(typeof(NCharacterSelectScreen))]
        public static class UpdateCharTrackerUI
        {
            [HarmonyPatch("SelectCharacter")]
            [HarmonyPostfix]
            public static void Postfix(NCharacterSelectScreen __instance, NCharacterSelectButton charSelectButton, CharacterModel characterModel)
            {
                Patches_AscensionOverride.UpdateCharacterSelectAscension(
                    __instance,
                    charSelectButton,
                    characterModel
                );

                // OnSubmenuOpened selects the initial character before it injects the
                // tracker. CreateUI performs that first refresh once the labels exist.
                // Part of the fix to prevent the gold issue before character is ready
                if (!ArchipelagoCharTrackerUI.IsInjected)
                {
                    return;
                }

                ArchipelagoCharTrackerUI.Show();
                ArchipelagoGoalTrackerUI.Show();
                UpdateReceivedItems(characterModel);
                UpdateCheckedLocations(characterModel);
                Callable.From(() => ArchipelagoGoalTrackerUI.UpdateGoalProgress()).CallDeferred();
            }

            /// <summary>
            /// Updates the Found/Checked Locations in the UI for the currently selected character.
            /// Shows/Hides items based on what settings you are using for this run.
            /// </summary>
            public static void UpdateCheckedLocations(CharacterModel character)
            {
                ArchipelagoSettings? settings = ArchipelagoClient.Settings;
                if (settings == null)
                {
                    LogUtility.Error("Cannot update checked-location UI without AP slot settings.");
                    return;
                }
                if(!settings.Characters.ContainsKey(character.Id.Entry))
                {
                    return;
                }
                // Update Card Locations
                var cardLocations = LocationData.GetCardRewardLocations(character);
                SetCheckedLocation(ArchipelagoCharTrackerUI.CardChecks, cardLocations, ArchipelagoProgress._maxCardRewards / (settings.ShouldShuffleAllCards ? 1 : 2));

                // Update Rare Card Locations
                var rareCardLocations = LocationData.GetRareCardRewardLocations(character);
                SetCheckedLocation(ArchipelagoCharTrackerUI.RareCardChecks, rareCardLocations, ArchipelagoProgress._maxRareCardRewards);

                // Update Relic Locations
                var relicLocations = LocationData.GetRelicRewardLocations(character);
                SetCheckedLocation(ArchipelagoCharTrackerUI.RelicChecks, relicLocations, ArchipelagoProgress._maxRelicRewards);
                
                // Update Ancient Locations
                var ancientLocations = LocationData.GetAncientRewardLocations(character);
                SetCheckedLocation(ArchipelagoCharTrackerUI.AncientChecks, ancientLocations, ArchipelagoProgress.MaxConfiguredAncients);


                // Update Floorsanity Locations
                if (settings.Floorsanity)
                {
                    var floorLocations = LocationData.GetFloorsanityLocations(character);
                    SetCheckedLocation(ArchipelagoCharTrackerUI.FloorsanityChecks, floorLocations, ArchipelagoProgress._maxFloorRewards);
                }

                // Update Campfiresanity Locations
                if (settings.CampfireSanity)
                {
                    var campfireLocations = LocationData.GetCampfiresanityLocations(character);
                    SetCheckedLocation(ArchipelagoCharTrackerUI.CampfiresanityChecks, campfireLocations, ArchipelagoProgress._maxCampfireChecks);
                }

                // Update Goldsanity Locations
                if (settings.GoldSanity)
                {
                    var goldLocations = LocationData.GetGoldsanityLocations(character);
                    SetCheckedLocation(ArchipelagoCharTrackerUI.GoldsanityChecks, goldLocations, ArchipelagoProgress._maxGoldRewards);
                }

                // Update Potionsanity Locations
                if (settings.PotionSanity)
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

                if (settings.ShopSanity)
                {
                    var shopLocations = LocationData.GetShopsanityLocations(character);
                    SetCheckedLocation(ArchipelagoCharTrackerUI.ShopsanityChecks, shopLocations, shopLocations.Count);
                }
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
                ArchipelagoSettings? settings = ArchipelagoClient.Settings;
                if (settings == null)
                {
                    LogUtility.Error("Cannot update received-item UI without AP slot settings.");
                    return;
                }
                if(!settings.Characters.ContainsKey(characterModel.Id.Entry))
                {
                    // Not sure how this is getting called with something that doesn't belong, but just in case.
                    return;
                }
                // Get Character ID
                long? checkMe = characterModel.GetAPCharacterNumber();
                LogUtility.Info($"Selected Character: {characterModel.APName()}, AP Char ID: {checkMe}");

                // If (somehow) the character ID is null, stop
                if (checkMe == null ) return;

                long offset = (long) checkMe;
                // Update Gold Rewards
                LogUtility.Info($"Checking for gold rewards for character ID {offset}");
                ArchipelagoClient.Progress.GoldReceived.TryGetValue(offset, out int gold);
                LogUtility.Info($"Gold rewards received for character ID {offset}: {gold}");
                ArchipelagoCharTrackerUI.GoldRewards?.SetText(gold.ToString());

                if(settings.CampfireSanity)
                {
                    // Update Progressive Smiths/Rests
                    ArchipelagoCharTrackerUI.ProgressiveRestLabel?.SetText($"({ArchipelagoClient.Progress.MaxRestLevel(offset) ?? 0} / 3)");
                    ArchipelagoCharTrackerUI.ProgressiveSmithLabel?.SetText($"({ArchipelagoClient.Progress.MaxSmithLevel(offset) ?? 0} / 3)");
                }
                ArchipelagoCharTrackerUI.ProgressiveAncients?.SetText($"{ArchipelagoClient.Progress.MaxProgressiveAncientLevel(offset)} / 3");

                ArchipelagoClient.Progress.ProgressiveStarterCards.TryGetValue(offset, out int starterCardTier);
                ArchipelagoCharTrackerUI.ProgressiveStarterCardLabel?.SetText($"({starterCardTier} / 2)");

                ArchipelagoClient.Progress.ProgressiveStarterRelics.TryGetValue(offset, out int starterRelicTier);
                ArchipelagoCharTrackerUI.ProgressiveStarterRelicLabel?.SetText($"({starterRelicTier} / 2)");

                // Count Card/Relic/Potion/Progressive Rewards
                var itemCounts = ArchipelagoClient.Progress.AllReceivedItems
                    .Where(i => i.Item.GetAPCharacterNumber() == offset)
                    .GroupBy(i => i.Item.GetCharacterItemType())
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
                var relicCount = itemCounts.TryGetValue(ItemTable.APItem.Relic, out int relicStandard) ? relicStandard : 0;
                                 //(itemCounts.TryGetValue(ItemTable.APItem.ProgressiveAncient, out int relicBoss) ? relicBoss : 0);
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

                // Update Shopsanity Item Unlocks (only tracked/shown when Shopsanity is enabled)
                if (settings.ShopSanity)
                {
                    int shopCardSlots = ArchipelagoClient.Progress.ShopCardSlotsReceived.TryGetValue(offset, out int cs) ? cs : 0;
                    ArchipelagoCharTrackerUI.ShopCardSlots?.SetText(
                        $"({Math.Min(shopCardSlots, settings.ShopCardSlots)} / {settings.ShopCardSlots})");

                    int shopNeutralSlots = ArchipelagoClient.Progress.ShopNeutralSlotsReceived.TryGetValue(offset, out int ns) ? ns : 0;
                    ArchipelagoCharTrackerUI.ShopNeutralSlots?.SetText(
                        $"({Math.Min(shopNeutralSlots, settings.ShopNeutralSlots)} / {settings.ShopNeutralSlots})");

                    int shopRelicSlots = ArchipelagoClient.Progress.ShopRelicSlotsReceived.TryGetValue(offset, out int rs) ? rs : 0;
                    ArchipelagoCharTrackerUI.ShopRelicSlots?.SetText(
                        $"({Math.Min(shopRelicSlots, settings.ShopRelicSlots)} / {settings.ShopRelicSlots})");

                    int shopPotionSlots = ArchipelagoClient.Progress.ShopPotionSlotsReceived.TryGetValue(offset, out int ps) ? ps : 0;
                    ArchipelagoCharTrackerUI.ShopPotionSlots?.SetText(
                        $"({Math.Min(shopPotionSlots, settings.ShopPotionSlots)} / {settings.ShopPotionSlots})");

                    if (settings.ShopRemoveSlots)
                    {
                        int shopRemoveLevel = ArchipelagoClient.Progress.MaxShopRemoveLevel(offset) ?? 0;
                        ArchipelagoCharTrackerUI.ShopRemoves?.SetText(
                            $"({Math.Min(shopRemoveLevel, ArchipelagoProgress._maxShopRemoves)} / {ArchipelagoProgress._maxShopRemoves})");
                    }
                }
            }
        }
    }
}
