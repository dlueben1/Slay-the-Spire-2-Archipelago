using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using StS2AP.Data;
using StS2AP.Models;
using StS2AP.UI;
using StS2AP.Utils;
using static StS2AP.Data.CharTable;
using static StS2AP.Data.ItemTable;

namespace StS2AP
{
    /// <summary>
    /// Represents the connection lifecycle of the Archipelago client.
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting
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
        public const string Game = "Slay the Spire II";

        /// <summary>
        /// Minimum Archipelago Version that's supported by the mod.
        /// </summary>
        public const string APVersion = "0.6.7";

        /// <summary>
        /// The current connection state of the client.
        /// </summary>
        public static ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        /// <summary>
        /// Convenience property: `true` when fully connected to the Archipelago server.
        /// </summary>
        public static bool IsConnected => State == ConnectionState.Connected && Session?.Socket?.Connected == true;

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

        /// <summary>
        /// Archipelago Item Locations that we've already found so far, collected by their Location ID
        /// </summary>
        public static List<long> CheckedLocations { get; set; }

        #endregion

        /// <summary>
        /// Spinlock for processing incoming items to ensure that we don't have multiple threads trying to process items at the same time
        /// </summary>
        private static readonly object _itemLock = new();

        /// <summary>
        /// Fires when the connection state changes
        /// </summary>
        public static event Action<ConnectionState> ConnectionStateChanged;


        /// <summary>
        /// Pre-scouted location data. Key is location ID, value is a tuple of (ItemName, PlayerName).
        /// Populated on connection to avoid async calls during gameplay.
        /// </summary>
        public static Dictionary<long, ScoutedItemInfo> ScoutedLocations { get; set; } = new();

        #region Death Link Information

        /// <summary>
        /// Handles Death Link functionality, which allows players to share deaths across the multiworld.
        /// </summary>
        public static DeathLinkService DeathLinkController { get; set; }

        /// <summary>
        /// A cache of the last Death Link message received, which will be loaded into a clone of the Death Link Curse after it
        /// goes from "canonical" to "mutable" (i.e. instanced)
        /// </summary>
        public static string? LastDeathLinkMessage { get; set; }

        /// <summary>
        /// The UTC timestamp of the most recently received Death Link.
        /// 
        /// Used to suppress re-triggering a Death Link when the player dies
        /// as a direct result of receiving one. 
        /// 
        /// Null if no Death Link has been received this session,
        /// or if we're in Curse mode (which doesn't warrant suppression).
        /// </summary>
        public static DateTime? LastDeathLinkReceivedAt { get; set; }

        #endregion

        #region Networking

