using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Saves;
using StS2AP.Data;
using StS2AP.Models;
using StS2AP.UI;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static StS2AP.Data.ItemTable;
using static System.Collections.Specialized.BitVector32;

namespace StS2AP
{
    public class ResultEventArgs : EventArgs
    {
        public bool Value;
    }

    /// <summary>
    /// Handles the state of our Archipelago Multiworld, including connection details and gameplay data
    /// </summary>
    public static class ArchipelagoClient
    {
        /// <summary>
        /// The version of the Archipelago Mod (semantic version: major.minor.patch)
        /// </summary>
        public static string Version
        {
            get
            {
                var version = typeof(ArchipelagoClient).Assembly.GetName().Version;
                if (version == null) return "Version Unknown";
                return $"v{version.Major}.{version.Minor}.{version.Build}";
            }
        }

        #region Connection Info

        public static string ServerAddress { get; set; }
        public static string ServerPassword { get; set; }
        public static string PlayerName { get; set; }
        public static string Seed { get; set; }

        /// <summary>
        /// The name of the Game
        /// </summary>
        private const string Game = "Slay the Spire II";

        /// <summary>
        /// Minimum Archipelago Version that's supported by the mod.
        /// </summary>
        public const string APVersion = "0.6.6";

        public static bool Authenticated { get; set; }
        public static bool Connecting { get; set; }
        public static bool IsConnected => Authenticated && Session?.Socket?.Connected == true;

        #endregion

        #region Session Information

        /// <summary>
        /// The Player's settings, mostly from the YAML
        /// </summary>
        public static ArchipelagoSettings Settings { get; private set; }

        public static ArchipelagoSession Session { get; set; }

        /// <summary>
        /// Progress of the player through their Archipelago game.
        /// Some of this data resets every run.
        /// </summary>
        public static ArchipelagoProgress Progress { get; set; } = new();


        /// <summary>
        /// Represents how caught up we are with Archipelago's sent items
        /// </summary>
        private static int Index;

        public static Dictionary<string, object> SlotData { get; set; }

        public static List<long> CheckedLocations { get; set; }

        #endregion

        /// <summary>
        /// Spinlock for processing incoming items to ensure that we don't have multiple threads trying to process items at the same time
        /// </summary>
        private static readonly object _itemLock = new();

        /// <summary>
        /// Fires when the connection state changes
        /// </summary>
        public static event EventHandler<ResultEventArgs> ConnectionStateChanged;


        /// <summary>
        /// Pre-scouted location data. Key is location ID, value is a tuple of (ItemName, PlayerName).
        /// Populated on connection to avoid async calls during gameplay.
        /// </summary>
        public static Dictionary<long, ScoutedItemInfo> ScoutedLocations { get; set; } = new();

        #region Networking

        /// <summary>
        /// Attempts to connect to an Archipelago room
        /// </summary>
        public static void Connect()
        {
            // Ignore if we're already authenticated
            if (Authenticated || Connecting) return;
            Connecting = true;

            // Setup Data
            SlotData?.Clear();
            SlotData = new Dictionary<string, object>();
            CheckedLocations = new List<long>();
            ScoutedLocations.Clear();

            // Attempt to create the AP Session
            try
            {
                // Setup the Session
                Session = ArchipelagoSessionFactory.CreateSession(ServerAddress);
            }
            catch (Exception e)
            {
                return;
            }

            // Listen for received items
            Session.Items.ItemReceived += OnItemReceived;

            // Listen for errors
            Session.Socket.ErrorReceived += OnErrorReceived;

            // Listen for connection termination
            Session.Socket.SocketClosed += OnSocketSessionEnd;

            // Attempt to connect to the server
            try
            {
                // it's safe to thread this function call but unity notoriously hates threading so do not use excessively
                ThreadPool.QueueUserWorkItem(
                    _ => HandleConnectResult(
                        Session.TryConnectAndLogin(
                            Game,
                            PlayerName,
                            ItemsHandlingFlags.AllItems,
                            new Version(APVersion),
                            password: ServerPassword,
                            requestSlotData: SlotData.Count == 0
                        )));
            }
            catch (Exception e)
            {
                HandleConnectResult(new LoginFailure(e.ToString()));
            }
        }

