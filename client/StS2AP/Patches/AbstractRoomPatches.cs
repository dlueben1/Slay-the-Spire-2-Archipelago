using HarmonyLib;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Patches
{
    /// <summary>
    /// Patches for `AbstractRoom` and all of its derived classes.
    /// Note: You can't patch an abstract class, so some patches in this file are duplicated for each type 
    /// of room (Combat, Event, Treasure, Rest Site, Merchant, etc.) if there's a better way, let me know.
    /// </summary>
    public static class AbstractRoomPatches
    {
        #region Patches

        /// <summary>
        /// Sends an Archipelago location check when entering a Combat Room.
        /// We can't patch to abstract classes apparently, so I'm doing it to each one.
        /// </summary>
        [HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.Enter))]
        public class OnCombatRoomEnterPatch
        {
            static void Postfix(IRunState? runState, bool isRestoringRoomStackBase)
            {
                TrySendFloorCheck(runState);
            }
        }

        /// <summary>
        /// Sends an Archipelago location check when entering an Event Room.
        /// We can't patch to abstract classes apparently, so I'm doing it to each one.
        /// </summary>
        [HarmonyPatch(typeof(EventRoom), nameof(EventRoom.Enter))]
        public class OnEventRoomEnterPatch
        {
            static void Postfix(IRunState? runState, bool isRestoringRoomStackBase)
            {
                TrySendFloorCheck(runState);
            }
        }

        /// <summary>
        /// Sends an Archipelago location check when entering a Treasure Room.
        /// We can't patch to abstract classes apparently, so I'm doing it to each one.
        /// </summary>
        [HarmonyPatch(typeof(TreasureRoom), nameof(TreasureRoom.Enter))]
        public class OnTreasureRoomEnterPatch
        {
            static void Postfix(IRunState? runState, bool isRestoringRoomStackBase)
            {
                TrySendFloorCheck(runState);
            }
        }

        /// <summary>
        /// Sends an Archipelago location check when entering a Rest Site Room.
        /// We can't patch to abstract classes apparently, so I'm doing it to each one.
        /// </summary>
        [HarmonyPatch(typeof(RestSiteRoom), nameof(RestSiteRoom.Enter))]
        public class OnRestSiteRoomEnterPatch
        {
            static void Postfix(IRunState? runState, bool isRestoringRoomStackBase)
            {
                TrySendFloorCheck(runState);
            }
        }

        /// <summary>
        /// Sends an Archipelago location check when entering a Merchant Room.
        /// We can't patch to abstract classes apparently, so I'm doing it to each one.
        /// </summary>
        [HarmonyPatch(typeof(MerchantRoom), nameof(MerchantRoom.Enter))]
        public class OnMerchantRoomEnterPatch
        {
            static void Postfix(IRunState? runState, bool isRestoringRoomStackBase)
            {
                TrySendFloorCheck(runState);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// The logic to determine if we need to send a location check
        /// </summary>
        /// <param name="runState">The current state of the run</param>
        static void TrySendFloorCheck(IRunState? runState)
        {
            // Null checks to shut compiler up
            if (GameUtility.CurrentPlayer == null || runState == null)
            {
                LogUtility.Error("CurrentPlayer or runState is null, skipping Archipelago check");
                return;
            }

            // Try to get floor information from runState using reflection
            var floorProperty = runState.GetType().GetProperty("ActFloor");

            if (floorProperty == null)
            {
                LogUtility.Error("fail");
                return;
            }

            // Create the Location/Check name to send
            var floorValue = floorProperty.GetValue(runState);
            var locationName = $"Act 1 - Reach Floor {floorValue}";

            LogUtility.Debug($"Attempting to send Archipelago location check: {locationName}");

            // Get the location ID from the name
            if (ArchipelagoClient.Session?.Locations.GetLocationIdFromName("Slay the Spire II", locationName) is long locationId)
            {
                // Make sure this is the first time we've hit this location, otherwise we might be sending duplicates
                if (!ArchipelagoClient.CheckedLocations.Contains(locationId))
                {
                    // Check the location off and let the server know
                    ArchipelagoClient.CheckedLocations.Add(locationId);
                    _ = ArchipelagoClient.Session.Locations.CompleteLocationChecksAsync(locationId);

                    // Log it and notify the user (uses pre-scouted data)
                    LogUtility.Success($"Sent location check: {locationName}");
                    NotificationUtility.ShowLocationChecked(locationId, fallbackLocationName: locationName);
                }
            }
            else
            {
                LogUtility.Warn($"Location '{locationName}' not found in Archipelago");
            }
        }


        #endregion
    }
}