        /// <summary>
        /// Attempts to connect to an Archipelago room
        /// </summary>
        public static void Connect()
        {
            // Ignore if we're already connected or connecting
            if (State == ConnectionState.Connected || State == ConnectionState.Connecting) return;
            State = ConnectionState.Connecting;

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
            Session.MessageLog.OnMessageReceived += OnMessageReceived;

            // Setup the Death Link Service (even if the player isn't using Death Link)
            DeathLinkController = Session.CreateDeathLinkService();
            DeathLinkController.OnDeathLinkReceived += deathLinkInfo =>
            {
                Callable.From(() => GameUtility.OnDeathLinkReceived(deathLinkInfo)).CallDeferred();
            };

            // Attempt to connect to the server
            try
            {
                // it's safe to thread this function call but Godot hates threading so do not use excessively
                Callable.From(() => HandleConnectResult(
                        Session.TryConnectAndLogin(
                            Game,
                            PlayerName,
                            ItemsHandlingFlags.AllItems,
                            new Version(APVersion),
                            password: ServerPassword,
                            requestSlotData: SlotData.Count == 0
                        ))).CallDeferred();
            }
            catch (Exception e)
            {
                Callable.From(() => HandleConnectResult(new LoginFailure(e.ToString()))).CallDeferred();
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
                State = ConnectionState.Connected;

                // Store Session information
                SlotData = success.SlotData;
                Seed = Session.RoomState.Seed;

                // Before we tell the user everything is okay, let's make sure that the mod version is correct
                var apWorldVersion = "v" + (SlotData["mod_compat_version"] as string);
                LogUtility.Info($"APWorld Version: {apWorldVersion}");
                LogUtility.Info($"Client Version: {Version}");

                // If there's a version mismatch, we have another step
                if (apWorldVersion == null || apWorldVersion != Version)
                {
                    // Log the mismatch
                    LogUtility.Warn($"Version mismatch! Server expects version {apWorldVersion}, but client is version {Version}. Please update your mod.");

                    // Warn the user that there's a version mismatch, and let them decide how to proceed.
                    var popup = new ConfirmPopup();
                    popup.Header = new LocString("main_menu_ui", "VERSION_MISMATCH.header");
                    popup.Body = new LocString("main_menu_ui", "VERSION_MISMATCH.body");
                    popup.Body.Add("server", apWorldVersion!);
                    popup.Body.Add("client", Version);
                    popup.ButtonPressed = (yesPressed) =>
                    {
                        // On no, we should cancel out.
                        if (!yesPressed)
                        {
                            LogUtility.Warn("User was warned about version mismatch, proceeded anyways!");

                            // Show the connection UI again
                            ArchipelagoConnectionUI.Show();
                            
                            // Disconnect from the server since we can't guarantee compatibility
                            Disconnect();

                            // Re-Enable the UI
                            ArchipelagoConnectionUI.SetConnectButtonEnabled(true);
                            ArchipelagoConnectionUI.SetCloseButtonEnabled(true);

                            // Tell the user they need to update their mod
                            ArchipelagoConnectionUI.SetStatus($"Version mismatch! Server expects version {apWorldVersion}, but client is version {Version}. Please update your mod.");

                            return;
                        }
                        // On yes, we proceed
                        else
                        {
                            // Complete any locations that we have
                            outText = $"Successfully connected to {ServerAddress} as {PlayerName}!";

                            // Let the game know that we've connected
                            OnConnected();
                        }
                    };

                    // Hide the connection UI and show the popup
                    ArchipelagoConnectionUI.Hide();
                    popup.Show();
                }

                // Otherwise proceed
                else
                {
                    // Complete any locations that we have
                    outText = $"Successfully connected to {ServerAddress} as {PlayerName}!";

                    // Let the game know that we've connected
                    OnConnected();
                }
            }
            else
            {
                // Log the error
                var failure = (LoginFailure)result;
                outText = $"Failed to connect to {ServerAddress} as {PlayerName}.";
                outText = failure.Errors.Aggregate(outText, (current, error) => current + $"\n    {error}");

                // End the connection
                Disconnect();
            }
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

                // Enable/Disable the Death Link Service based on user settings
                LogUtility.Info($"Is Death Link Enabled: {Settings.IsDeathLinkEnabled.ToString()}");
                LogUtility.Info($"Death Link Damage Percentage: {Settings.DeathLinkDamagePercent.ToString()}%");
                LogUtility.Info($"Death Link Curse Enabled: {Settings.EnableDeathFragments.ToString()}");
                if (Settings.IsDeathLinkEnabled)
                {
                    DeathLinkController.EnableDeathLink();
                }
                else
                {
                    DeathLinkController.DisableDeathLink();
                }
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to load player settings: {ex.Message}");
                Disconnect();
                ArchipelagoConnectionUI.SetConnectButtonEnabled(true);
                ArchipelagoConnectionUI.SetCloseButtonEnabled(true);
                ArchipelagoConnectionUI.SetStatus($"Failed to load settings: {ex.Message}");
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
                Progress.UnlockedCharacters.AddRange(characters);
            }

            // Log all slot data
            LogUtility.Info("Dumping Slot Data:");
            foreach (var kvp in SlotData)
            {
                LogUtility.Info($"KEY: {kvp.Key}");
                LogUtility.Info($"VAL: {kvp.Value.ToString()}");
            }

            // Pre-scout all locations so we have item info available for notifications
            ThreadPool.QueueUserWorkItem(_ => PreScoutAllLocations());

            // Restore goaled characters from DataStorage so cross-session goal tracking works
            _ = GameUtility.RestoreGoaledCharsFromStorage();

            _ = GameUtility.SetupOnChangedSaves();

