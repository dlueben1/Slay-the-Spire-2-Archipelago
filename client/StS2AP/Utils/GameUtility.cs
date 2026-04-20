using Archipelago.MultiClient.Net.Models;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Newtonsoft.Json.Linq;
using StS2AP.Extensions;
using StS2AP.Patches;
using StS2AP.UI;
using System.Text.Json;
using static StS2AP.Data.CharTable;

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
        /// Local cache of characters that have completed the run in this slot.
        /// Populated from DataStorage on connect, updated locally on each goal.
        /// Avoids GetAsync deserialization issues by keeping the source of truth local.
        /// </summary>
        private static HashSet<string> _goaledCharacters = new HashSet<string>();

        /// <summary>
        /// The number of the characters that have reached their goal
        /// </summary>
        public static int GoaledCharactersCount => _goaledCharacters.Count;

        /// <summary>
        /// Whether or not the character has completed the run at least once, based on the local cache of goaled characters.
        /// </summary>
        /// <param name="charName">The name of the character to check. Please use `.APName()` from the `Player` or the `CharacterModel`</param>
        /// <returns>True if the character has completed the run at least once, false otherwise.</returns>
        public static bool HasCharacterGoaled(string charName)
        {
            return _goaledCharacters.Contains(charName);
        }

        /// <summary>
        /// Reference to the Current Player character.
        /// Set when a run starts, cleared when a run ends.
        /// </summary>
        public static Player? CurrentPlayer { get; set; }
        
        /// <summary>
        /// Dictionary that holds the current AP Saves for each character. Stored in DataStorage.
        /// </summary>
        public static Dictionary<string, string> APSaves { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Returns the Current Player's `APItemCharID`
        /// </summary>
        public static APItemCharID? CurrentCharacterID
        {
            get
            {
                if (CurrentPlayer == null)
                {
                    LogUtility.Warn("Attempted to get CurrentCharacterID but there is no active player");
                    return null;
                }
                var charName = CurrentPlayer.APName();
                return GetCharacterIDByName(charName);
            }
        }

        /// <summary>
        /// Gets the `APItemCharID` for a character by their AP Name.
        /// </summary>
        /// <param name="name">The name of a character, as recognized by the Archipelago World. Usually found by calling `.APName()` on a `CharacterModel` or `Player`.</param>
        /// <returns>The `APItemCharID` for a given character, by it's name. Returns `null` if the character name is invalid or unknown.</returns>
        public static APItemCharID? GetCharacterIDByName(string name)
        {
            return name switch
            {
                "Ironclad" => APItemCharID.Ironclad,
                "Silent" => APItemCharID.Silent,
                "Defect" => APItemCharID.Defect,
                "Regent" => APItemCharID.Regent,
                "Necrobinder" => APItemCharID.Necrobinder,
                _ => null
            };
        }

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
        /// Grants a random relic to the current player.
        /// This was previously used for granting a relic on the reward screen, but that was before we added `GrantRelic(RelicModel relicModel)`, 
        /// which should be used instead since the relic should've been pulled from the RelicFactory.
        /// </summary>
        [Obsolete("GrantRelic() without parameters is likely deprecated, but we'll keep it for now as the code is changing often. Use GrantRelic(RelicModel relicModel) instead to grant a specific pre-assigned relic.")]
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
        /// Grants a specific pre-assigned relic to the current player.
        /// Used when the relic was already pulled from the RelicFactory during reward screen creation.
        /// </summary>
        /// <param name="relicModel">The pre-assigned relic model to grant.</param>
        public static async Task GrantRelic(RelicModel relicModel)
        {
            if (CurrentPlayer == null)
            {
                LogUtility.Warn("Cannot grant relic: no active player (not in a run)");
                return;
            }

            try
            {
                var relic = relicModel.ToMutable();
                await RelicCmd.Obtain(relic, CurrentPlayer);
                LogUtility.Success($"Granted pre-assigned relic '{relic.Id}' to player");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to grant relic: {ex.Message}");
            }
        }

        /// <summary>
        /// Grants a random potion to the current player.
        /// Will fail silently if the player's potion slots are full so it matches the behaviour of the game's own PotionReward.
        /// </summary>
        public static async Task<bool> GrantPotion(PotionModel potion)
        {
            if (CurrentPlayer == null)
            {
                LogUtility.Warn("Cannot grant potion: no active player (not in a run)");
                return false;
            }

            try
            {
                //var potion = PotionFactory.CreateRandomPotionOutOfCombat(CurrentPlayer, CurrentPlayer.PlayerRng.Rewards).ToMutable();
                var result = await PotionCmd.TryToProcure(potion.ToMutable(), CurrentPlayer);
                if (result.success)
                    LogUtility.Success($"Granted potion '{potion.Id}' to player");
                else
                    LogUtility.Warn($"Could not grant potion '{potion.Id}': potion slots may be full");
                return result.success;
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to grant potion: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Returns the CardReward assigned to the given item index, creating and populating one if it hasn't been assigned yet.
        /// This ensures that even if the player skips a Card Reward, the same three cards are shown next time.
        /// </summary>
        private static async Task<CardReward?> GetOrAssignCardReward(int index, Player player, bool rare)
        {
            if (ArchipelagoClient.Progress.CardAssignments.TryGetValue(index, out var existing))
                return existing;

            try
            {
                var rarity = rare ? CardRarityOddsType.BossEncounter : CardRarityOddsType.RegularEncounter;
                var options = new CardCreationOptions(
                    new[] { player.Character.CardPool },
                    CardCreationSource.Encounter,
                    rarity);

                var reward = new CardReward(options, 3, player);
                await reward.Populate();

                ArchipelagoClient.Progress.CardAssignments[index] = reward;
                LogUtility.Info($"Pre-assigned card reward for item w/ index {index} (rare={rare})");
                return reward;
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to pre-assign card reward for item w/ index {index}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Opens the game's standard card selection screen so the player can pick a card
        /// from a pre-assigned (or freshly generated) card reward pool.
        /// </summary>
        /// <param name="index">The Archipelago item index, used to look up / cache the CardReward in CardAssignments.</param>
        /// <param name="rare">If true, uses boss-encounter rarity odds (higher chance of rares).</param>
        /// <returns>True if a card was actually added to the player's deck; false if the reward was skipped.</returns>
        public static async Task<bool> GrantCardReward(int index, bool rare = false)
        {
            if (CurrentPlayer == null)
            {
                LogUtility.Warn("Cannot grant card reward: no active player (not in a run)");
                return false;
            }
            // hiding the map
            var mapScreen = NMapScreen.Instance;
            bool mapWasVisible = mapScreen?.Visible ?? false;
            if (mapWasVisible && mapScreen != null)
                mapScreen.Visible = false;

            try
            {
                // Get or create the cached CardReward for this item index
                var reward = await GetOrAssignCardReward(index, CurrentPlayer, rare);
                if (reward == null)
                {
                    LogUtility.Error($"Failed to get or assign card reward for index {index}");
                    return false;
                }

                // Track how many cards are in the reward before selection
                int cardCountBefore = reward.Cards.Count();

                var paelsWing = CurrentPlayer.Relics.OfType<PaelsWing>().FirstOrDefault();
                int sacrificesBefore = paelsWing?.RewardsSacrificed ?? 0;
                LogUtility.Info($"[Debug] PaelsWing found: {paelsWing != null}, RewardsSacrificed: {sacrificesBefore}");

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
                }

                // hopefully this fixes it, it took me a while to figure out
                await Task.Yield();

                // If the card count decreased, a card was picked (added to deck)
                int cardCountAfter = reward.Cards.Count();
                bool cardWasPicked = cardCountAfter < cardCountBefore;
                bool wasSacrificed = (paelsWing?.RewardsSacrificed ?? 0) > sacrificesBefore;
                bool rewardConsumed = cardWasPicked || wasSacrificed;

                if (rewardConsumed)
                {
                    ArchipelagoClient.Progress.CardAssignments.Remove(index);
                    LogUtility.Success(cardWasPicked
                        ? "Card reward selection completed — card added to deck"
                        : "Card reward selection completed — sacrificed via Pael's Wing");
                }
                else
                {
                    LogUtility.Info("Card reward selection completed — reward was skipped");
                }

                return rewardConsumed;

            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to grant card reward: {ex.Message}");
                return false;
            }
            finally
            {
                // returning the map visibility so no issues are caused
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

            if(!ArchipelagoClient.Progress.UnlockedCharacters.Contains(characterToUnlock)) ArchipelagoClient.Progress.UnlockedCharacters.Add(characterToUnlock);
        }

        #endregion

        #region Game State Event Listeners

        public static async Task RestoreGoaledCharsFromStorage()
        {
            if (!ArchipelagoClient.IsConnected) return;

            try
            {
                const string storageKey = "StS2AP_GoaledChars";

                // Initialize the key with an empty dict if it doesn't exist yet
                ArchipelagoClient.Session.DataStorage[
                    Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                    .Initialize(new Dictionary<string, bool>()); // replace inside () with `new Newtonsoft.Json.Linq.JObject()` in case it breaks not sure if this is correct

                // Read back whatever is stored
                var stored = await ArchipelagoClient.Session.DataStorage[
                    Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                    .GetAsync<Dictionary<string, bool>>();

                _goaledCharacters = stored != null
                    ? new HashSet<string>(stored.Keys)
                    : new HashSet<string>();

                LogUtility.Info($"Restored {_goaledCharacters.Count} goaled character(s) from DataStorage: {string.Join(", ", _goaledCharacters)}");
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Could not restore goaled characters from DataStorage: {ex.Message}. Starting with empty set.");
                _goaledCharacters = new HashSet<string>();
            }
        }

        /// <summary>
        /// Sets up a watch for save files stored in datastorage.
        /// </summary>
        public static async Task SetupOnChangedSaves()
        {

            try
            {
                LogUtility.Info("Setting up StS Saves on the server");
                var storageKey = "StS2AP_Saves";

                // Initialize the key with an empty dict if it doesn't exist yet
                ArchipelagoClient.Session.DataStorage[
                    Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                    .Initialize(new JObject()); 
                // replace inside () with `new Newtonsoft.Json.Linq.JObject()` in case it breaks not sure if this is correct

                // Read back whatever is stored
                ArchipelagoClient.Session.DataStorage[Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                    .OnValueChanged += (oldData, newData, additionalArguments) =>
                    {
                        if (newData != null)
                        {
                            GameUtility.APSaves = newData?.ToObject<Dictionary<string, string>>() ?? GameUtility.APSaves;
                            LogUtility.Info($"Loaded saves from datastorage; got characters {GameUtility.APSaves?.Keys}");
                        }
                    };
                GameUtility.APSaves = await ArchipelagoClient.Session.DataStorage[Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                    .GetAsync<Dictionary<string, string>>();
            }
            catch(Exception ex)
            {
                LogUtility.Warn($"Failed to initialize datastorage watch for save files: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks whether the player has met the goal condition and sends SetGoalAchieved if so.
        /// Uses a local HashSet for deduplication to avoid DataStorage deserialization issues
        /// and then writes to DataStorage with Operation.Update for cross-session persistence
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

                var charName = CurrentPlayer.APName();
                const string storageKey = "StS2AP_GoaledChars";

                // Add to local cache HashSet.Add returns false if already present
                bool wasNew = _goaledCharacters.Add(charName);

                if (wasNew)
                {
                    // Persist to DataStorage atomically
                    ArchipelagoClient.Session.DataStorage[
                        Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                        .Initialize(new Newtonsoft.Json.Linq.JObject());
 
                    ArchipelagoClient.Session.DataStorage[
                        Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                        += Operation.Update(new Dictionary<string, bool> { { charName, true } });

                    LogUtility.Success($"Recorded goal for '{charName}'. Total goaled: {_goaledCharacters.Count}");
                }
                else
                {
                    LogUtility.Info($"'{charName}' already recorded as goaled. Total goaled: {_goaledCharacters.Count}");
                }

                // Delete save from server as a good steward
                ArchipelagoClient.Session.DataStorage[Archipelago.MultiClient.Net.Enums.Scope.Slot, "StS2AP_Saves"]
                    += Operation.Update(new Dictionary<string, string> { { charName, "" } });

                // num_chars_goal == 0 means all characters in the slot must complete
                int required = settings.NumCharsGoal == 0
                    ? settings.TotalCharacters
                    : settings.NumCharsGoal;

                LogUtility.Info($"Goal check: {_goaledCharacters.Count}/{required} characters have completed the run");

                if (_goaledCharacters.Count >= required)
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
            var name = GameUtility.CurrentPlayer.APName();

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
            }
        }

        /// <summary>
        /// Builds a Godot user:// path for the emergency recovery save file
        /// that is uniquely identifiable to the current Archipelago session.
        /// Uses the Slot Name and the room Seed so the file persists across
        /// connection/disconnection cycles.
        /// </summary>
        public static string GetRecoverySavePath()
        {
            var slotName = ArchipelagoClient.PlayerName ?? "unknown";
            var seed = ArchipelagoClient.Seed ?? "unknown";
            // Sanitise so no illegal path characters sneak in
            var safeName = string.Join("_", slotName.Split(System.IO.Path.GetInvalidFileNameChars()));
            var safeSeed = string.Join("_", seed.Split(System.IO.Path.GetInvalidFileNameChars()));
            return $"user://sts_ap_recovery_{safeName}_{safeSeed}.save";
        }

        /// <summary>
        /// When the connection to the Archipelago server is lost during a run, show a popup giving the player the option 
        /// to create an emergency recovery save file so they don't lose progress.
        /// 
        /// Unlike usual, this save file will be stored locally, rather than in the Archipelago Server's DataStorage
        /// </summary>
        public static void ShowOptionsOnLostConnection()
        {
            // Ignore if we're not in a run
            if (!IsInRun) return;

            // Build a popup for the player to choose whether to create a save file or return to main menu
            var popup = new ConfirmPopup();
            popup.Header = new LocString("gameplay_ui", "AP_LOST_CONNECTION.header");
            popup.Body = new LocString("gameplay_ui", "AP_LOST_CONNECTION.body");
            popup.ButtonPressed = (savePressed) =>
            {
                if (savePressed)
                {
                    LogUtility.Info("Attempting to create an Emergency Save");
                    CreateEmergencyRecoverySave();
                }
                else
                {
                    LogUtility.Info("No Emergency Save will be created, returning to menu");
                }

                NGame.Instance?.ReturnToMainMenuAfterRun();
            };
            NModalContainer.Instance.Add(popup.Popup);
            popup.Show();
        }

        /// <summary>
        /// Creates an emergency recovery save file locally.
        /// Serializes the current run (via RunManager.ToSave) using the same format as the normal DataStorage save,
        /// then writes the compressed data to a local file so it can be restored when the server comes back.
        /// </summary>
        private static void CreateEmergencyRecoverySave()
        {
            try
            {
                /// Serialize the run the same way the normal save path does.
                /// RunManager.ToSave triggers the Harmony postfix on SerializableRun.Serialize,
                /// which appends the ArchipelagoProgress data to the stream.
                SerializableRun saveMe = RunManager.Instance.ToSave(preFinishedRoom: null);
                var json = JsonSerializer.Serialize(saveMe, JsonSerializationUtility.GetTypeInfo<SerializableRun>());
                var zipped = Patches_RunSaveManager.SaveRun.Zip(json);

                // Write to a local file using Godot's FileAccess (respects user:// virtual path)
                var savePath = GetRecoverySavePath();
                using var file = Godot.FileAccess.Open(savePath, Godot.FileAccess.ModeFlags.Write);
                if (file == null)
                {
                    LogUtility.Error($"Failed to open recovery save file for writing: {Godot.FileAccess.GetOpenError()}");
                    return;
                }

                file.StoreString(zipped);
                LogUtility.Success($"Emergency recovery save written to {savePath}");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to create emergency recovery save: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks whether a local emergency recovery save file exists for the current Archipelago session.
        /// </summary>
        public static bool HasRecoverySave()
        {
            return Godot.FileAccess.FileExists(GetRecoverySavePath());
        }

        /// <summary>
        /// Loads the emergency recovery save data as a compressed string, or null if the file doesn't exist.
        /// </summary>
        public static string? LoadRecoverySaveData()
        {
            if (!HasRecoverySave()) return null;

            try
            {
                var savePath = GetRecoverySavePath();
                using var file = Godot.FileAccess.Open(savePath, Godot.FileAccess.ModeFlags.Read);
                if (file == null)
                {
                    LogUtility.Error($"Failed to open recovery save file for reading: {Godot.FileAccess.GetOpenError()}");
                    return null;
                }

                return file.GetAsText();
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to load recovery save: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Deletes the local emergency recovery save file.
        /// </summary>
        public static void DeleteRecoverySave()
        {
            try
            {
                if (HasRecoverySave())
                {
                    Godot.DirAccess.RemoveAbsolute(GetRecoverySavePath());
                    LogUtility.Info("Emergency recovery save file deleted.");
                }
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Failed to delete recovery save file: {ex.Message}");
            }
        }

        #endregion
    }
}
