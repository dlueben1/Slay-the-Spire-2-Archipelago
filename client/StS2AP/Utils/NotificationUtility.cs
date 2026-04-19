using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.RichTextTags;
using StS2AP.UI;
using System.Collections.Concurrent;
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
        private static readonly ConcurrentQueue<ArchipelagoNotification> _queue = new();
        private static readonly ConcurrentQueue<ArchipelagoNotification> _devQueue = new();

        /// <summary>
        /// Represents a single notification.
        /// </summary>
        public class ArchipelagoNotification
        {
            public string Message { get; set; }
            public NotificationType Type { get; set; }
            public double DisplayDuration { get; set; } = 3.0;
            public bool ForceIntoDevConsole { get; set; } = false;
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
        private static void EnqueueNotification(
            string message, 
            NotificationType type = NotificationType.Info, 
            bool devConsoleOnly = false, 
            double timeout=3.0,
            bool forceIntoDevConsole = false)
        {
            LogUtility.Info($"Attempting to enqueue notification {message} {type}");
            var notification = new ArchipelagoNotification(message, type);
            notification.DisplayDuration = timeout;
            notification.ForceIntoDevConsole = forceIntoDevConsole;
            if (!devConsoleOnly)
            { 
                _queue.Enqueue(notification);
            }
            _devQueue.Enqueue(notification);
            LogUtility.Info($"Notification queued ({type}): {message}");
        }

        /// <summary>
        /// Dequeues the next notification, or returns null if none are available.
        /// </summary>
        public static ArchipelagoNotification? DequeueNotification()
        {
            if(_queue.TryDequeue(out var result))
            {
                //LogUtility.Info($"Notification dequeued: {result.Message}");
                return result;
            }
            return null;
        }

        /// <summary>
        /// Dequeues the next notification for the dev console, or returns null if none are available.
        /// </summary>
        public static ArchipelagoNotification? DequeueDevNotification()
        {
            if(_devQueue.TryDequeue(out var result))
            {
                //LogUtility.Info($"Notification dequeued: {result.Message}");
                return result;
            }
            return null;
        }

        public static ArchipelagoNotification? PeekDevNotification()
        {
            if(_devQueue.TryPeek(out var result))
            {
                return result;
            }
            return null;
        }


        /// <summary>
        /// Returns the number of queued notifications.
        /// </summary>
        public static int GetQueueCount()
        {
            return _queue.Count;
        }

        /// <summary>
        /// Clears all queued notifications.
        /// </summary>
        public static void ClearQueue()
        {
            _queue.Clear();
            _devQueue.Clear();
            LogUtility.Info("Notification queue cleared");
        }

        #endregion

        #region Display Notifications

        /// <summary>
        /// Returns an icon for each AP item, if one exists; intended to be processes by godot
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
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
            var result = ToColoredString(msg);
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

        public static void HandleOtherAPMessages(LogMessage message, bool devConsoleOnly = false, double timeout = 3.0, bool forceIntoDevConsole = false)
        {

            var result = ToColoredString(message, null);
            EnqueueNotification(result, NotificationType.Info, devConsoleOnly, timeout, forceIntoDevConsole);
        }

        private static String ToColoredString(ItemSendLogMessage msg)
        {
            
            ItemInfo info = msg.Item;
            LogUtility.Info($"ItemInfo: Item Game: {info.ItemGame} Location Game: {info.LocationGame}");
            return ToColoredString(msg, info);
        }

        private static String ToColoredString(LogMessage msg, ItemInfo? info)
        {
            StringBuilder sb = new StringBuilder();
            string? itemIcon = null;

            if(info?.ItemGame == ArchipelagoClient.Game)
            {
                itemIcon = GetItemIcon(info);
            }
            LogUtility.Info($"Got item icon: {itemIcon}");
            
            foreach(var part in msg.Parts)
            {
                var colorWord = ToColorWord(part.Color);
                if(part.Type == Archipelago.MultiClient.Net.MessageLog.Parts.MessagePartType.Item)
                {
                    if(itemIcon != null)
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