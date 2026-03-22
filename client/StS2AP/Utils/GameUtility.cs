using Archipelago.MultiClient.Net.Models;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Rooms;
using StS2AP.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static StS2AP.Data.CharTable;
using StS2AP.UI;
using StS2AP.Extensions;

namespace StS2AP.Utils
{
    /// <summary>
    /// Collection of functions related to the player's Gameplay.
    /// Anything that touches the Player's run, their deck, their gold, etc. should be here.
    /// </summary>
    public static class GameUtility
    {
        /// <summary>
        /// Returns true if there is an active run with a valid player
        /// All grant methods check this before doing anything.
        /// </summary>
        public static bool IsInRun => CurrentPlayer != null;

        /// <summary>
        /// Reference to the Current Player character.
        /// Set when a run starts, cleared when a run ends.
        /// </summary>
        public static Player? CurrentPlayer { get; set; }

        public static APItemCharID? CurrentCharacterID
        {
            get
            {
                if (CurrentPlayer == null)
                {
                    LogUtility.Warn("Attempted to get CurrentCharacterID but there is no active player");
                    return null;
                }
                var charName = CurrentPlayer.Character.Title.GetFormattedText().Split().Last();
                return charName switch
                {
                    "Ironclad" => APItemCharID.Ironclad,
                    "Silent" => APItemCharID.Silent,
                    "Defect" => APItemCharID.Defect,
                    "Regent" => APItemCharID.Regent,
                    "Necrobinder" => APItemCharID.Necrobinder,
                    _ => null
                };
            }
        }

        #region Lock/Unlock Content

        /// <summary>
        /// Collection of all the characters that should be unlocked.
        /// 
        /// If you want to add a character to the unlocked list, you'll need to add it using the `ModelDb.Character<>()` function.
        /// For example, to add the Necrobinder, you'd need to do:
        /// `GameUtility.UnlockedCharacters.Add(ModelDb.Character<Characters.Necrobinder>());`
        /// </summary>
        public static List<CharacterModel> UnlockedCharacters { get; set; } = new List<CharacterModel>();

        #endregion

        #region Receiving Items

        /// <summary>
        /// Grants the specified amount of gold to the current player
        /// </summary>
        /// <param name="amount">The amount of gold to grant.</param>
        public static async Task GrantGold(int amount)
        {
            if (CurrentPlayer == null)
            {
                LogUtility.Warn($"Cannot grant {amount} gold: no active player (not in a run)");
                return;
            }

            try
            {
                await PlayerCmd.GainGold(amount, CurrentPlayer);
                LogUtility.Success($"Granted {amount} gold to player");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to grant gold: {ex.Message}");
            }
        }

        /// <summary>
        /// Grants a random relic to the current player
        /// </summary>
        public static async Task GrantRelic()
        {
            if (CurrentPlayer == null)
            {
                LogUtility.Warn("Cannot grant relic: no active player (not in a run)");
                return;
            }

            try
            {
                var relic = RelicFactory.PullNextRelicFromFront(CurrentPlayer).ToMutable();
                await RelicCmd.Obtain(relic, CurrentPlayer);
                LogUtility.Success($"Granted relic '{relic.Id}' to player");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to grant relic: {ex.Message}");
            }
        }

        /// <summary>
        /// Grants a random boss relic to the current player
        /// </summary>
        // public static async Task GrantBossRelic()
        // {
        //     if (CurrentPlayer == null)
        //     {
        //         LogUtility.Warn("Cannot grant boss relic: no active player (not in a run)");
        //         return;
        //     }

        //     try
        //     {
        //         var relic = RelicFactory.PullNextRelicFromFront(CurrentPlayer, RelicRarity.Rare).ToMutable();
        //         await RelicCmd.Obtain(relic, CurrentPlayer);
        //         LogUtility.Success($"Granted boss relic '{relic.Id}' to player");
        //     }
        //     catch (Exception ex)
        //     {
        //         LogUtility.Error($"Failed to grant boss relic: {ex.Message}");
        //     }
        // }

