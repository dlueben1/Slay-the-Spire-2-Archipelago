using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.RichTextTags;
using StS2AP.UI;
using System.Drawing;
using System.Reflection;
using System.Text;
using static StS2AP.Data.CharTable;
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
            LogUtility.Info($"Attempting to enqueue notification {message} {type}");
            lock (_lock)
            {
                // Enqueue the notification
                var notification = new ArchipelagoNotification(message, type);
                _queue.Enqueue(notification);
                LogUtility.Info($"Notification queued ({type}): {message}");

                // Show the Notification UI if it isn't already visible
                if (!ArchipelagoNotificationUI.IsVisible)
                {
                    // I'm concerned about a race condition with the dev console, hence this safeguard, but it doesn't seem
                    // to work and I don't know why.
                    if (DevConsoleVisible())
                    {
                        ArchipelagoNotificationUI.ResetTimer(3.0);
                    }
                    else
                    {
                        Callable.From(ArchipelagoNotificationUI.ShowMessage).CallDeferred(); // FIX WILL DO A BETTER COMMENT LATER
                    }
                }

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
                if (_queue.Count == 0) return null;
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
            switch (item.GetRawItemID())
            {
                case APItem.OneGold:
                case APItem.FiveGold:
                case APItem.BossGold:
                case APItem._15Gold:
                case APItem._30Gold:
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
                case APItem.Potion:
                    {
                        itemIcon = @"[img]res://images/packed/sprite_fonts/potion_icon.png[/img]";
                        break;
                    }
                case APItem.Unlock:
                    {
                        switch(item.GetStSCharID())
                        {
                            case APItemCharID.Ironclad:
                                {
                                    itemIcon = @"[img]res://images/packed/sprite_fonts/ironclad_energy_icon.png[/img]";
                                    break;
                                }
                            case APItemCharID.Silent:
                                {
                                    itemIcon = @"[img]res://images/packed/sprite_fonts/silent_energy_icon.png[/img]";
                                    break;
                                }
                            case APItemCharID.Defect:
                                {
                                    itemIcon = @"[img]res://images/packed/sprite_fonts/defect_energy_icon.png[/img]";
                                    break;
                                }
                            case APItemCharID.Necrobinder:
                                {
                                    itemIcon = @"[img]res://images/packed/sprite_fonts/necrobinder_energy_icon.png[/img]";
                                    break;
                                }
                            case APItemCharID.Regent:
                                {
                                    itemIcon = @"[img]res://images/packed/sprite_fonts/regent_energy_icon.png[/img]";
                                    break;
                                }
                        }
                        break;
                    }
            }

            // Setup the final string for the notification
            var msg = $"{item.Player} sent you {itemIcon.Replace("  ", " ")} [sine][gold]{item.ItemDisplayName}[/gold]![/sine]";
            EnqueueNotification(
                msg,
                NotificationType.ItemReceived);
        }

        private static string? GetItemIcon(ItemInfo item)
        {
            switch (item.GetRawItemID())
            {
                case APItem.OneGold:
                case APItem.FiveGold:
                case APItem.BossGold:
                case APItem._15Gold:
                case APItem._30Gold:
                        return @"[img]res://images/packed/sprite_fonts/gold_icon.png[/img]";
                case APItem.CardReward:
                case APItem.RareCardReward:
                        return @"[img]res://images/packed/sprite_fonts/card_icon.png[/img]";
                case APItem.Potion:
                        return @"[img]res://images/packed/sprite_fonts/potion_icon.png[/img]";
                case APItem.Unlock:
                        switch (item.GetStSCharID())
                        {
                            case APItemCharID.Ironclad:
                                    return @"[img]res://images/packed/sprite_fonts/ironclad_energy_icon.png[/img]";
                            case APItemCharID.Silent:
                                    return @"[img]res://images/packed/sprite_fonts/silent_energy_icon.png[/img]";
                            case APItemCharID.Defect:
                                    return @"[img]res://images/packed/sprite_fonts/defect_energy_icon.png[/img]";
                            case APItemCharID.Necrobinder:
                                    return @"[img]res://images/packed/sprite_fonts/necrobinder_energy_icon.png[/img]";
                            case APItemCharID.Regent:
                                    return @"[img]res://images/packed/sprite_fonts/regent_energy_icon.png[/img]";
                        }
                    return null;
            }
            return null;
        }

        public static void HandleItemSend(ItemSendLogMessage msg)
        {
            if(!msg.IsRelatedToActivePlayer)
            {
                return;
            }
            if(msg.GetType() == typeof(HintItemSendLogMessage))
            {
                if(((HintItemSendLogMessage) msg).IsFound)
                {
                    return;
                }
            }
            var result = ToColoredString(msg, true);
            NotificationType type = NotificationType.Info;
            if(msg.GetType() != typeof(HintItemSendLogMessage))
            {
                if(msg.IsReceiverTheActivePlayer)
                {
                    type = NotificationType.ItemReceived;
                }
                else if(msg.IsSenderTheActivePlayer)
                {
                    type = NotificationType.LocationCheck;
                }
            }
            EnqueueNotification(result, type);
        }

        public static bool DevConsoleVisible()
        {
            // This doesn't seem to work, and I don't know why?
            return NDevConsole.Instance?.Visible ?? false;
        }

        private static RichTextLabel? GetDevConsoleBuffer()
        {
            var console = NDevConsole.Instance;
            if(console == null)
            {
                return null;
            }

            var outputBufferInfo = console.GetType().GetField("_outputBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
            if(outputBufferInfo == null)
            {
                return null;
            }
            return (RichTextLabel?) outputBufferInfo.GetValue(console);
        }

        public static void WriteToDevConsole(string msg)
        {
            RichTextLabel? outputBuffer = GetDevConsoleBuffer();
            if(outputBuffer != null)
            {
                outputBuffer.Text = outputBuffer.Text + msg + "\n";
            }
        }

        private static String ToColoredString(ItemSendLogMessage msg, bool includeItemIcon)
        {
            
            StringBuilder sb = new StringBuilder();
            ItemInfo info = msg.Item;
            LogUtility.Info($"ItemInfo: Item Game: {info.ItemGame} Location Game: {info.LocationGame}");
            string? itemIcon = null;

            if(info.ItemGame == ArchipelagoClient.Game)
            {
                itemIcon = GetItemIcon(info);
            }
            LogUtility.Info($"Got item icon: {itemIcon}");
            
            foreach(var part in msg.Parts)
            {
                var colorWord = ToColorWord(part.Color);
                if(part.Type == Archipelago.MultiClient.Net.MessageLog.Parts.MessagePartType.Item)
                {
                    if(itemIcon != null && includeItemIcon)
                    {
                        sb.Append(itemIcon.Replace("  ", " "))
                            .Append(' ');
                    }
                    sb.Append("[sine]");
                }
                if(colorWord != null)
                {
                    sb.Append($"[color={colorWord}]");
                }
                sb.Append(part.Text?.Replace("[", "[lb]"));
                if(colorWord != null)
                {
                    sb.Append($"[/color]");
                }
                if(part.Type == Archipelago.MultiClient.Net.MessageLog.Parts.MessagePartType.Item)
                {
                    sb.Append("[/sine]");
                }
            }
            return sb.ToString();
        }

        private static string? ToColorWord(Archipelago.MultiClient.Net.Models.Color? color)
        {
            if(color == null)
            {
                return null; 
            }
            if (Archipelago.MultiClient.Net.Models.Color.Red == color)
                return "red";
            else if (Archipelago.MultiClient.Net.Models.Color.Green == color)
                return "green";
            else if (Archipelago.MultiClient.Net.Models.Color.Yellow == color)
                return "yellow";
            else if (Archipelago.MultiClient.Net.Models.Color.Blue == color)
                return "blue";
            else if (Archipelago.MultiClient.Net.Models.Color.Magenta == color)
                return "magenta";
            else if (Archipelago.MultiClient.Net.Models.Color.Cyan == color)
                return "cyan";
            else if (Archipelago.MultiClient.Net.Models.Color.Black == color)
                return "black";
            // No one likes the color white
            //else if (Archipelago.MultiClient.Net.Models.Color.White == color)
            //    return "white";
            else if (Archipelago.MultiClient.Net.Models.Color.SlateBlue == color)
                return "slateblue";
            else if (Archipelago.MultiClient.Net.Models.Color.Salmon == color)
                return "salmon";
            else if (Archipelago.MultiClient.Net.Models.Color.Plum == color)
                return "plum";
            return null;
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

                // And don't display the notification
                return;
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