using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Models
{
    /// <summary>
    /// Represents a collection of user-configurable settings for the mod.
    /// 
    /// Compared to <seealso cref="ArchipelagoSettings"/>, these settings represent
    /// local preferences and configuration, intended to be read/written to at runtime.
    /// </summary>
    public sealed class ClientSettings
    {
        #region Notifications

        /// <summary>
        /// Preference for which reward notifications to display.
        /// Values: "All", "My Checks & Items", "Only My Checks"
        /// </summary>
        public string RewardNotificationPref { get; set; } = "All";

        #endregion

        #region Death Link

        /// <summary>
        /// If true, allows overriding the server-provided Death Link settings with client-side options.
        /// When false, the server settings from ArchipelagoSettings are used instead.
        /// </summary>
        public bool OverrideDeathLinkOptions { get; set; } = false;

        /// <summary>
        /// Client-side override for Death Link enablement.
        /// Only applies if OverrideDeathLinkOptions is true.
        /// </summary>
        public bool EnableDeathLink { get; set; } = false;

        /// <summary>
        /// Client-side override for Death Fragment enablement.
        /// Only applies if OverrideDeathLinkOptions is true.
        /// </summary>
        public bool EnableDeathFragments { get; set; } = false;

        /// <summary>
        /// Client-side override for Death Link damage percentage.
        /// Value should be between 0 and 100, inclusive.
        /// Only applies if OverrideDeathLinkOptions is true.
        /// </summary>
        public int DeathLinkPercentDamage { get; set; } = 15;

        #endregion

        #region Key/Button Bindings

        /// <summary>
        /// Keybinding for opening the Archipelago Loot window.
        /// </summary>
        public string OpenArchLootHotKey { get; set; } = "P";

        #endregion
    }
}
