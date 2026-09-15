using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Models;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Newtonsoft.Json.Linq;
using StS2AP.Data;
using StS2AP.Extensions;
using StS2AP.Patches;
using StS2AP.UI;
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
        private static HashSet<string> _goaledCharacters = new(StringComparer.OrdinalIgnoreCase);

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

        /// <summary>
        /// Returns the current player's one-based AP character number.
        /// </summary>
        public static long? CurrentAPCharacterNumber
        {
            get
            {
                if (CurrentConfig == null)
                {
                    LogUtility.Warn("Attempted to get CurrentAPCharacterNumber without an active character configuration");
                    return null;
                }
                return CurrentConfig.CharOffset;
            }
        }

        #region Receiving Items

        /// <summary>
        /// Grants the specified amount of gold to the current player
        /// </summary>
        /// <param name="amount">The amount of gold to grant.</param>
        public static async Task<bool> GrantGold(int amount)
        {
            if (CurrentPlayer == null)
            {
                LogUtility.Warn($"Cannot grant {amount} gold: no active player (not in a run)");
                return false;
            }

            if (!MultiplayerSupport.CanClaimGold(out string blockedReason))
            {
                LogUtility.Warn($"Cannot grant gold: {blockedReason}");
                return false;
            }

            // EXPLAIN: this to me
            if (MultiplayerSupport.IsRealMultiplayerRun && !LocalContext.IsMe(CurrentPlayer))
            {
                LogUtility.Error(
                    $"Refusing to originate AP gold for non-local player {CurrentPlayer.NetId}"
                );
                return false;
            }

            try
            {
                int goldBefore = CurrentPlayer.Gold;
                await PlayerCmd.GainGold(amount, CurrentPlayer);

                if (MultiplayerSupport.IsRealMultiplayerRun)
                {
                    try
                    {
                        RunManager.Instance.RewardSynchronizer.SyncLocalObtainedGold(amount);
                    }
                    catch (Exception ex)
                    {
                        // Gold is already authoritative on the local player. Retrying would
                        // duplicate it, so consume once and fail closed for later claims.
                        LogUtility.Error(
                            $"AP gold was applied locally but multiplayer sync failed: {ex.Message}"
                        );
                        MultiplayerSupport.InvalidateRunClaims(
                            "a locally applied AP gold reward could not be synchronized"
                        );
                    }
                }

                LogUtility.Success(
                    $"AP gold claim applied: localNetId={CurrentPlayer.NetId}, amount={amount}, "
                        + $"goldBefore={goldBefore}, goldAfter={CurrentPlayer.Gold}, syncSent="
                        + MultiplayerSupport.IsRealMultiplayerRun
                );
                return true;
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to grant gold: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Maps a regular AP Card Reward's stable item ordinal to the act whose native
        /// card-upgrade odds it should use. AP item indices are stable even when the player
        /// waits until a later act to claim the reward.
        /// </summary>
        internal static int? GetCardRewardActIndex(int index, Player player)
        {
            if (index < 0)
                return null;

            var characterOffset = player.GetAPCharacterNumber();
            var orderedCardRewardIndices = ArchipelagoClient.Progress.AllReceivedItems
                .Where(item =>
                    item.Item.GetAPCharacterNumber() == characterOffset
                    && item.Item.GetCharacterItemType() == APItem.CardReward
                )
                .OrderBy(item => item.Index)
                .Select(item => item.Index)
                .ToList();

            var rewardOrdinal = orderedCardRewardIndices.IndexOf(index);
            ArchipelagoSettings? settings = ArchipelagoClient.Settings;
            if (settings == null)
            {
                LogUtility.Error(
                    $"Could not map Card Reward item index {index}: AP slot settings are unavailable"
                );
                return null;
            }
            var shuffleAllCards = settings.ShouldShuffleAllCards;
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
        internal static async Task AddCardRewardToCombatDrawPile(CardModel selectedCard, Player player)
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
        /// Unlocks a Character for the player.
        /// </summary>
        public static void UnlockCharacter(ItemInfo item)
        {
            try
            {
                ArchipelagoSettings? settings = ArchipelagoClient.Settings;
                if (settings == null)
                {
                    LogUtility.Error(
                        $"Cannot unlock {item.ItemName}: AP slot settings are unavailable"
                    );
                    return;
                }

                var apCharacterNumber = item.GetAPCharacterNumber();
                var config = settings.Characters.Values.FirstOrDefault(
                    candidate => candidate.CharOffset == apCharacterNumber
                );
                if (config == null)
                {
                    LogUtility.Warn(
                        $"Got unlock item {item.ItemName} for unconfigured AP character #{apCharacterNumber}"
                    );
                    return;
                }

                var characterToUnlock = ModelDb.AllCharacters.FirstOrDefault(character =>
                    string.Equals(
                        character.Id.Entry,
                        config.OfficialName,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                if (characterToUnlock == null)
                {
                    LogUtility.Warn(
                        $"Could not find installed character '{config.OfficialName}' for unlock item {item.ItemName}"
                    );
                    return;
                }

                LogUtility.Info($"Unlocking character {characterToUnlock.Id.Entry}");

                if (!ArchipelagoClient.Progress.UnlockedCharacters.Contains(characterToUnlock)) ArchipelagoClient.Progress.UnlockedCharacters.Add(characterToUnlock);
            }
            catch(Exception ex)
            {
                LogUtility.Error(ex.ToString());
            }
        }

        #endregion

        #region Game State Event Listeners

        private static HashSet<string> _allGoaledCharacters = new(StringComparer.OrdinalIgnoreCase);
        private static bool _slotGoalSent;

        public static async Task RestoreGoaledCharsFromStorage()
        {
            var session = ArchipelagoClient.Session;
            var settings = ArchipelagoClient.Settings;
            if (session == null || settings == null) return;
            try
            {
                var storage = session.DataStorage[Archipelago.MultiClient.Net.Enums.Scope.Slot, "StS2AP_GoaledChars"];
                storage.Initialize(new JObject());
                storage.OnValueChanged += (oldData, newData, args) =>
                {
                    var stored = newData?.ToObject<Dictionary<string, bool>>();
                    ArchipelagoClient.RunForSession(session, () => ApplySharedGoalProgress(session, settings, stored));
                };
                var initial = await storage.GetAsync<Dictionary<string, bool>>();
                ArchipelagoClient.RunForSession(session, () => ApplySharedGoalProgress(session, settings, initial));
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Could not restore shared goal progress: {ex.Message}");
            }
        }

        private static void ApplySharedGoalProgress(ArchipelagoSession session, ArchipelagoSettings settings,
            Dictionary<string, bool>? stored)
        {
            // Goal records only grow. A delayed initial read must not erase a newer notification.
            if (stored != null)
                _allGoaledCharacters.UnionWith(stored.Where(pair => pair.Value).Select(pair => pair.Key));
            _goaledCharacters.UnionWith(settings.Characters.Keys.Where(character =>
                _allGoaledCharacters.Contains(ArchipelagoIdCodec.PlayerName(character, settings.PlayerNumber))));
            if (!_slotGoalSent && CoopGoalPolicy.IsComplete(_allGoaledCharacters, settings.Characters.Keys,
                    settings.PlayerCount, settings.NumCharsGoal))
            {
                session.SetGoalAchieved();
                _slotGoalSent = true;
                LogUtility.Success($"All {settings.PlayerCount} co-op players completed their character goals. SetGoalAchieved sent.");
                NotificationUtility.ShowRawText("Goal Complete! Every player has finished their goals.");
            }
        }

        internal static void ResetSlotState()
        {
            _goaledCharacters = new(StringComparer.OrdinalIgnoreCase);
            _allGoaledCharacters = new(StringComparer.OrdinalIgnoreCase);
            _slotGoalSent = false;
            CurrentPlayer = null;
            CurrentConfig = null;
        }

        /// <summary>
        /// Checks whether the player has met the goal condition and sends SetGoalAchieved if so.
        /// Uses a local HashSet for deduplication to avoid DataStorage deserialization issues
        /// and then writes to DataStorage with Operation.Update for cross-session persistence.
        /// </summary>
        public static async Task TrySetGoalAchieved(Player player)
        {
            LogUtility.Debug($"TrySetGoalAchieved() called for player {player.NetId}");

            if (!ArchipelagoClient.IsConnected)
            {
                LogUtility.Warn("TrySetGoalAchieved: not connected");
                return;
            }

            if (MultiplayerSupport.IsRealMultiplayerRun
                && (!MultiplayerSupport.IsLocalOwnApSlot
                    || !MultiplayerLocationChecks.IsLocalProgressOwner(player)))
            {
                LogUtility.Warn(
                    $"Refusing to originate AP victory progress for non-local player {player.NetId}"
                );
                return;
            }

            try
            {
                var session = ArchipelagoClient.Session;
                var settings = ArchipelagoClient.Settings;
                if (session == null || settings == null)
                {
                    LogUtility.Warn(
                        "TrySetGoalAchieved: the AP session or slot settings are unavailable"
                    );
                    return;
                }

                var charName = player.Character.Id.Entry;
                const string storageKey = "StS2AP_GoaledChars";
                LogUtility.Debug($"TrySetGoalAchieved: charName - {charName}");

                // Add to local cache HashSet.Add returns false if already present
                bool wasNew = _goaledCharacters.Add(charName);
                LogUtility.Debug($"TrySetGoalAchieved: wasNew - {wasNew.ToString()}");

                if (wasNew)
                {
                    // Persist to DataStorage atomically
                    // Do not wait for diagnostic reads here: returning to the home screen
                    // can now disconnect this slot while such a read is still in flight.
                    session.DataStorage[
                        Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                        .Initialize(new Newtonsoft.Json.Linq.JObject());

                    var updateDict = new Dictionary<string, bool> { { CoopSlot.Name(charName), true } };
 
                    session.DataStorage[
                        Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                        += Operation.Update(updateDict);

                    LogUtility.Success($"TrySetGoalAchieved: Recorded goal for '{charName}'. Total goaled: {_goaledCharacters.Count}");

                    // Goal progress is independent from whether victory releases this character's checks.
                    if (settings.ReleaseOnVictory)
                    {
                        await TryReleaseAllCharacterChecks(player.APName());
                        if (!ReferenceEquals(session, ArchipelagoClient.Session)) return;
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

                // Shared-slot completion is evaluated from merged server records, including updates
                // from other connected clients. Never goal from this player's local count alone.
                var merged = await session.DataStorage[Archipelago.MultiClient.Net.Enums.Scope.Slot, storageKey]
                    .GetAsync<Dictionary<string, bool>>();
                ArchipelagoClient.RunForSession(session, () => ApplySharedGoalProgress(session, settings, merged));
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
            charName = CoopSlot.Name(charName);
            // Location names begin with the AP character name (for example, "Ironclad").
            var characterLocations = ArchipelagoClient.ScoutedLocations
                .Where(kvp => kvp.Value.LocationName.StartsWith(
                    $"{charName} ",
                    StringComparison.OrdinalIgnoreCase
                ))
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
                    QueueCheck(locationId);
                }
            }

            await Task.CompletedTask;
        }

        public static void TrySendPressStartCheck()
        {
            Player? currentPlayer = CurrentPlayer;
            if (currentPlayer == null)
            {
                LogUtility.Warn("Cannot send the Press Start check without an active player");
                return;
            }

            TrySendPressStartCheckFor(currentPlayer.Character);
        }

        public static void TrySendPressStartCheckFor(CharacterModel character)
        {
            var locationId = LocationData.GetPressStartLocation(character);
            QueueCheck(locationId);
        }

        public static void QueueCheck(string checkName)
        {
            if (MultiplayerSupport.IsMultiplayerScope
                && !MultiplayerSupport.IsLocalOwnApSlot)
            {
                return;
            }
            ArchipelagoSession? session = ArchipelagoClient.Session;
            if (session == null)
            {
                LogUtility.Warn($"Cannot send location '{checkName}' without an active AP session.");
                return;
            }
            var locationId = session.Locations.GetLocationIdFromName("Slay the Spire II", CoopSlot.Name(checkName));
            QueueCheck(locationId);
        }

        public static void QueueCheck(long locationId)
        {
            if (!CoopSlot.Owns(locationId))
                return;
            if (MultiplayerSupport.IsMultiplayerScope
                && !MultiplayerSupport.IsLocalOwnApSlot)
            {
                return;
            }
            if (!ArchipelagoClient.CheckedLocations.Contains(locationId) && locationId != -1 && ArchipelagoClient.ScoutedLocations.ContainsKey(locationId))
            {
                // Record the location durably before attempting the socket write. If the
                // connection is timing out, it will be replayed after the next login.
                ArchipelagoClient.CheckedLocations.Add(locationId);
                PendingCheckUtility.RecordAndSend(locationId);
            }
        }

        /// <summary>Local checkpoints survive a disconnect; arbitrary recovery would bypass Ancient gates.</summary>
        public static void ShowOptionsOnLostConnection()
        {
            if (!IsInRun) return;
            NotificationUtility.ShowRawText(
                "Connection lost. Your local AP checkpoints are preserved. Reconnect and select a checkpoint to resume.");
            _ = NGame.Instance?.ReturnToMainMenuAfterRun();
        }

        #endregion
    }
}
