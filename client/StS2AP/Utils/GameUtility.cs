using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;
using Newtonsoft.Json.Linq;
using StS2AP.Extensions;
using StS2AP.Models;
using StS2AP.Patches;
using System.Text.Json;
using StS2AP.UI;
using static StS2AP.Data.CharTable;
using static StS2AP.Data.ItemTable;

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
            LogUtility.Debug($"HasCharacterGoaled({charName}): {_goaledCharacters.Contains(charName)}");
            return _goaledCharacters.Contains(charName);
        }

        /// <summary>
        /// Reference to the Current Player character.
        /// Set when a run starts, cleared when a run ends.
        /// </summary>
        public static Player? CurrentPlayer { get; set; }

        /// <summary>
        /// Returns the slot data configuration for the current character being run.
        /// </summary>
        public static CharacterConfig? CurrentConfig { get; set; }
        
        /// <summary>
        /// Dictionary that holds the current AP Saves for each character. Stored in DataStorage.
        /// </summary>
        public static Dictionary<string, string> APSaves { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Returns the Current Player's `APItemCharID`
        /// </summary>
        public static long? CurrentCharacterID
        {
            get
            {
                if (CurrentConfig == null)
                {
                    LogUtility.Warn("Attempted to get CurrentCharacterID but there is no active player");
                    return null;
                }
                return CurrentConfig.CharOffset;
                // var charName = CurrentPlayer.APName();
                // return GetCharacterIDByName(charName);
            }
        }

        // /// <summary>
        // /// Gets the `APItemCharID` for a character by their AP Name.
        // /// </summary>
        // /// <param name="name">The name of a character, as recognized by the Archipelago World. Usually found by calling `.APName()` on a `CharacterModel` or `Player`.</param>
        // /// <returns>The `APItemCharID` for a given character, by it's name. Returns `null` if the character name is invalid or unknown.</returns>
        // public static APItemCharID? GetCharacterIDByName(string name)
        // {
        //     return name switch
        //     {
        //         "Ironclad" => APItemCharID.Ironclad,
        //         "Silent" => APItemCharID.Silent,
        //         "Defect" => APItemCharID.Defect,
        //         "Regent" => APItemCharID.Regent,
        //         "Necrobinder" => APItemCharID.Necrobinder,
        //         _ => null
        //     };
        // }

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
            await TryGrantRelic(relicModel);
        }

        /// <summary>
        /// Attempts to grant a specific pre-assigned relic and reports whether it was actually obtained.
        /// Reward UIs must only consume their AP item when this returns true.
        /// </summary>
        /// <param name="relicModel">The pre-assigned relic model to grant.</param>
        public static async Task<bool> TryGrantRelic(RelicModel relicModel)
        {
            if (CurrentPlayer == null)
            {
                LogUtility.Warn("Cannot grant relic: no active player (not in a run)");
                return false;
            }

            try
            {
                // some hacky weird mutable changing stuff to make relics with setup for players work
                var relic = relicModel.IsMutable
                    ? RelicModel.FromSerializable(relicModel.ToSerializable())
                    : relicModel.ToMutable();
                await RelicCmd.Obtain(relic, CurrentPlayer);
                LogUtility.Success($"Granted pre-assigned relic '{relic.Id}' to player");
                return true;
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to grant relic: {ex.Message}");
                return false;
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
        private static CardReward? GetOrAssignCardReward(int index, Player player, bool rare)
        {
            if (ArchipelagoClient.Progress.CardAssignments.TryGetValue(index, out var existing))
            {
                LogUtility.Info($"Existing rewards: {string.Join(",", existing.Cards.Select(c => c.Title))}");
                return existing;
            }

            try
            {
                var rarity = rare ? CardRarityOddsType.BossEncounter : CardRarityOddsType.RegularEncounter;
                var options = BetaMainCompatibility.WithCombatRewardCompatibility(
                    new CardCreationOptions(
                        new[] { player.Character.CardPool },
                        CardCreationSource.Encounter,
                        rarity)
                );

                var reward = new CardReward(options, 3, player);
                var rewardActIndex = rare ? null : GetCardRewardActIndex(index, player);
                if (rewardActIndex.HasValue)
                {
                    Patches_APCardRewardUpgradeOdds.PopulateForAct(
                        reward,
                        rewardActIndex.Value
                    );
                }
                else
                {
                    reward.Populate();
                }

                ArchipelagoClient.Progress.CardAssignments[index] = reward;
                var rewardActDescription = rewardActIndex.HasValue
                    ? (rewardActIndex.Value + 1).ToString()
                    : "current";
                LogUtility.Info(
                    $"Pre-assigned card reward for item w/ index {index} " +
                    $"(rare={rare}, rewardAct={rewardActDescription})"
                );
                return reward;
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to pre-assign card reward for item w/ index {index}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Maps a regular AP Card Reward's stable item ordinal to the act whose native
        /// card-upgrade odds it should use. AP item indices are stable even when the player
        /// waits until a later act to claim the reward.
        /// </summary>
        private static int? GetCardRewardActIndex(int index, Player player)
        {
            if (index < 0)
                return null;

            var characterOffset = player.Character.GetCharacterOffset();
            var orderedCardRewardIndices = ArchipelagoClient.Progress.AllReceivedItems
                .Where(item =>
                    item.Item.GetCharacterOffset() == characterOffset
                    && item.Item.GetCharacterSpecificItemID() == APItem.CardReward
                )
                .OrderBy(item => item.Index)
                .Select(item => item.Index)
                .ToList();

            var rewardOrdinal = orderedCardRewardIndices.IndexOf(index);
            var shuffleAllCards = ArchipelagoClient.Settings.ShouldShuffleAllCards;
            var actOneCount = shuffleAllCards ? 7 : 3;
            var actTwoCount = shuffleAllCards ? 7 : 4;
            var totalCount = shuffleAllCards
                ? ArchipelagoProgress._maxCardRewards
                : ArchipelagoProgress._maxCardRewards / 2;

            if (rewardOrdinal < 0 || rewardOrdinal >= totalCount)
            {
                LogUtility.Error(
                    $"Could not map Card Reward item index {index} to one of " +
                    $"the expected {totalCount} AP Card Rewards; using the current act's odds"
                );
                return null;
            }

            if (rewardOrdinal < actOneCount)
                return 0;

            return rewardOrdinal < actOneCount + actTwoCount ? 1 : 2;
        }

        /// <summary>
        /// Adds a combat-local copy of a selected AP reward card to the draw pile.
        /// Does nothing when the player is not currently in combat.
        /// </summary>
        private static async Task AddCardRewardToCombatDrawPile(CardModel selectedCard, Player player)
        {
            if (!CombatManager.Instance.IsInProgress || CombatManager.Instance.IsEnding)
            {
                return;
            }

            var combatState = player.Creature.CombatState;
            if (combatState == null)
            {
                return;
            }

            try
            {
                // Match Player.PopulateCombatState: clone the permanent deck card so upgrades,
                // enchantments, and other mutable card state carry into combat.
                var combatCard = combatState.CloneCard(selectedCard);
                combatCard.DeckVersion = selectedCard;

                var result = await CardPileCmd.AddGeneratedCardToCombat(
                    combatCard,
                    PileType.Draw,
                    player,
                    CardPilePosition.Random
                );

                if (result.success)
                {
                    // Primary use is to update the draw pile UI so it displays the correct
                    // number of cards in our draw pile. Without it, it's display is too small
                    result.cardAdded.Pile?.InvokeCardAddFinished();

                    LogUtility.Success(
                        $"Added selected AP reward card '{selectedCard.Id}' to the combat draw pile"
                    );
                }
                else
                {
                    LogUtility.Warn(
                        $"Could not add selected AP reward card '{selectedCard.Id}' to the combat draw pile"
                    );
                }
            }
            catch (Exception ex)
            {
                // The card has already been added to the permanent deck. Do not make the AP
                // reward claimable again if only the additional combat copy fails.
                LogUtility.Warn(
                    $"Failed to add selected AP reward card '{selectedCard.Id}' to the combat draw pile: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// Opens the game's standard card selection screen so the player can pick a card
        /// from a pre-assigned (or freshly generated) card reward pool.
        /// </summary>
        /// <param name="index">The Archipelago item index, used to look up / cache the CardReward in CardAssignments.</param>
        /// <param name="rare">If true, uses boss-encounter rarity odds (higher chance of rares).</param>
        /// <returns>
        /// True if the reward was consumed by selecting a card or a card-reward alternative;
        /// false if the reward was skipped.
        /// </returns>
        public static async Task<bool> GrantCardReward(int index, bool rare = false)
        {
            var player = CurrentPlayer;
            if (player == null)
            {
                LogUtility.Warn("Cannot grant card reward: no active player (not in a run)");
                return false;
            }

            try
            {
                // Get or create the cached CardReward for this item index
                var reward = GetOrAssignCardReward(index, player, rare);
                if (reward == null)
                {
                    LogUtility.Error($"Failed to get or assign card reward for index {index}");
                    return false;
                }

                // CardReward.OnSelect may replace the selected card while adding it to the deck
                // (for example through an Egg relic), so identify the actual resulting deck card.
                var deckCardsBeforeSelection = player.Deck.Cards.ToHashSet();

                // well the decompiled code say we should probably not use this but it seems to work well for our
                // use case. this replaces the manual card counting we were doing for relics such as pael's wing
                // but this may impact how easy it is to port to multiplayer
                bool rewardConsumed = await reward.SelectUnsynchronized();
                var selectedCards = player.Deck.Cards
                    .Where(card => !deckCardsBeforeSelection.Contains(card))
                    .ToList();

                if (rewardConsumed)
                {
                    ArchipelagoClient.Progress.CardAssignments.Remove(index);

                    foreach (var selectedCard in selectedCards)
                    {
                        await AddCardRewardToCombatDrawPile(selectedCard, player);
                    }

                    LogUtility.Success(selectedCards.Count > 0
                        ? "Card reward selection completed — card added to deck"
                        : "Card reward selection completed — non-card option selected");
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
        }

        /// <summary>
        /// Unlocks a Character for the player.
        /// </summary>
        public static void UnlockCharacter(ItemInfo item)
        {
            try
            {
                CharacterModel? characterToUnlock = null;
                LogUtility.Info($"Before switch");
                switch (item.GetCharacterOffset())
                {
                    case (int)APItemCharID.Ironclad:
                        characterToUnlock = ModelDb.Character<Ironclad>();
                        break;
                    case (int)APItemCharID.Silent:
                        characterToUnlock = ModelDb.Character<Silent>();
                        break;
                    case (int)APItemCharID.Defect:
                        characterToUnlock = ModelDb.Character<Defect>();
                        break;
                    case (int)APItemCharID.Regent:
                        characterToUnlock = ModelDb.Character<Regent>();
                        break;
                    case (int)APItemCharID.Necrobinder:
                        characterToUnlock = ModelDb.Character<Necrobinder>();
                        break;
                    default:
                        LogUtility.Info($"Default case");
                        var config = ArchipelagoClient.Settings.Characters.Values.FirstOrDefault(c => c.CharOffset == (int)item.GetCharacterOffset());
                        LogUtility.Warn($"Got item unlock but character not configured {item.ItemName}");
                        if (config != null)
                        {
                            characterToUnlock = ModelDb.AllCharacters.FirstOrDefault(c => string.Equals(c.Id.Entry, config.OfficialName, StringComparison.OrdinalIgnoreCase));
                        }
                        break;
                }

                if (characterToUnlock == null)
                {
                    LogUtility.Warn($"Could not find character to unlock for item {item.ItemName} (Char ID Parsed: {item.GetCharacterOffset()})");
                    return;
                }

                LogUtility.Info($"Unlocking character {characterToUnlock.Id.Entry}");

                if (!ArchipelagoClient.Progress.UnlockedCharacters.Contains(characterToUnlock)) ArchipelagoClient.Progress.UnlockedCharacters.Add(characterToUnlock);
            }
            catch(Exception ex)
            {
                LogUtility.Error(ex.StackTrace);
            }
        }

        #endregion

        #region Game State Event Listeners

        public static async Task RestoreGoaledCharsFromStorage()
        {
            if (!ArchipelagoClient.IsConnected) return;

            // Debug: Let's see the goal progress before we try to restore it
            try
            {
                // Debug: Dump all values in the DataStorage
                var ds = await ArchipelagoClient.Session.DataStorage[
                    Archipelago.MultiClient.Net.Enums.Scope.Slot, "StS2AP_GoaledChars"].GetAsync<Dictionary<string, bool>>();
                if(ds == null)
                {
                    LogUtility.Debug("RestoreGoaledCharsFromStorage: No goaled chars found in DataStorage");
                }
                else
                {
                    foreach (var x in ds)
                    {
                        LogUtility.Debug($"RestoreGoaledCharsFromStorage: Goaled DataStorage (Before Restore Attempt) - Key: {x.Key} / Value: {x.Value.ToString()}");
                    }
                }
            }
            catch (Exception e)
            {
                LogUtility.Error($"RestoreGoaledCharsFromStorage: Failed to dump pre-restore debug - {e.Message}");
            }

            try
            {
                const string storageKey = "StS2AP_GoaledChars";

                /// Initialize the key with an empty JObject (JSON object) if it doesn't exist yet.
                /// Must use JObject, not Dictionary, to match the JSON structure stored on the server.
                ArchipelagoClient.Session.DataStorage[
                    Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                    .Initialize(new JObject());

                // Read back whatever is stored and deserialize it as a Dictionary<string, bool>
                var stored = await ArchipelagoClient.Session.DataStorage[
                    Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                    .GetAsync<Dictionary<string, bool>>();

                // Debug: Dump all values in the DataStorage
                foreach (var x in stored)
                {
                    LogUtility.Debug($"RestoreGoaledCharsFromStorage: Goaled DataStorage (After Restore Attempt) - Key: {x.Key} / Value: {x.Value.ToString()}");
                }

                LogUtility.Debug($"RestoreGoaledCharsFromStorage: stored is null? {stored == null}");
                _goaledCharacters = stored != null
                    ? new HashSet<string>(stored.Keys)
                    : new HashSet<string>();

                // Debug: Dump local cache of goaled chars
                foreach (var x in _goaledCharacters)
                {
                    LogUtility.Debug($"RestoreGoaledCharsFromStorage: Local Cache Goaled Char - {x}");
                }

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
        /// and then writes to DataStorage with Operation.Update for cross-session persistence.
        /// </summary>
        public static async Task TrySetGoalAchieved()
        {
            LogUtility.Debug("TrySetGoalAchieved() Called");

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

                var charName = CurrentPlayer.Character.Id.Entry;
                const string storageKey = "StS2AP_GoaledChars";
                LogUtility.Debug($"TrySetGoalAchieved: charName - {charName}");

                // Add to local cache HashSet.Add returns false if already present
                var extras = new List<string>();
                bool wasNew = _goaledCharacters.Add(charName);
                foreach(var unrecognized in ArchipelagoClient.Settings.UnrecognizedCharacters.Values)
                {
                    wasNew |= _goaledCharacters.Add(unrecognized.OfficialName);
                    extras.Add(unrecognized.OfficialName);
                }
                LogUtility.Debug($"TrySetGoalAchieved: wasNew - {wasNew.ToString()}");

                if (wasNew)
                {
                    // Debug: Dump all values in the DataStorage
                    var ds = await ArchipelagoClient.Session.DataStorage[
                        Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey].GetAsync<Dictionary<string, bool>>();
                    foreach(var x in ds)
                    {
                        LogUtility.Debug($"TrySetGoalAchieved: Goaled DataStorage (Before Update) - Key: {x.Key} / Value: {x.Value.ToString()}");
                    }

                    // Persist to DataStorage atomically
                    ArchipelagoClient.Session.DataStorage[
                        Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                        .Initialize(new Newtonsoft.Json.Linq.JObject());

                    var updateDict = new Dictionary<string, bool> { { charName, true } };
                    foreach(var extra in extras)
                    {
                        updateDict[extra] = true;
                    }
 
                    ArchipelagoClient.Session.DataStorage[
                        Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                        += Operation.Update(updateDict);

                    // Debug: Dump all values in the DataStorage
                    var ds2 = await ArchipelagoClient.Session.DataStorage[
                        Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey].GetAsync<Dictionary<string, bool>>();
                    foreach (var x in ds2)
                    {
                        LogUtility.Debug($"TrySetGoalAchieved: Goaled DataStorage (After Update) - Key: {x.Key} / Value: {x.Value.ToString()}");
                    }

                    LogUtility.Success($"TrySetGoalAchieved: Recorded goal for '{charName}'. Total goaled: {_goaledCharacters.Count}");

                    // Goal progress is independent from whether victory releases this character's checks.
                    if (settings.ReleaseOnVictory)
                    {
                        await TryReleaseAllCharacterChecks(CurrentPlayer.APName());
                        foreach(var unrecognized in ArchipelagoClient.Settings.UnrecognizedCharacters.Values)
                        {
                            await TryReleaseAllCharacterChecks(unrecognized.Name);
                        }
                    }
                    else
                    {
                        LogUtility.Info(
                            $"Victory recorded for '{charName}' without releasing remaining checks"
                        );
                    }
                }
                else
                {
                    LogUtility.Info($"TrySetGoalAchieved: '{charName}' already recorded as goaled. Total goaled: {_goaledCharacters.Count}");
                }

                // Delete save from server as a good steward
                ArchipelagoClient.Session.DataStorage[Archipelago.MultiClient.Net.Enums.Scope.Slot, "StS2AP_Saves"]
                    += Operation.Update(new Dictionary<string, string> { { charName, "" } });

                // num_chars_goal == 0 means all characters in the slot must complete
                int required = settings.NumCharsGoal == 0
                    ? settings.TotalCharacters
                    : settings.NumCharsGoal;
                LogUtility.Debug($"TrySetGoalAchieved: required - {required.ToString()}");

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

        /// <summary>
        /// Releases all checks for a given Character. 
        /// This function should be called upon clearing a run with that character.
        /// </summary>

        public static async Task TryReleaseAllCharacterChecks(string charName)
        {
            // Grab all locations whose name contains the character's name (e.g. "Ironclad")
            var characterLocations = ArchipelagoClient.ScoutedLocations
                .Where(kvp => kvp.Value.LocationName.Contains(charName, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();

            // It shouldn't be possible, but if somehow we get here, write this problem to the log.
            if (characterLocations.Count == 0)
            {
                LogUtility.Warn($"TryReleaseAllCharacterChecks(): No locations found containing '{charName}'");
                return;
            }

            LogUtility.Info($"TryReleaseAllCharacterChecks: Releasing {characterLocations.Count} checks for '{charName}'");

            // Send every unchecked location for this character
            foreach (var locationId in characterLocations)
            {
                if (!ArchipelagoClient.CheckedLocations.Contains(locationId) && locationId != -1 && ArchipelagoClient.ScoutedLocations.ContainsKey(locationId))
                {
                    // Check the location off and let the server know
                    GameUtility.SendCheck(locationId);
                }
            }

            await Task.CompletedTask;
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
            SendCheck(locationId, true);
        }

        private static void SendCheck(long locationId, bool includeUnrecognizedChars)
        {
            if (!ArchipelagoClient.CheckedLocations.Contains(locationId) && locationId != -1 && ArchipelagoClient.ScoutedLocations.ContainsKey(locationId))
            {
                // Check the location off and let the server know
                ArchipelagoClient.CheckedLocations.Add(locationId);
                _ = ArchipelagoClient.Session.Locations.CompleteLocationChecksAsync(locationId);

                LogUtility.Success($"Sent location check: {locationId}");
            }
            if(includeUnrecognizedChars)
            {
                foreach(var otherChar in ArchipelagoClient.Settings.UnrecognizedCharacters.Values)
                {
                    // - 1 because locations are offset from items by 1
                    long newLocationId = (locationId % 10000L) + (10000L * (otherChar.CharOffset - 1));
                    LogUtility.Info($"Sending location for unrecognized character {otherChar.OfficialName} {locationId} {newLocationId}");
                    SendCheck(newLocationId, false);
                }
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
