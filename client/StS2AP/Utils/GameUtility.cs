using Archipelago.MultiClient.Net.Models;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Rooms;
using StS2AP.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        /// Reference to the Current Player character
        /// </summary>
        public static Player CurrentPlayer { get; set; }

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

        #region Processing Incoming Items

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

            // If we didn't find the character there's a problem to log
            if(characterToUnlock == null)
            {
                LogUtility.Warn($"Could not find character to unlock for item {item.ItemName} (Char ID Parsed: {item.GetStSCharID().ToString()})");
                return;
            }

            // Add the character to the game
            if(!UnlockedCharacters.Contains(characterToUnlock)) UnlockedCharacters.Add(characterToUnlock);
        }

        #endregion

        #region Game State Event Listeners

        public static void OnCombatWin(CombatRoom room)
        {
            //// If it's a boss, send a check for boss defeat
            //if (room.RoomType == RoomType.Boss)
            //{
            //    TrySendBossDefeatCheck();
            //}
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
                var name = GameUtility.CurrentPlayer.Character.Title.GetFormattedText().Split().Last();

                // Grab the check ID
                var checkName = $"{name} Act {ArchipelagoClient.Progress.BossRewardsDistributed} Boss";
                LogUtility.Debug($"BOSS NAME CHECK: {checkName}");
                var _locationId = ArchipelagoClient.Session.Locations.GetLocationIdFromName("Slay the Spire II", checkName);

                if (!ArchipelagoClient.CheckedLocations.Contains(_locationId))
                {
                    // Check the location off and let the server know
                    ArchipelagoClient.CheckedLocations.Add(_locationId);
                    _ = ArchipelagoClient.Session.Locations.CompleteLocationChecksAsync(_locationId);

                    LogUtility.Success($"Sent location check: {_locationId}");
                    NotificationUtility.ShowLocationChecked(_locationId);
                }
            }
        }

        public static void TrySendPressStartCheck()
        {
            // Grab the Character Name
            var name = GameUtility.CurrentPlayer.Character.Title.GetFormattedText().Split().Last();

            // Grab the check ID
            var checkName = $"{name} Press Start";
            var _locationId = ArchipelagoClient.Session.Locations.GetLocationIdFromName("Slay the Spire II", checkName);

            if (!ArchipelagoClient.CheckedLocations.Contains(_locationId))
            {
                // Check the location off and let the server know
                ArchipelagoClient.CheckedLocations.Add(_locationId);
                _ = ArchipelagoClient.Session.Locations.CompleteLocationChecksAsync(_locationId);

                LogUtility.Success($"Sent location check: {_locationId}");
            }
        }

        #endregion
    }
}