        /// <summary>
        /// Handle the outcome of a connection attempt
        /// </summary>
        private static void HandleConnectResult(LoginResult result)
        {
            string outText;
            if (result.Successful)
            {
                // We are now connected!
                var success = (LoginSuccessful)result;
                Authenticated = true;

                // Store Session information
                SlotData = success.SlotData;
                Seed = Session.RoomState.Seed;

                // Before we tell the user everything is okay, let's make sure that the mod version is correct
                var apWorldVersion = "v" + (SlotData["mod_compat_version"] as string);
                LogUtility.Info($"APWorld Version: {apWorldVersion}");
                LogUtility.Info($"Client Version: {Version}");
                if (apWorldVersion == null || apWorldVersion != Version)
                {
                    // Log the issue
                    LogUtility.Error($"Version mismatch! Server expects version {apWorldVersion}, but client is version {Version}. Please update your mod.");

                    // Disconnect from the server since we can't guarantee compatibility
                    Disconnect();

                    // Re-Enable the UI
                    ArchipelagoConnectionUI.SetConnectButtonEnabled(true);
                    ArchipelagoConnectionUI.SetCloseButtonEnabled(true);

                    // Tell the user they need to update their mod
                    ArchipelagoConnectionUI.SetStatus($"Version mismatch! Server expects version {apWorldVersion}, but client is version {Version}. Please update your mod.");

                    return;
                }

                // Complete any locations that we have
                //Session.Locations.CompleteLocationChecksAsync(null, CheckedLocations.ToArray());
                outText = $"Successfully connected to {ServerAddress} as {PlayerName}!";

                // Let the game know that we've connected
                OnConnected();
            }
            else
            {
                // Log the error
                var failure = (LoginFailure)result;
                outText = $"Failed to connect to {ServerAddress} as {PlayerName}.";
                outText = failure.Errors.Aggregate(outText, (current, error) => current + $"\n    {error}");

                // Mark us as un-authenticated and disconnect
                Authenticated = false;
                Disconnect();
            }
            Connecting = false;
        }

        /// <summary>
        /// Fires on a successful Archipelago connection.
        /// </summary>
        public static void OnConnected()
        {
            LogUtility.Success("Successfully Connected to Archipelago Server");

            // Restore checked locations from server so "Claimed" state survives restarts
            CheckedLocations = new List<long>(Session.Locations.AllLocationsChecked);
            LogUtility.Info($"Restored {CheckedLocations.Count} previously checked location(s) from server.");

            try
            {
                // Get all settings for this player
                Settings = GetPlayerSettings();
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to load player settings: {ex.Message}");
                Disconnect();
                ArchipelagoConnectionUI.SetConnectButtonEnabled(true);
                ArchipelagoConnectionUI.SetCloseButtonEnabled(true);
                ArchipelagoConnectionUI.SetStatus($"Failed to load settings: {ex.Message}");
                Connecting = false;
                return;
            }

            // If all characters should be unlocked, set that up
            if (Settings.NoCharactersLocked)
            {
                CharacterModel[] characters = new CharacterModel[]
                {
                    ModelDb.Character<Ironclad>(),
                    ModelDb.Character<Silent>(),
                    ModelDb.Character<Regent>(),
                    ModelDb.Character<Necrobinder>(),
                    ModelDb.Character<Defect>()
                };
                GameUtility.UnlockedCharacters.AddRange(characters);
            }

            // Log all slot data
            foreach (var kvp in SlotData)
            {
                LogUtility.Debug($"KEY: {kvp.Key}");
                LogUtility.Debug($"VAL: {kvp.Value.ToString()}");
            }

            // Pre-scout all locations so we have item info available for notifications
            ThreadPool.QueueUserWorkItem(_ => PreScoutAllLocations());

            // Restore goaled characters from DataStorage so cross-session goal tracking works
            _ = GameUtility.RestoreGoaledCharsFromStorage();

            _ = GameUtility.SetupOnChangedSaves();
            // Let the game know that we've connected
            ConnectionStateChanged?.Invoke(null, new ResultEventArgs { Value = true });
        }