        /// <summary>
        /// Grants a random potion to the current player.
        /// Will fail silently if the player's potion slots are full so it matches the behaviour of the game's own PotionReward.
        /// </summary>
        public static async Task GrantPotion()
        {
            if (CurrentPlayer == null)
            {
                LogUtility.Warn("Cannot grant potion: no active player (not in a run)");
                return;
            }

            try
            {
                var potion = PotionFactory.CreateRandomPotionOutOfCombat(CurrentPlayer, CurrentPlayer.PlayerRng.Rewards).ToMutable();
                var result = await PotionCmd.TryToProcure(potion, CurrentPlayer);
                if (result.success)
                    LogUtility.Success($"Granted potion '{potion.Id}' to player");
                else
                    LogUtility.Warn($"Could not grant potion '{potion.Id}': potion slots may be full");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to grant potion: {ex.Message}");
            }
        }

        /// <summary>
        /// Opens the game's standard card selection screen so the player can pick a card
        /// from a freshly generated card reward pool drawn from their character's card pools.
        /// </summary>
        /// <param name="rare">If true, uses boss-encounter rarity odds (higher chance of rares).</param>
        public static async Task GrantCardReward(bool rare = false)
        {
            if (CurrentPlayer == null)
            {
                LogUtility.Warn("Cannot grant card reward: no active player (not in a run)");
                return;
            }
            // hiding the map
            var mapScreen = NMapScreen.Instance;
            bool mapWasVisible = mapScreen?.Visible ?? false;
            if (mapWasVisible && mapScreen != null)
                mapScreen.Visible = false;

            try
            {
                // CardPool is singular on CharacterModel — wrap it in an array to satisfy CardCreationOptions
                var rarity = rare ? CardRarityOddsType.BossEncounter : CardRarityOddsType.RegularEncounter;
                var options = new CardCreationOptions(
                    new[] { CurrentPlayer.Character.CardPool },
                    CardCreationSource.Encounter,
                    rarity);

                var reward = new CardReward(options, 3, CurrentPlayer);
                await reward.Populate();

                // Hide the Reward UI
                ArchipelagoRewardUI.HideTemporarily();

                // OnSelectWrapper opens NCardRewardSelectionScreen and waits for the player to pick
                try
                {
                    await reward.OnSelectWrapper();
                }
                finally
                {
                    ArchipelagoRewardUI.ShowAgain();
                    LogUtility.Success("Card reward selection completed");
                }

            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to grant card reward: {ex.Message}");
            }
            finally
            {
                // returning the map visibility so no issues are caused(hopefully lmao my code is ehhh)
                if (mapWasVisible && mapScreen != null)
                    mapScreen.Visible = true;
            }
        }

        /// <summary>
        /// Unlocks a Character for the player.
        /// </summary>
        public static void UnlockCharacter(ItemInfo item)
        {
            CharacterModel? characterToUnlock = null;
            switch(item.GetStSCharID())
            {
                case APItemCharID.Ironclad:
                    characterToUnlock = ModelDb.Character<Ironclad>();
                    break;
                case APItemCharID.Silent:
                    characterToUnlock = ModelDb.Character<Silent>();
                    break;
                case APItemCharID.Defect:
                    characterToUnlock = ModelDb.Character<Defect>();
                    break;
                case APItemCharID.Regent:
                    characterToUnlock = ModelDb.Character<Regent>();
                    break;
                case APItemCharID.Necrobinder:
                    characterToUnlock = ModelDb.Character<Necrobinder>();
                    break;
            }

            if(characterToUnlock == null)
            {
                LogUtility.Warn($"Could not find character to unlock for item {item.ItemName} (Char ID Parsed: {item.GetStSCharID().ToString()})");
                return;
            }

            if(!UnlockedCharacters.Contains(characterToUnlock)) UnlockedCharacters.Add(characterToUnlock);
        }

        #endregion

        #region Game State Event Listeners

        /// <summary>
        /// Fires when the player wins combat.
        /// Currently used to deal with boss-related triggers.
        /// </summary>
        public static void OnCombatWin(CombatRoom room)
        {
            TrySendBossDefeatCheck();

            // If this was the Act 3 boss check whether the player has met the goal
            bool isAct3Boss = room.RoomType == RoomType.Boss
                && CurrentPlayer?.RunState?.CurrentActIndex == 2;
 
            bool isFinalBoss = isAct3Boss && (
                !CurrentPlayer!.RunState.Act.HasSecondBoss ||
                ArchipelagoClient.Progress.BossRewardsDistributed > ArchipelagoProgress._maxBossRewards);
 
            if (isFinalBoss)
                _ = TrySetGoalAchieved();
        }

        #endregion

        #region Sending Checks

