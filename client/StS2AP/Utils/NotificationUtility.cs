using StS2AP.UI;

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
        /// </summary>
        public enum NotificationType
        {
            Info,
            ItemReceived,
            LocationCheck,
            Error,
            Warning
        }

        /// <summary>
        /// Queues a notification to be displayed.
        /// </summary>
        public static void EnqueueNotification(string message, NotificationType type = NotificationType.Info)
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

        #region Display Notifications

        public static void ShowItemReceived(string itemName)
        {
            EnqueueNotification(
                itemName,
                NotificationType.ItemReceived);
        }

        public static void ShowLocationChecked(string locationName)
        {
            var message = $"✓ Checked: {locationName}";
            EnqueueNotification(
                message,
                NotificationType.LocationCheck);
        }

        public static void ShowError(string errorMessage)
        {
            var message = $"⚠ Error: {errorMessage}";
            EnqueueNotification(
                message,
                NotificationType.Error);
        }

        public static void ShowStatus(string status)
        {
            EnqueueNotification(
                status,
                NotificationType.Info);
        }

        #endregion
    }
}