        /// <summary>
        /// Pre-scouts all locations in the game and stores the results.
        /// This gives us the ability to show item and player names in location/check notifications without having to make async calls during gameplay.
        /// This runs on a background thread, triggered on connection before gameplay starts.
        /// </summary>
        private static void PreScoutAllLocations()
        {
            try
            {
                if (Session == null)
                {
                    LogUtility.Error("Cannot pre-scout locations: Session is null");
                    return;
                }

                // Get all location IDs for our game
                var allLocationIds = Session.Locations.AllLocations.ToArray();

                if (allLocationIds.Length == 0)
                {
                    LogUtility.Warn("No locations found to scout");
                    return;
                }

                LogUtility.Info($"Pre-scouting {allLocationIds.Length} locations...");

                // Scout all locations at once (blocking call on this thread)
                var scoutTask = Session.Locations.ScoutLocationsAsync(allLocationIds);
                scoutTask.Wait(); // Block until complete. Async doesn't play well with Harmony Patches
                ScoutedLocations = scoutTask.Result;

                // Add all scouted locations to the game's localization tables so they can be shown as rewards (which require `LocString`)
                Dictionary<string, string> locationLocalizations = new();
                foreach(var loc in ScoutedLocations)
                {
                    // Add the Item at this location to the localization table with the keys "AP_LOC_{LocationID}"
                    string locKey = $"AP_LOC_{loc.Key}";
                    string locText = $"{loc.Value.ItemDisplayName} for {loc.Value.Player.Name}";
                    locationLocalizations.Add(locKey, locText);
                    LogUtility.Warn($"{loc.Key}:{loc.Value.LocationName}:{loc.Value.LocationDisplayName}");
                }
                TextUtility.RegisterLocTableAtRuntime("ap", locationLocalizations);

                LogUtility.Success($"Pre-scouted {ScoutedLocations.Count} locations successfully");
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to pre-scout locations: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up our Session with Archipelago
        /// </summary>
        public static void Disconnect()
        {
            LogUtility.Debug("Disconnecting from Archipelago...");
            Task.Run(() => Session?.Socket.DisconnectAsync());
            Session = null;
            Authenticated = false;

            // Let the game know that we've disconnected
            ConnectionStateChanged?.Invoke(null, new ResultEventArgs { Value = false });
        }

        /// <summary>
        /// Log errors to the console
        /// </summary>
        private static void OnErrorReceived(Exception e, string message)
        {
            LogUtility.Error($"Archipelago Error: {message}");
            if (e != null)
            {
                LogUtility.Error($"Exception: {e.Message}");
            }
        }

        /// <summary>
        /// When we end our Session, disconnect from the Archipelago server
        /// </summary>
        private static void OnSocketSessionEnd(string reason)
        {
            LogUtility.Warn($"Socket session ended: {reason}");
            Disconnect();
        }

        /// <summary>
        /// Handle incoming items that come from Archipelago
        /// </summary>
        private static void OnItemReceived(ReceivedItemsHelper helper)
        {
            // Deal with this Item
            lock (_itemLock)
            {
                // Grab the item data
                var receivedItem = helper.DequeueItem();

                // Ignore if this item is an old message
                if (helper.Index <= Index) return;

                // Process it
                ProcessItem(receivedItem, helper.Index);
                
                // Keep track of how many messages we've had so far
                Index++;
            }

        }

        #endregion

        #region Item Processing

        /// <summary>
        /// Determines what to do with an Item that we've received from Archipelago.
        /// </summary>
        /// <param name="item">Received Item</param>
        /// <param name="index">The index of the item in the Archipelago Multiworld</param>
        private static void ProcessItem(ItemInfo item, int index)
        {
            // Log the item
            LogUtility.Success($"Received: {item.ItemName} from {item.Player.Name} (ID: {item.ItemId} / LocID: {item.LocationId} / Index: {index})");

            // Show Notification for the item
            NotificationUtility.ShowItemReceived(item);

            // Apply the item to the game
            switch(item.GetRawItemID())
            {
                // Characters get tracked in GameUtility
                case APItem.Unlock:
                    {
                        GameUtility.UnlockCharacter(item);
                        break;
                    }
                // Gold is condensed into a single reward pool
                case APItem.OneGold:
                case APItem.FiveGold:
                case APItem._15Gold:
                case APItem._30Gold:
                case APItem.BossGold:
                    {
                        // Get the IDs for storing the item
                        var playerId = item.GetStSCharID();
                        var itemId = item.GetRawItemID();

                        // Add the Gold to the amount we've received
                        try
                        {
                            var haveKey = Progress.GoldReceived.TryGetValue(playerId, out int gold);
                            if (!haveKey) gold = 0;
                            Progress.GoldReceived[playerId] = gold + ItemTable.GoldItemAmounts[itemId];
                        }
                        catch (KeyNotFoundException e)
                        {
                            LogUtility.Error($"GoldItemAmounts does not have a value for this item! ({item.ItemDisplayName} from {item.Player.Name})");
                        }
                        catch
                        {
                            LogUtility.Error($"Failed to process Gold when this item was received: ({item.ItemDisplayName} from {item.Player.Name})");
                        }

                        break;
                    }
                default:
                    {
                        // adding reward to the reward screen
                        Progress.AllReceivedItems.Add(new IndexedItemInfo(item, index));
                        break;
                    }
            }

            // Refresh the unused item count
            ArchipelagoTopBarUI.RefreshCount();
        }

        #endregion

        #region Slot Information

        /// <summary>
        /// Get all of the Player's Settings for their Archipelago Slot
        /// </summary>
        private static ArchipelagoSettings GetPlayerSettings()
        {
            /// Use the SlotData that was already retrieved during login
            /// instead of calling Session.DataStorage.GetSlotData() which performs
            /// a synchronous network call that can deadlock/timeout when the websocket
            /// thread is busy processing incoming item packets (e.g. on reconnect).
            var slotData = SlotData;
            if(slotData == null || slotData.Count == 0)
            {
                LogUtility.Error("No slot data found for this player!");
                throw new InvalidDataException("No slot data found for this player!");
            }
            ArchipelagoSettings settings = new();

            // Apply all found settings
            if (slotData.ContainsKey("ascension")) settings.AscensionLevel = Convert.ToInt32(slotData["ascension"]);
            if (slotData.ContainsKey("seeded")) settings.IsSeeded = Convert.ToBoolean(slotData["seeded"]);
            if (slotData.ContainsKey("shuffle_all_cards")) settings.ShouldShuffleAllCards = Convert.ToBoolean(slotData["shuffle_all_cards"]);
            if (slotData.ContainsKey("lock_characters")) settings.NoCharactersLocked = Convert.ToString(slotData["lock_characters"]) == "unlocked";
            if (slotData.ContainsKey("num_chars_goal")) settings.NumCharsGoal = Convert.ToInt32(slotData["num_chars_goal"]);
            if (slotData.ContainsKey("characters") && slotData["characters"] is System.Collections.IList charsList) settings.TotalCharacters = charsList.Count;

            if (slotData.ContainsKey("campfire_sanity"))
                settings.CampfireSanity = Convert.ToInt32(slotData["campfire_sanity"]) != 0;

            if (slotData.ContainsKey("gold_sanity"))
                settings.GoldSanity = Convert.ToInt32(slotData["gold_sanity"]) != 0;
                
            if (slotData.ContainsKey("potion_sanity"))
                settings.PotionSanity = Convert.ToInt32(slotData["potion_sanity"]) != 0;

            if (slotData.ContainsKey("include_floor_checks"))
                settings.Floorsanity = Convert.ToInt32(slotData["include_floor_checks"]) != 0;

            // And return it
            return settings;
        }

        #endregion
    }
}