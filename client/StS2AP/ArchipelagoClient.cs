using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using Newtonsoft.Json.Linq;
using StS2AP.Data;
using StS2AP.Extensions;
using StS2AP.Models;
using StS2AP.Patches;
using StS2AP.UI;
using StS2AP.Utils;
using STS2RitsuLib;
using STS2RitsuLib.Data;
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
        Reconnecting,
    }

    /// <summary>
    /// Handles the state of our Archipelago Multiworld, including connection details and gameplay data
    /// </summary>
    public static class ArchipelagoClient
    {
        /// <summary>
        /// Slot-data contract supported by this client. Increment only when a future client can
        /// no longer safely consume worlds using the previous contract.
        /// </summary>
        public const int SupportedCompatFlag = 1;

        /// <summary>
        /// The version of the Archipelago Mod (semantic version: major.minor.patch)
        /// </summary>
        public static string Version
        {
            get
            {
                var version = typeof(ArchipelagoClient).Assembly.GetName().Version;
                if (version == null)
                    return "Version Unknown";
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
        public static bool IsConnected =>
            State == ConnectionState.Connected && Session?.Socket?.Connected == true;

        #endregion

        #region Session Information

        /// <summary>
        /// The local settings for the client, as configured by the player.
        ///
        /// This contains overrides for the server-provided settings, which are stored in <seealso cref="Settings"/>,
        /// and allows the player to customize their experience without affecting the server's authoritative configuration,
        /// changing non-YAML settings such as notification frequency, etc.
        /// </summary>
        public static ModDataStoreCache<ClientSettings> LocalSettings { get; set; } =
            RitsuLibFramework
                .GetDataStore(ModEntry.ModId)
                .CreateCache<ClientSettings>("apsettings");

        /// <summary>
        /// The Archipelago Slot's settings, returned from the Server and initially configured from the player's YAML.
        ///
        /// Unless overridden using local settings, this is the default source of truth for the session's settings.
        ///
        /// It should not be written to after initialization, as it represents the server's authoritative configuration for this slot,
        /// which we can't change.
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

        // RitsuLib polls top-bar counts every frame. Cache the derived reward count and only
        // re-enumerate item history when one of its inexpensive inputs changes.
        private static ArchipelagoProgress? _rewardCountProgress;
        private static long? _rewardCountCharacterOffset;
        private static int _rewardCountReceivedItems = -1;
        private static int _rewardCountUsedItems = -1;
        private static int _rewardCountGoldRemaining = int.MinValue;
        private static int _rewardCountRelicChoiceAssignments = -1;
        private static int _rewardCountRelicsAvailableAnytime = -1;
        private static int _cachedAvailableRewardCount;

        /// <summary>
        /// Safely reads whether a character has enough of the requested progressive campfire item
        /// for the supplied one-based Act. Incoming AP items may be processed off the Godot main
        /// thread, so top-bar UI reads share the item-processing lock.
        /// TODO: @Platando: if/once there's clear separation between consumption and producing:
        /// this lock will stay here but most likely can be removed later
        /// </summary>
        internal static bool HasProgressiveCampfireAccess(long characterOffset, int act, bool smith)
        {
            lock (_itemLock)
            {
                var source = smith ? Progress.ProgressiveSmiths : Progress.ProgressiveRests;
                return source.TryGetValue(characterOffset, out var maxAct) && maxAct >= act;
            }
        }

        /// <summary>
        /// Returns the number shown on the RitsuLib Archipelago Rewards button. RitsuLib polls
        /// this from the Godot main thread while incoming items may be processed in the background.
        /// @Platando same with this stuff as above, lock can probably be removed in the future
        /// </summary>
        internal static int GetAvailableRewardCount()
        {
            lock (_itemLock)
            {
                long? characterOffset = GameUtility.CurrentConfig?.CharOffset;
                int receivedItems = Progress.AllReceivedItems.Count;
                int usedItems = Progress.UsedItems.Count;
                int goldRemaining = Progress.GoldRemaining;
                int relicChoiceAssignments = Progress.RelicChoiceAssignments.Count;
                int relicsAvailableAnytime = Progress.RelicRewardsAvailableAnytimeForRun;

                if (ReferenceEquals(_rewardCountProgress, Progress) &&
                    _rewardCountCharacterOffset == characterOffset &&
                    _rewardCountReceivedItems == receivedItems &&
                    _rewardCountUsedItems == usedItems &&
                    _rewardCountGoldRemaining == goldRemaining &&
                    _rewardCountRelicChoiceAssignments == relicChoiceAssignments &&
                    _rewardCountRelicsAvailableAnytime == relicsAvailableAnytime)
                {
                    return _cachedAvailableRewardCount;
                }

                int count = Progress.UnusedItemCount;
                if (goldRemaining > 0)
                    count++;

                _rewardCountProgress = Progress;
                _rewardCountCharacterOffset = characterOffset;
                _rewardCountReceivedItems = receivedItems;
                _rewardCountUsedItems = usedItems;
                _rewardCountGoldRemaining = goldRemaining;
                _rewardCountRelicChoiceAssignments = relicChoiceAssignments;
                _rewardCountRelicsAvailableAnytime = relicsAvailableAnytime;
                _cachedAvailableRewardCount = count;
                return _cachedAvailableRewardCount;
            }
        }

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

        private static DateTime? _lastDeathLinkReceivedAt;
        // Locking because we're reading/writing across threads, and caching can happen
        private static readonly object _deathLinkLock = new();
        /// <summary>
        /// The UTC timestamp of the most recently received Death Link.
        ///
        /// Used to suppress re-triggering a Death Link when the player dies
        /// as a direct result of receiving one.
        ///
        /// Null if no Death Link has been received this session,
        /// or if we're in Curse mode (which doesn't warrant suppression).
        /// </summary>
        public static DateTime? LastDeathLinkReceivedAt { get {
            lock(_deathLinkLock)
            {
                return _lastDeathLinkReceivedAt;
            }
        } set {
            lock(_deathLinkLock)
            {
                _lastDeathLinkReceivedAt = value;
            }
        } }

        #endregion

        #region Networking

        private static ReaderWriterLock ConnectionLock { get; } = new ReaderWriterLock();
        private static readonly object _connectionStateLock = new();

        /// <summary>
        /// Attempts to connect to an Archipelago room
        /// </summary>
        public static void Connect()
        {
            lock (_connectionStateLock)
            {
                // Ignore if we're already connected or connecting
                if (State == ConnectionState.Connected || State == ConnectionState.Connecting)
                    return;
                State = ConnectionState.Connecting;
            }

            // Setup Data
            SlotData?.Clear();
            SlotData = new Dictionary<string, object>();
            CheckedLocations = new List<long>();
            ScoutedLocations.Clear();

            // Attempt to create the AP Session
            ArchipelagoSession connectionSession;
            try
            {
                connectionSession = ArchipelagoSessionFactory.CreateSession(ServerAddress);
            }
            catch (Exception e)
            {
                LogUtility.Error($"Failed to create Archipelago session: {e.Message}");
                Disconnect();
                return;
            }

            lock (_connectionStateLock)
            {
                if (State != ConnectionState.Connecting)
                {
                    LogUtility.Debug("Discarding an Archipelago session after connection was cancelled");
                    _ = Task.Run(() => connectionSession.Socket.DisconnectAsync());
                    return;
                }
                Session = connectionSession;
            }

            // Listen for received items
            connectionSession.Items.ItemReceived += OnItemReceived;

            // Listen for errors
            connectionSession.Socket.ErrorReceived += OnErrorReceived;

            // Listen for connection termination
            connectionSession.Socket.SocketClosed += OnSocketSessionEnd;
            connectionSession.MessageLog.OnMessageReceived += OnMessageReceived;

            // Setup the Death Link Service (even if the player isn't using Death Link)
            DeathLinkController = connectionSession.CreateDeathLinkService();
            DeathLinkController.OnDeathLinkReceived += deathLinkInfo =>
            {
                Callable
                    .From(() => DeathLinkUtility.OnDeathLinkReceived(deathLinkInfo))
                    .CallDeferred();
            };

            // Attempt to connect to the server
            try
            {
                // it's safe to thread this function call but Godot hates threading so do not use excessively
                Callable
                    .From(() =>
                    {
                        ConnectionLock.AcquireWriterLock(30000);
                        try
                        {
                            HandleConnectResult(
                                connectionSession,
                                connectionSession.TryConnectAndLogin(
                                    Game,
                                    PlayerName,
                                    ItemsHandlingFlags.AllItems,
                                    new Version(APVersion),
                                    password: ServerPassword,
                                    requestSlotData: SlotData.Count == 0
                                )
                            );
                        }
                        finally
                        {
                            ConnectionLock.ReleaseWriterLock();
                        }
                    })
                    .CallDeferred();
            }
            catch (Exception e)
            {
                Callable
                    .From(() =>
                        HandleConnectResult(connectionSession, new LoginFailure(e.ToString()))
                    )
                    .CallDeferred();
            }
        }

        /// <summary>
        /// Handle the outcome of a connection attempt
        /// </summary>
        private static void HandleConnectResult(
            ArchipelagoSession connectionSession,
            LoginResult result
        )
        {
            string outText;
            lock (_connectionStateLock)
            {
                if (
                    State != ConnectionState.Connecting
                    || !ReferenceEquals(Session, connectionSession)
                )
                {
                    LogUtility.Debug("Ignoring a stale Archipelago login result");
                    return;
                }

                if (result.Successful)
                {
                    State = ConnectionState.Connected;
                }
            }

            if (result.Successful)
            {
                var success = (LoginSuccessful)result;

                // Store Session information
                SlotData = success.SlotData;
                Seed = connectionSession.RoomState.Seed;

                // Log all slot data
                LogUtility.Info("Dumping Slot Data:");
                foreach (var kvp in SlotData)
                {
                    LogUtility.Info($"KEY: {kvp.Key}");
                    LogUtility.Info($"VAL: {kvp.Value.ToString()}");
                }

                if (!TryReadApWorldCompatibility(
                        out System.Version apWorldVersion,
                        out int apWorldCompatFlag,
                        out string compatibilityError
                    ))
                {
                    RejectIncompatibleConnection(compatibilityError);
                    return;
                }

                System.Version clientVersion = GetClientSemanticVersion();
                LogUtility.Info($"APWorld Version: v{apWorldVersion}");
                LogUtility.Info($"Client Version: {Version}");
                LogUtility.Info(
                    $"APWorld CompatFlag: {apWorldCompatFlag}; client CompatFlag: {SupportedCompatFlag}"
                );

                if (apWorldCompatFlag != SupportedCompatFlag)
                {
                    RejectIncompatibleConnection(
                        $"Incompatible APWorld contract: the APWorld uses CompatFlag "
                            + $"{apWorldCompatFlag}, but this client requires {SupportedCompatFlag}."
                    );
                    return;
                }

                int majorMinorComparison = CompareMajorMinor(clientVersion, apWorldVersion);
                if (majorMinorComparison < 0)
                {
                    RejectIncompatibleConnection(
                        $"This APWorld is v{apWorldVersion}, but this client is {Version}. "
                            + "Update the mod before connecting to this newer APWorld."
                    );
                    return;
                }

                Settings = GetPlayerSettings();

                if (majorMinorComparison > 0)
                {
                    LogUtility.Warn(
                        $"Client {Version} supports APWorld v{apWorldVersion} through CompatFlag "
                            + $"{SupportedCompatFlag}, but updating the APWorld is recommended."
                    );

                    var warningBody = new LocString("main_menu_ui", "APWORLD_OLDER.body");
                    warningBody.Add("server", $"v{apWorldVersion}");
                    warningBody.Add("client", Version);
                    var popup = new ConfirmPopup
                    {
                        Header = new LocString("main_menu_ui", "APWORLD_OLDER.header"),
                        Body = warningBody,
                        ButtonPressed = continueConnecting =>
                        {
                            if (continueConnecting)
                                OnConnected();
                            else
                                RejectIncompatibleConnection(
                                    "Connection cancelled. Update the APWorld before trying again."
                                );
                        },
                    };

                    ArchipelagoConnectionUI.Hide();
                    popup.Show();
                    return;
                }

                // Patch-only differences within one major/minor line are intentionally silent.
                OnConnected();
            }
            else
            {
                // Log the error
                var failure = (LoginFailure)result;
                outText = $"Failed to connect to {ServerAddress} as {PlayerName}.";
                outText = failure.Errors.Aggregate(
                    outText,
                    (current, error) => current + $"\n    {error}"
                );

                // End the connection
                Disconnect();
            }
        }

        private static bool TryReadApWorldCompatibility(
            out System.Version apWorldVersion,
            out int compatFlag,
            out string error
        )
        {
            apWorldVersion = new System.Version(0, 0, 0);
            compatFlag = SupportedCompatFlag;
            error = string.Empty;

            if (!SlotData.TryGetValue("mod_compat_version", out object? versionValue)
                || !System.Version.TryParse(
                    Convert.ToString(versionValue)?.TrimStart('v', 'V'),
                    out System.Version? parsedVersion
                )
                || parsedVersion == null)
            {
                error = "The APWorld did not provide a valid semantic version.";
                return false;
            }
            apWorldVersion = parsedVersion;

            if (!SlotData.TryGetValue("CompatFlag", out object? compatValue))
            {
                LogUtility.Info("APWorld omitted CompatFlag; defaulting to contract 1.");
                return true;
            }

            try
            {
                compatFlag = Convert.ToInt32(compatValue);
                if (compatFlag < 1)
                    throw new InvalidDataException("CompatFlag must be a positive integer.");
                return true;
            }
            catch (Exception ex)
            {
                error = $"The APWorld supplied an invalid CompatFlag "
                    + $"('{Convert.ToString(compatValue)}'): {ex.Message}";
                return false;
            }
        }

        private static System.Version GetClientSemanticVersion() =>
            typeof(ArchipelagoClient).Assembly.GetName().Version
            ?? throw new InvalidDataException("The client assembly version is unavailable.");

        private static int CompareMajorMinor(System.Version left, System.Version right)
        {
            int majorComparison = left.Major.CompareTo(right.Major);
            return majorComparison != 0
                ? majorComparison
                : left.Minor.CompareTo(right.Minor);
        }

        private static void RejectIncompatibleConnection(string reason)
        {
            LogUtility.Error($"Archipelago compatibility check failed: {reason}");
            ArchipelagoConnectionUI.Show();
            Disconnect();
            ArchipelagoConnectionUI.SetConnectButtonEnabled(true);
            ArchipelagoConnectionUI.SetCloseButtonEnabled(true);
            ArchipelagoConnectionUI.SetStatus(reason);
        }

        /// <summary>
        /// Initializes the character-select unlock state from authoritative slot data.
        /// This must happen before the initial received-item queue is allowed to run.
        /// </summary>
        private static void SetupUnlockedCharacters()
        {
            var characters = Settings.Characters;
            var ids = new HashSet<string>(
                Progress.UnlockedCharacters.Select(c => c.Id.Entry),
                StringComparer.InvariantCultureIgnoreCase
            );

            // Initial item callbacks are blocked by ConnectionLock until OnConnected
            // completes, so the starting Unlock item has not been processed yet. Use
            // the authoritative slot-data flag to initialize every starting character.
            foreach (var config in characters.Values.Where(config => !config.Locked))
            {
                // The character may already be present after a reconnect or save restore.
                if (ids.Contains(config.OfficialName))
                {
                    continue;
                }

                // ModelDb should also work for modded characters to register here
                var model = ModelDb.AllCharacters.FirstOrDefault(character =>
                    string.Equals(
                        character.Id.Entry,
                        config.OfficialName,
                        StringComparison.InvariantCultureIgnoreCase
                    )
                );
                if (model == null)
                {
                    LogUtility.Warn(
                        $"Could not resolve starting AP character '{config.OfficialName}'"
                    );
                    continue;
                }

                Progress.UnlockedCharacters.Add(model);
                ids.Add(model.Id.Entry);
                LogUtility.Info($"Unlocking starting character {model.Id.Entry} from slot data");
            }

            bool someoneUnlocked = characters.Keys.Any(ids.Contains);
            if (!someoneUnlocked)
            {
                // A configured starting character could not be resolved, most likely
                // because a modded character ID is wrong or its mod is not loaded.
                // Keep the existing fail-safe so the character screen is still usable.
                foreach (var c in ModelDb.AllCharacters)
                {
                    if (characters.ContainsKey(c.Id.Entry))
                    {
                        Progress.UnlockedCharacters.Add(c);
                        break;
                    }
                }
                if (Progress.UnlockedCharacters.Count == 0)
                {
                    LogUtility.Error(
                        $"No valid AP characters found to unlock!  Valid characters: {string.Join(",", characters.Keys)}; Existing: {
                        string.Join(",", ModelDb.AllCharacters.Select(c => c.Id.Entry))}"
                    );
                }
                else
                {
                    LogUtility.Info(
                        $"Force unlocking character {Progress.UnlockedCharacters.First().Id.Entry}"
                    );
                }
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
            LogUtility.Info(
                $"Restored {CheckedLocations.Count} previously checked location(s) from server."
            );

            // A fresh session's checked-location list is authoritative, so this is the safe
            // point to discard confirmed outbox entries and replay anything still missing.
            PendingCheckUtility.ReconcileAndSend();

            try
            {
                // Enable/Disable the Death Link Service based on user settings
                LogUtility.Info(
                    $"SLOT - Is Death Link Enabled: {Settings.IsDeathLinkEnabled.ToString()}"
                );
                LogUtility.Info(
                    $"SLOT - Death Link Damage Percentage: {Settings.DeathLinkDamagePercent.ToString()}%"
                );
                LogUtility.Info(
                    $"SLOT - Death Link Curse Enabled: {Settings.EnableDeathFragments.ToString()}"
                );
                LogUtility.Info(
                    $"LOCAL - Death Link Settings Override: {LocalSettings.Value.OverrideDeathLinkOptions.ToString()}"
                );
                LogUtility.Info(
                    $"LOCAL - Opt-In to Death Link: {LocalSettings.Value.EnableDeathLink.ToString()}"
                );
                LogUtility.Info(
                    $"LOCAL - Death Link Override Damage Percentage: {LocalSettings.Value.DeathLinkPercentDamage.ToString()}%"
                );
                LogUtility.Info(
                    $"LOCAL - Death Link Override Curse Enabled: {LocalSettings.Value.EnableDeathFragments.ToString()}"
                );
                if (DeathLinkUtility.IsDeathLinkEnabled)
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

            SetupUnlockedCharacters();

            // Pre-scout all locations so we have item info available for notifications
            ThreadPool.QueueUserWorkItem(_ => PreScoutAllLocations());

            // Restore goaled characters from DataStorage so cross-session goal tracking works
            _ = GameUtility.RestoreGoaledCharsFromStorage();

            _ = GameUtility.SetupOnChangedSaves();

            // Load the set of already-consumed buff indices from DataStorage before item processing begins.
            _ = BuffUtility.LoadFromStorageAsync();

            // Let the game know that we've connected
            Callable
                .From(() => ConnectionStateChanged?.Invoke(ConnectionState.Connected))
                .CallDeferred();
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
                foreach (var loc in ScoutedLocations)
                {
                    // Add the Item at this location to the localization table with the keys "AP_LOC_{LocationID}"
                    string locKey = $"AP_LOC_{loc.Key}";
                    string locText = $"{loc.Value.ItemDisplayName} for {loc.Value.Player.Name}";
                    locationLocalizations.Add(locKey, locText);
                    LogUtility.Warn(
                        $"{loc.Key}:{loc.Value.LocationName}:{loc.Value.LocationDisplayName}"
                    );
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
            ArchipelagoSession? session;
            lock (_connectionStateLock)
            {
                if (State == ConnectionState.Disconnected)
                {
                    LogUtility.Debug("Ignoring duplicate Archipelago disconnect request");
                    return;
                }

                LogUtility.Debug("Disconnecting from Archipelago...");
                session = Session;
                Session = null;
                State = ConnectionState.Disconnected;
            }

            if (session != null)
            {
                // Stop the socket-close callback from re-entering this workflow after an
                // intentional disconnect, and release the other session event handlers.
                session.Items.ItemReceived -= OnItemReceived;
                session.Socket.ErrorReceived -= OnErrorReceived;
                session.Socket.SocketClosed -= OnSocketSessionEnd;
                session.MessageLog.OnMessageReceived -= OnMessageReceived;
                Task.Run(() => session.Socket.DisconnectAsync());
            }

            // Clear session queues so stale entries don't carry over after reconnecting
            BuffUtility.ClearQueue();
            NotificationUtility.ClearQueue();

            // Let the game know that we've disconnected
            Callable
                .From(() => ConnectionStateChanged?.Invoke(ConnectionState.Disconnected))
                .CallDeferred();

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

            return errorLower.Contains("closed the websocket connection")
                || errorLower.Contains("connection closed")
                || errorLower.Contains("connection reset")
                || e.GetType().Name == "WebSocketException"
                || e.GetType().Name == "OperationCanceledException"
                    && message.Contains("WebSocket");
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
            ConnectionLock.AcquireReaderLock(120000);

            try
            {
                // Deal with this Item
                lock (_itemLock)
                {
                    // Grab the item data
                    var receivedItem = helper.DequeueItem();

                    // Ignore if this item is an old message
                    if (helper.Index <= Index)
                        return;

                    // Process on Godot main thread
                    Patches_ItemProcessor.AddToQueue(new IndexedItemInfo(receivedItem, helper.Index));

                    // Keep track of how many messages we've had so far
                    Index++;
                }
            }
            finally
            {
                ConnectionLock.ReleaseReaderLock();
            }
        }

        private static void OnMessageReceived(LogMessage message)
        {
            LogUtility.Info($"Got PrintJson packet {message.GetType().Name} {message.ToString()}");
            switch (message)
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
                    NotificationUtility.HandleOtherAPMessages(message, true, 3.0);
                    break;
                default:
                    return;
            }
        }

        #endregion

        #region Slot Information

        /// <summary>
        /// Get all of the Player's Settings for their Archipelago Slot
        /// </summary>
        private static ArchipelagoSettings GetPlayerSettings()
        {
            // Use the SlotData that was already retrieved during login
            // instead of calling Session.DataStorage.GetSlotData() which performs
            // a synchronous network call that can deadlock/timeout when the websocket
            // thread is busy processing incoming item packets (e.g. on reconnect).
            var slotData = SlotData;
            if (slotData == null || slotData.Count == 0)
            {
                LogUtility.Error("No slot data found for this player!");
                throw new InvalidDataException("No slot data found for this player!");
            }
            ArchipelagoSettings settings = new();

            if(slotData.ContainsKey("mod_compat_version"))
                if(System.Version.TryParse(Convert.ToString(slotData["mod_compat_version"]), out var apworldVersion))
                    settings.APWorldVersion = apworldVersion;

            // Apply all found settings
            if (slotData.ContainsKey("seeded"))
                settings.IsSeeded = Convert.ToBoolean(slotData["seeded"]);
            if (slotData.ContainsKey("death_link"))
                settings.IsDeathLinkEnabled = Convert.ToBoolean(slotData["death_link"]);
            if (slotData.ContainsKey("shuffle_all_cards"))
                settings.ShouldShuffleAllCards = Convert.ToBoolean(slotData["shuffle_all_cards"]);
            if (slotData.ContainsKey("lock_characters"))
                settings.NoCharactersLocked = Convert.ToInt32(slotData["lock_characters"]) == 0;
            if (slotData.ContainsKey("enable_death_fragments"))
                settings.EnableDeathFragments =
                    Convert.ToInt32(slotData["enable_death_fragments"]) == 1;
            if (slotData.ContainsKey("death_link_damage_percent"))
                settings.DeathLinkDamagePercent = Convert.ToInt32(
                    slotData["death_link_damage_percent"]
                );
            if (slotData.ContainsKey("num_chars_goal"))
                settings.NumCharsGoal = Convert.ToInt32(slotData["num_chars_goal"]);
            if (
                slotData.ContainsKey("characters")
                && slotData["characters"] is System.Collections.IList charsList
            )
            {
                // Grab the total number of characters
                settings.TotalCharacters = charsList.Count;

                // Go through each character and add it to the list of Characters in our settings.
                // Slot data from Archipelago.MultiClient.Net is deserialized via Newtonsoft.Json,
                // so each entry arrives as a JObject, NOT a Dictionary<string, object>.
                foreach (var charData in charsList)
                {
                    if (charData is JObject)
                    {
                        var config = CharacterConfig.fromJObject(charData as JObject, settings.APWorldVersion);
                        if (config != null)
                        {
                            settings.Characters.Add(config.OfficialName, config);
                        }
                    }
                }

                foreach (var config in settings.Characters.Values)
                {
                    var model = ModelDb.AllCharacters.FirstOrDefault(model =>
                        string.Equals(
                            model.Id.Entry,
                            config.OfficialName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                    if (model == null)
                    {
                        settings.UnrecognizedCharacters[config.OfficialName] = config;
                    }
                }
            }

            if (slotData.ContainsKey("neow_sanity"))
                settings.NeowSanity = Convert.ToInt32(slotData["neow_sanity"]) != 0;

            if(slotData.ContainsKey("ancient_relic_location"))
                settings.AncientRelicLocation = (AncientRelicLocation)Convert.ToInt32(slotData["ancient_relic_location"]);
            if(slotData.ContainsKey("ancient_relic_pool"))
                settings.AncientRelicPool = (AncientRelicPoolMode)Convert.ToInt32(slotData["ancient_relic_pool"]);
            // These keys are one APWorld/client contract. Missing values should reject the slot
            // instead of silently changing the run's reward rules.
            if(slotData.ContainsKey("relic_rewards_available_anytime"))
                settings.RelicRewardsAvailableAnytime = Convert.ToInt32(slotData["relic_rewards_available_anytime"]);
            if(slotData.ContainsKey("release_on_victory"))
                settings.ReleaseOnVictory = Convert.ToBoolean(slotData["release_on_victory"]);

            if (slotData.ContainsKey("campfire_sanity"))
                settings.CampfireSanity = Convert.ToInt32(slotData["campfire_sanity"]) != 0;

            if (slotData.ContainsKey("gold_sanity"))
                settings.GoldSanity = Convert.ToInt32(slotData["gold_sanity"]) != 0;

            if (slotData.ContainsKey("potion_sanity"))
                settings.PotionSanity = Convert.ToInt32(slotData["potion_sanity"]) != 0;

            if (slotData.ContainsKey("include_floor_checks"))
                settings.Floorsanity = Convert.ToInt32(slotData["include_floor_checks"]) != 0;

            if(slotData.ContainsKey("progressive_starter_card"))
                settings.ProgressiveStarterCard =
                    Convert.ToInt32(slotData["progressive_starter_card"]) != 0;
            if(slotData.ContainsKey("progressive_starter_relic"))
                settings.ProgressiveStarterRelic =
                    Convert.ToInt32(slotData["progressive_starter_relic"]) != 0;

            if (slotData.ContainsKey("shop_sanity"))
                settings.ShopSanity = Convert.ToInt32(slotData["shop_sanity"]) != 0;
                
            if (slotData.ContainsKey("shop_sanity_options") && slotData["shop_sanity_options"] is Newtonsoft.Json.Linq.JObject shopOptions)
            {
                if (shopOptions.TryGetValue("card_slots", out var cardSlotsToken))
                    settings.ShopCardSlots = Convert.ToInt32(cardSlotsToken);

                if (shopOptions.TryGetValue("neutral_slots", out var neutralSlotsToken))
                    settings.ShopNeutralSlots = Convert.ToInt32(neutralSlotsToken);

                if (shopOptions.TryGetValue("relic_slots", out var relicSlotsToken))
                    settings.ShopRelicSlots = Convert.ToInt32(relicSlotsToken);

                if (shopOptions.TryGetValue("potion_slots", out var potionSlotsToken))
                    settings.ShopPotionSlots = Convert.ToInt32(potionSlotsToken);

                if (shopOptions.TryGetValue("card_remove", out var cardRemoveToken))
                    settings.ShopRemoveSlots = Convert.ToBoolean(cardRemoveToken);

                if (shopOptions.TryGetValue("costs", out var costsToken))
                    settings.ShopSanityCosts = Convert.ToInt32(costsToken);
            }
            else if (settings.ShopSanity)
            {
                LogUtility.Warn("ShopSanity is enabled but 'shop_sanity_options' was missing or not the expected object shape — all shop slots will read as unlocked.");
            }
            // And return it
            return settings;
        }

        #endregion
    }
}
