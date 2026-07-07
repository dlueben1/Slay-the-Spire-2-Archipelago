using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using StS2AP.Models;
using STS2RitsuLib.Settings;

namespace StS2AP.Utils
{
    /// <summary>
    /// Provides utility methods for handling Death Link functionality in the Archipelago mod,
    /// taking into account both Slot and Client settings.
    /// 
    /// Because of the disparity between Slot and Client settings, this utility class should be used
    /// as the *single source of truth* for what Death Link options are in-play.
    /// </summary>
    public static class DeathLinkUtility
    {
        #region General

        /// <summary>
        /// Based on both Client and Server settings, determines if Death Link is enabled for this player.
        /// </summary>
        public static bool IsDeathLinkEnabled => ArchipelagoClient.LocalSettings.Value.OverrideDeathLinkOptions ? ArchipelagoClient.LocalSettings.Value.EnableDeathLink : (ArchipelagoClient.Settings?.IsDeathLinkEnabled ?? false);

        public static void Initialize()
        {
            // Subscribe to all mod settings value writes
            ModSettingsBindingWriteEvents.ValueWritten += OnSettingChanged;
        }

        #endregion

        #region Damage

        /// <summary>
        /// The percentage of your Max Health to use when calculating the damage you take from a Death Link.
        /// </summary>
        public static int DeathLinkDamagePercent => ArchipelagoClient.LocalSettings.Value.OverrideDeathLinkOptions ? ArchipelagoClient.LocalSettings.Value.DeathLinkPercentDamage : ArchipelagoClient.Settings?.DeathLinkDamagePercent ?? 0;

        #endregion

        #region Death Fragments

        /// <summary>
        /// Whether or not Death Fragments are enabled
        /// </summary>
        public static bool AreDeathFragmentsEnabled => ArchipelagoClient.LocalSettings.Value.OverrideDeathLinkOptions ? ArchipelagoClient.LocalSettings.Value.EnableDeathFragments : (ArchipelagoClient.Settings?.EnableDeathFragments ?? false);

        /// <summary>
        /// Adds the Death Link Curse to the deck.
        /// </summary>
        private async static Task AddCurseToDeck(string deathMessage)
        {
            // Cache the death message, so that the Curse can store it after it's been properly cloned
            ArchipelagoClient.LastDeathLinkMessage = deathMessage;

            try
            {
                // Permanently adds a clone (with the SavedProperty stamped) to the deck
                await CardPileCmd.AddCursesToDeck(new[] { ModelDb.Card<DeathLinkCurse>() }, GameUtility.CurrentPlayer!);

                // Also add to the current combat draw pile, if in combat
                if (GameUtility.CurrentPlayer!.Creature.CombatState != null)
                {
                    var combatCard = (DeathLinkCurse)GameUtility.CurrentPlayer.Creature.CombatState.CreateCard(
                        ModelDb.Card<DeathLinkCurse>(), GameUtility.CurrentPlayer);
                    await CardPileCmd.AddGeneratedCardToCombat(combatCard, PileType.Draw, GameUtility.CurrentPlayer, CardPilePosition.Random);
                }
            }
            finally
            {
                // Flush the buffer death message (good stewardship but honestly probably unnecessary)
                ArchipelagoClient.LastDeathLinkMessage = null;
            }
        }

        #endregion

        #region Receiving Death Links

        /// <summary>
        /// Processes a received Death Link from the Multiworld.
        /// 
        /// BTW, I'm using `TaskHelper.RunSafely()` because that seems to be a way that MegaCrit runs Async tasks from sync functions,
        /// and it's helpful because it won't silently consume any exceptions like the `_ = func()` syntax does.
        /// </summary>
        public static void OnDeathLinkReceived(DeathLink info)
        {
            // It shouldn't be possible, but to be defensive, ignore this function if Death Link is disabled and somehow we got here
            if (!IsDeathLinkEnabled) return;

            // Log/Notify
            LogUtility.Info($"Received Death Link from {info.Source}");
            NotificationUtility.ShowDeathLink(info);

            // If we're not in the run, there's nothing to do other than log it
            if (!GameUtility.IsInRun || GameUtility.CurrentPlayer == null) return;

            // If Damage is to be dealt to the player, calculate it and apply it
            int newHp = GameUtility.CurrentPlayer.Creature.CurrentHp;
            if (DeathLinkDamagePercent > 0)
            {
                /// Record the timestamp so the send patch can suppress re-triggering a Death Link
                /// if the incoming damage is lethal and we hit the Game Over screen.
                ArchipelagoClient.LastDeathLinkReceivedAt = DateTime.UtcNow;

                /// Deal a percentage of the player's max health, as damage
                int damage = Mathf.RoundToInt(GameUtility.CurrentPlayer.Creature.MaxHp * (DeathLinkDamagePercent / 100.0f));
                newHp = Math.Max(0, GameUtility.CurrentPlayer.Creature.CurrentHp - damage);
                TaskHelper.RunSafely(CreatureCmd.SetCurrentHp(GameUtility.CurrentPlayer.Creature, newHp));
            }
            // If Death Fragments are enabled, and the player hasn't died, add a curse to the player's deck
            if (newHp > 0 && AreDeathFragmentsEnabled)
            {
                var deathMsg = info.Cause ?? $"{info.Source} died";
                if (!deathMsg.Contains(info.Source)) deathMsg = $"{deathMsg} ({info.Source})";
                TaskHelper.RunSafely(AddCurseToDeck(deathMsg));
            }
        }

        #endregion

        #region Handle Settings Changes
        
        /// <summary>
        /// When Death Link Settings are changed, this function reacts to those changes.
        /// </summary>
        private static void OnSettingChanged(IModSettingsBinding binding)
        {
            // Grab the entry ID of the setting that was changed
            var entryId = binding.DataKey;

            LogUtility.Info($"DEBUG: entryId: {entryId}, value: {binding.ToString()}");
            LogUtility.Info($"DEBUG: IsDeathLinkEnabled: {IsDeathLinkEnabled} / SLOT: {ArchipelagoClient.Settings.IsDeathLinkEnabled} / LOCAL: {ArchipelagoClient.LocalSettings.Value.EnableDeathLink}");

            // Listen for changes that may require us to enable/disable death link
            if (entryId == "apsettings")
            {
                // Ignore if we're not connected to Archipelago
                if(!ArchipelagoClient.IsConnected) return;

                // Determine if we need to Enable or Disable Death Link
                if(IsDeathLinkEnabled)
                {
                    LogUtility.Info("Enabling Death Link");
                    ArchipelagoClient.DeathLinkController.EnableDeathLink();
                }
                else
                {
                    LogUtility.Info("Disabling Death Link");
                    ArchipelagoClient.DeathLinkController.DisableDeathLink();
                }
            }
        }

        #endregion
    }
}
