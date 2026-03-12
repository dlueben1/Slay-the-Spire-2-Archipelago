using Archipelago.MultiClient.Net.Models;
using StS2AP.UI;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Utils
{
    /// <summary>
    /// High-level API for displaying Archipelago notifications.
    /// </summary>
    public static class NotificationUtility
    {
        /// <summary>
        /// The queue of messages to display.
        /// </summary>
        private static readonly Queue<ArchipelagoNotification> _queue = new();

        /// <summary>
        /// Spinlock for thread-safe access to the queue.
        /// </summary>
        private static readonly object _lock = new();

        /// <summary>
        /// Event fired when a notification is added to the queue.
        /// </summary>
        public static event Action? NotificationEnqueued;

        /// <summary>
        /// Represents a single notification.
        /// </summary>
        public class ArchipelagoNotification
        {
            public string Message { get; set; }
            public NotificationType Type { get; set; }
            public double DisplayDuration { get; set; } = 3.0;

            public ArchipelagoNotification(string message, NotificationType type = NotificationType.Info)
            {
                Message = message;
                Type = type;
            }
        }

        /// <summary>
        /// Types of notifications.
        /// Not directly used now, but maybe useful for future styling or categorization of messages.
        /// </summary>
        public enum NotificationType
        {
            Info,
            ItemReceived,
            LocationCheck,
            Error,
            Warning
        }

        #region Notification Queue

        /// <summary>
        /// Queues a notification to be displayed.
        /// 
        /// It's best not to hit this directly, but to go through the functions in the "Display Notifications" region below,
        /// which will format messages appropriately for the user.
        /// </summary>
        private static void EnqueueNotification(string message, NotificationType type = NotificationType.Info)
        {
            lock (_lock)
            {
                // Enqueue the notification
                var notification = new ArchipelagoNotification(message, type);
                _queue.Enqueue(notification);
                LogUtility.Info($"Notification queued ({type}): {message}");

                // Show the Notification UI if it isn't already visible
                if(!ArchipelagoNotificationUI.IsVisible) ArchipelagoNotificationUI.ShowMessage();
            }

            // Fire event outside of lock to avoid potential deadlocks
            NotificationEnqueued?.Invoke();
        }

        /// <summary>
        /// Dequeues the next notification, or returns null if none are available.
        /// </summary>
        public static ArchipelagoNotification? DequeueNotification()
        {
            lock (_lock)
            {
                var notification = _queue.Dequeue();
                LogUtility.Info($"Notification dequeued: {notification.Message}");
                return notification;
            }
        }

        /// <summary>
        /// Peeks at the next notification without removing it.
        /// </summary>
        public static ArchipelagoNotification? PeekNotification()
        {
            lock (_lock)
            {
                return _queue.Count > 0 ? _queue.Peek() : null;
            }
        }

        /// <summary>
        /// Returns the number of queued notifications.
        /// </summary>
        public static int GetQueueCount()
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }

        /// <summary>
        /// Clears all queued notifications.
        /// </summary>
        public static void ClearQueue()
        {
            lock (_lock)
            {
                _queue.Clear();
                LogUtility.Info("Notification queue cleared");
            }
        }

        #endregion

        #region Display Notifications

        /// <summary>
        /// Display a notification about an item that's been received for you from the Multiworld
        /// </summary>
        /// <param name="item">Information about the AP Item</param>
        public static void ShowItemReceived(ItemInfo item)
        {
            // Detrermine if a font icon is needed
            string itemIcon = "";
            switch ((APItem)item.ItemId)
            {
                case APItem._25Gold:
                case APItem._5Gold:
                case APItem._2Gold:
                    {
                        itemIcon = @"[img]res://images/packed/sprite_fonts/gold_icon.png[/img]";
                        break;
                    }
                case APItem.CardReward:
                case APItem.RareCardReward:
                    {
                        itemIcon = @"[img]res://images/packed/sprite_fonts/card_icon.png[/img]";
                        break;
                    }
                case APItem.PotionReward:
                    {
                        itemIcon = @"[img]res://images/packed/sprite_fonts/potion_icon.png[/img]";
                        break;
                    }
                case APItem.Ironclad:
                    {
                        itemIcon = @"[img]res://images/packed/sprite_fonts/ironclad_energy_icon.png[/img]";
                        break;
                    }
                case APItem.Silent:
                    {
                        itemIcon = @"[img]res://images/packed/sprite_fonts/silent_energy_icon.png[/img]";
                        break;
                    }
                case APItem.Defect:
                    {
                        itemIcon = @"[img]res://images/packed/sprite_fonts/defect_energy_icon.png[/img]";
                        break;
                    }
                case APItem.Necrobinder:
                    {
                        itemIcon = @"[img]res://images/packed/sprite_fonts/necrobinder_energy_icon.png[/img]";
                        break;
                    }
                case APItem.Regent:
                    {
                        itemIcon = @"[img]res://images/packed/sprite_fonts/regent_energy_icon.png[/img]";
                        break;
                    }
                default:
                    {
                        itemIcon = @"[img]res://images/packed/sprite_fonts/chest_icon.png[/img]";
                        break;
                    }
            }

            // Setup the final string for the notification
            var msg = $"{item.Player} sent you {itemIcon} [sine][gold]{item.ItemDisplayName}[/gold]![/sine]";
            EnqueueNotification(
                msg,
                NotificationType.ItemReceived);
        }

        /// <summary>
        /// Display a notification about a location that's been checked.
        /// Looks up the item info from the pre-scouted location cache.
        /// </summary>
        /// <param name="locationId">The Archipelago location ID</param>
        /// <param name="locationName">The display name of the location</param>
        public static void ShowLocationChecked(long locationId, string? fallbackLocationName = "")
        {
            // Setup default values if we can't fetch the correct ones
            string itemName = "An AP Item";
            string playerName = "Another Player";
            string locationName = string.IsNullOrEmpty(fallbackLocationName) ? $"Unknown Location ({locationId})" : fallbackLocationName;

            // Look up the pre-scouted info
            if (ArchipelagoClient.ScoutedLocations.TryGetValue(locationId, out var scoutedInfo))
            {
                locationName = scoutedInfo.LocationDisplayName;
                itemName = scoutedInfo.ItemDisplayName;
                playerName = scoutedInfo.Player.Name;
            }
            else
            {
                // Fallback if location wasn't pre-scouted
                LogUtility.Warn($"Location ID: {locationId} was not pre-scouted! Using default values!");
            }

            // Build the message
            var message = $"Found [aqua]{playerName}[/aqua]'s [gold][sine]{itemName}[/sine][/gold] at [green]{locationName}[/green]!";
            EnqueueNotification(
                message,
                NotificationType.LocationCheck);
        }

        /// <summary>
        /// Display an Error Message to the user
        /// </summary>
        /// <param name="errorMessage">The error to display</param>
        public static void ShowError(string errorMessage)
        {
            var message = $"⚠ Error: {errorMessage}";
            EnqueueNotification(
                message,
                NotificationType.Error);
        }

        /// <summary>
        /// Displays a notification as-is
        /// </summary>
        /// <param name="msg">The message to display</param>
        public static void ShowRawText(string msg)
        {
            EnqueueNotification(
                msg,
                NotificationType.Info);
        }

        #endregion
    }
}