            // Let the game know that we've connected
            Callable.From(() => ConnectionStateChanged?.Invoke(ConnectionState.Connected)).CallDeferred();
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
            State = ConnectionState.Disconnected;

            // Let the game know that we've disconnected
            Callable.From(() => ConnectionStateChanged?.Invoke(ConnectionState.Disconnected)).CallDeferred();

            // If we were in-game when we disconnected, we have to back out to the main menu. Before doing so, we prompt the user on how they want to quit.
            Callable.From(GameUtility.ShowOptionsOnLostConnection).CallDeferred();
        }

        /// <summary>
        /// Log errors to the console and handle connection-terminating errors
        /// </summary>
        private static void OnErrorReceived(Exception e, string message)
        {
            LogUtility.Error($"Archipelago Error: {message}");
            if (e != null)
            {
                LogUtility.Error($"Exception: {e.Message}");
            }

            // Check if this is a connection-terminating error that requires manual cleanup
            if (IsConnectionTerminatingError(e, message))
            {
                LogUtility.Warn("Connection-terminating error detected. Initiating disconnect...");
                Disconnect();
            }
        }

        /// <summary>
        /// Determines if an error represents a connection-terminating condition.
        /// These errors indicate the WebSocket connection is irreversibly broken and requires cleanup.
        /// 
        /// I wrote this function because apparently, if the AP Server *abruptly* disconnects (e.g. server crash, force quit, network loss),
        /// only `OnErrorReceived` gets called and not `OnSocketSessionEnd`. 
        /// This check allows us to know if we need to trigger the disconnection workflow or not.
        /// 
        /// And yeah, there are probably more elegant ways to check this - feel free to refactor in the future :)
        /// </summary>
        private static bool IsConnectionTerminatingError(Exception e, string message)
        {
            if (e == null || string.IsNullOrEmpty(message))
                return false;

            // Only disconnect if we're actually connected
            if (State != ConnectionState.Connected)
                return false;

            // Check for WebSocket protocol errors that indicate connection loss
            string errorLower = message.ToLower();
            
            return errorLower.Contains("closed the websocket connection") ||
                   errorLower.Contains("connection closed") ||
                   errorLower.Contains("connection reset") ||
                   e.GetType().Name == "WebSocketException" ||
                   e.GetType().Name == "OperationCanceledException" && message.Contains("WebSocket");
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

        private static void OnMessageReceived(LogMessage message)
        {
            LogUtility.Info($"Got PrintJson packet {message.GetType().Name} {message.ToString()}");
            switch(message)
            {
                case ItemSendLogMessage itemSend:
                    NotificationUtility.HandleItemSend(itemSend);
                    break;
                case CountdownLogMessage:
                    NotificationUtility.HandleOtherAPMessages(message, false, 0.5);
                    break;
                    // This caused the result messages to not come through, probably because the say packets get echoed
                //case PlayerSpecificLogMessage:
                //    NotificationUtility.HandleOtherAPMessages(message, true);
                //    break;
                case CommandResultLogMessage:
                case AdminCommandResultLogMessage:
                    NotificationUtility.HandleOtherAPMessages(message, true, 3.0, true);
                    break;
                default:
                    return;
            }

        }

        #endregion

        #region Item Processing

        /// <summary>
        /// Determines what to do with an Item that we've received from Archipelago.
        /// This function is controlled by a Spinlock, and can only process one item at a time.
        /// </summary>
        /// <param name="item">Received Item</param>
        /// <param name="index">The index of the item in the Archipelago Multiworld</param>
        private static void ProcessItem(ItemInfo item, int index, bool refresh = true)
        {
            // Log the item
            LogUtility.Success($"Received: {item.ItemName} from {item.Player.Name} (ID: {item.ItemId} / LocID: {item.LocationId} / Index: {index})");

            // Apply the item to the game
            switch(item.GetRawItemID())
            {
                // Character Unlocks
                case APItem.Unlock:
                    {
                        GameUtility.UnlockCharacter(item);

                        // Fire the CharacterUnlocked event on the Godot main thread.
                        // This allows the character select screen (if open) to immediately
                        // refresh the appropriate button without waiting for OnSubmenuOpened.
                        var charId = item.GetStSCharID();
                        Callable.From(() => CharacterUnlocked?.Invoke(charId)).CallDeferred();

                        break;
                    }
                // Progressive Smiths/Rests
                case APItem.ProgressiveSmith:
                case APItem.ProgressiveRest:
                    {
                        // Get the IDs for storing the item
                        var itemId = item.GetRawItemID();
                        var playerId = item.GetStSCharID();

                        // Add the Smith/Rest to the amount we've received for this character
                        var source = itemId == APItem.ProgressiveSmith ? Progress.ProgressiveSmiths : Progress.ProgressiveRests;

                        // Increment the reward
                        try
                        {
                            var haveKey = source.TryGetValue(playerId, out int amount);
                            if (!haveKey) amount = 0;
                            source[playerId] = amount + 1;
                            LogUtility.Success($"New Value for {(itemId == APItem.ProgressiveSmith ? "ProgressiveSmiths" : "ProgressiveRests")} is {source[playerId]}");
                        }
                        catch (KeyNotFoundException e)
                        {
                            LogUtility.Error($"ProgressiveSmiths/ProgressiveRests does not have a value for this character! ({item.ItemDisplayName} from {item.Player.Name})");
                        }
                        catch
                        {
                            LogUtility.Error($"Failed to process Progressive Smith/Rest when this item was received: ({item.ItemDisplayName} from {item.Player.Name})");
                        }

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
                // Everything else ends up in the "reward pool"
                default:
                    {
                        Progress.AllReceivedItems.Add(new IndexedItemInfo(item, index));
                        break;
                    }
            }

            if (refresh)
            {
                // Refresh the unused item count
                ArchipelagoTopBarUI.RefreshCount();
            }
        }

        public static void ReprocessItems()
        {
            for (global::System.Int32 i = 0;  i < ArchipelagoClient.Session.Items.AllItemsReceived.Count;  i++)
            {
                ItemInfo info = ArchipelagoClient.Session.Items.AllItemsReceived[i];

                // i+1 because the index from multiclient .net is essentially 1 based, not 0
                ProcessItem(info, i + 1, false);
            }
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
            if (slotData.ContainsKey("death_link")) settings.IsDeathLinkEnabled = Convert.ToBoolean(slotData["death_link"]);
            if (slotData.ContainsKey("shuffle_all_cards")) settings.ShouldShuffleAllCards = Convert.ToBoolean(slotData["shuffle_all_cards"]);
            if (slotData.ContainsKey("lock_characters")) settings.NoCharactersLocked = Convert.ToInt32(slotData["lock_characters"]) == 0;
            if (slotData.ContainsKey("enable_death_fragments")) settings.EnableDeathFragments = Convert.ToInt32(slotData["enable_death_fragments"]) == 1;
            if (slotData.ContainsKey("death_link_damage_percent")) settings.DeathLinkDamagePercent = Convert.ToInt32(slotData["death_link_damage_percent"]);
            if (slotData.ContainsKey("num_chars_goal")) settings.NumCharsGoal = Convert.ToInt32(slotData["num_chars_goal"]);
            if (slotData.ContainsKey("characters") && slotData["characters"] is System.Collections.IList charsList)
            {
                // Grab the total number of characters
                settings.TotalCharacters = charsList.Count;

                /// Go through each character and add it to the list of Characters in our settings.
                /// Slot data from Archipelago.MultiClient.Net is deserialized via Newtonsoft.Json,
                /// so each entry arrives as a JObject, NOT a Dictionary<string, object>.
                var charBuffer = new List<string>();
                foreach (var charData in charsList)
                {
                    // Cast to JObject to safely read the "name" field
                    if (charData is Newtonsoft.Json.Linq.JObject charObj && charObj.TryGetValue("name", out var nameToken))
                    {
                        charBuffer.Add(nameToken.ToString());
                    }
                }

                // Store the characters locally
                settings.AvailableCharacters = charBuffer.ToArray();
            }

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

        /// <summary>
        /// Fires when a character unlock item is received and processed.
        /// Passes the <see cref="APItemCharID"/> of the character that was just unlocked.
        /// Always dispatched on the Godot main thread via CallDeferred so UI can safely respond.
        /// </summary>
        public static event Action<APItemCharID> CharacterUnlocked;
    }
}