        public static void TrySendBossDefeatCheck()
        {
            // Determine if we send a check for this
            ArchipelagoClient.Progress.BossRewardsDistributed++;
            if (ArchipelagoClient.Progress.BossRewardsDistributed <= ArchipelagoProgress._maxBossRewards)
            {
                // Grab the Character Name
                var name = CurrentPlayer.APName();

                // Grab the check ID
                var checkName = $"{name} Act {ArchipelagoClient.Progress.BossRewardsDistributed} Boss";
                var _locationId = ArchipelagoClient.Session.Locations.GetLocationIdFromName("Slay the Spire II", checkName);

                // Attempt to send it
                if (!ArchipelagoClient.CheckedLocations.Contains(_locationId))
                {
                    // Check the location off and let the server know
                    ArchipelagoClient.CheckedLocations.Add(_locationId);
                    _ = ArchipelagoClient.Session.Locations.CompleteLocationChecksAsync(_locationId);

                    LogUtility.Success($"Sent location check: {checkName}");
                    NotificationUtility.ShowLocationChecked(_locationId);
                }
                else
                {
                    LogUtility.Error($"Failed to send {checkName}");
                }
            }
        }

        /// <summary>
        /// Checks whether the player has met the goal condition and sends SetGoalAchieved
        /// </summary>
        public static async Task TrySetGoalAchieved()
        {
            if (CurrentPlayer == null || !ArchipelagoClient.IsConnected)
            {
                LogUtility.Warn("TrySetGoalAchieved: no active player or not connected");
                return;
            }

            try
            {
                var settings = ArchipelagoClient.Settings;
                if (settings == null)
                {
                    LogUtility.Warn("TrySetGoalAchieved: Settings is null");
                    return;
                }

                // The character name that just completed Act 3
                var charName = CurrentPlayer.Character.Title.GetFormattedText().Split().Last();

                // DataStorage key scoped to this slot
                // Stored as a comma-separated string e.g. "Ironclad,Silent"
                const string storageKey = "StS2AP_GoaledChars";

                // Read the current goaled characters string from DataStorage
               ArchipelagoClient.Session.DataStorage[
                    Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                    += Operation.Update(new Dictionary<string, bool> { { charName, true } });
 
                // Read back the current dict to get the count
                var goaledChars = await ArchipelagoClient.Session.DataStorage[
                    Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                    .GetAsync<Dictionary<string, bool>>()
                    ?? new Dictionary<string, bool>();
 
                LogUtility.Success($"Recorded goal for '{charName}'. Total goaled: {goaledChars.Count}");

                // Determine required number of characters
                // num_chars_goal == 0 means all characters in the slot must complete
                int required = settings.NumCharsGoal == 0
                    ? settings.TotalCharacters
                    : settings.NumCharsGoal;

                LogUtility.Info($"Goal check: {goaledChars.Count}/{required} characters have completed Act 3");

                if (goaledChars.Count >= required)
                {
                    ArchipelagoClient.Session.SetGoalAchieved();
                    LogUtility.Success("Goal achieved! SetGoalAchieved sent to Archipelago server.");
                    NotificationUtility.ShowRawText("Goal Complete! You have won....?");
                }
            }
            catch (Exception ex)
            {
                LogUtility.Error($"TrySetGoalAchieved failed: {ex.Message}");
            }
        }

        public static void TrySendPressStartCheck()
        {
            // Grab the Character Name
            var name = GameUtility.CurrentPlayer.Character.Title.GetFormattedText().Split().Last();

            // Grab the check ID
            var checkName = $"{name} Press Start";
            SendCheck(checkName);

        }

        public static void SendCheck(string checkName)
        {
            var _locationId = ArchipelagoClient.Session.Locations.GetLocationIdFromName("Slay the Spire II", checkName);
            SendCheck(_locationId);
        }

        public static void SendCheck(long locationId)
        {
            if (!ArchipelagoClient.CheckedLocations.Contains(locationId) && locationId != -1 && ArchipelagoClient.ScoutedLocations.ContainsKey(locationId))
            {
                // Check the location off and let the server know
                ArchipelagoClient.CheckedLocations.Add(locationId);
                _ = ArchipelagoClient.Session.Locations.CompleteLocationChecksAsync(locationId);

                LogUtility.Success($"Sent location check: {locationId}");
                NotificationUtility.ShowLocationChecked(locationId);
            }
        }

        #endregion
    }
}
