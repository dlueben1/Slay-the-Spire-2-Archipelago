using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Unlocks;
using StS2AP.Data;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static StS2AP.Data.CharTable;

namespace StS2AP.Patches
{
    /// <summary>
    /// Patches for `UnlockState`.
    /// Allows us to control the unlock state of characters in the game.
    /// </summary>
    public static class Patches_UnlockCharacters
    {
        /// <summary>
        /// Allows us to control which characters are registered as unlocked, using local state (derived from Archipelago Options)
        /// instead of in-game data/saves
        /// </summary>
        [HarmonyPatch(typeof(UnlockState), "get_Characters", [])]
        public static class OverrideUnlockedCharacterData
        {
            [HarmonyPostfix]
            static void Postfix(ref IEnumerable<CharacterModel> __result)
            {
                LogUtility.Debug($"OverrideUnlockedCharacterData: Overriding unlocked characters. UnlockedCharacters count: {ArchipelagoClient.Progress.UnlockedCharacters.Count}");
                foreach (var c in ArchipelagoClient.Progress.UnlockedCharacters)
                    LogUtility.Debug($"OverrideUnlockedCharacterData: Unlocked character in progress: {c.Id.Entry}");
                __result = ArchipelagoClient.Progress.UnlockedCharacters;
            }
        }

        /// <summary>
        /// Allows us to control which characters are shown in the character select menu, so we can hide options not in the Multiworld.
        /// 
        /// Patching `OnSubmenuOpened()` instead of `InitCharacterButtons()`, because the obvious candidate `InitCharacterButtons()` is a private method
        /// called only once from _Ready, making it a JIT inlining candidate that Harmony cannot reliably patch (my theory).
        /// 
        /// `OnSubmenuOpened()` is public, virtual, and fires every time the screen opens — safe from inlining.
        /// </summary>
        [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened), [])]
        public static class OverrideCharacterSelectMenuOptions
        {
            private static readonly FieldInfo CharButtonContainerField =
                typeof(NCharacterSelectScreen)
                .GetField("_charButtonContainer", BindingFlags.NonPublic | BindingFlags.Instance)!;

            [HarmonyPostfix]
            public static void Postfix(NCharacterSelectScreen __instance)
            {
                LogUtility.Debug($"OverrideCharacterSelectMenuOptions: OnSubmenuOpened postfix fired. AvailableCharacters: [{string.Join(", ", ArchipelagoClient.Settings.AvailableCharacters)}]");

                if (CharButtonContainerField.GetValue(__instance) is not Control container)
                {
                    LogUtility.Debug("OverrideCharacterSelectMenuOptions: Could not find _charButtonContainer — skipping");
                    return;
                }

                LogUtility.Debug($"OverrideCharacterSelectMenuOptions: Found character button container '{container.Name}'. Iterating through buttons...");

                foreach (NCharacterSelectButton button in container.GetChildren().OfType<NCharacterSelectButton>())
                {
                    /// Button names are set as "{characterId.Entry}_button" during Init — Entry is lowercase (e.g. "silent_button")
                    /// We .Capitalize() after stripping the suffix to get the AP-style name (e.g. "Silent")
                    string rawName = button.Name.ToString();
                    string characterEntry = rawName.Replace("_button", "").Capitalize();
                    LogUtility.Debug($"OverrideCharacterSelectMenuOptions: Checking button '{rawName}' → characterEntry '{characterEntry}'");

                    // Hide any character that isn't in the available characters list for this Archipelago slot
                    bool isVisible = ArchipelagoClient.Settings.AvailableCharacters.Contains(characterEntry);
                    LogUtility.Debug($"OverrideCharacterSelectMenuOptions: '{characterEntry}' isVisible={isVisible}");

                    if (!isVisible)
                    {
                        LogUtility.Debug($"OverrideCharacterSelectMenuOptions: Hiding button '{rawName}' (character '{characterEntry}' not in slot)");
                        button.Visible = false;
                    }
                }
            }
        }

        /// <summary>
        /// Subscribes to `ArchipelagoClient.CharacterUnlocked` when the character select screen opens,
        /// so that receiving an unlock item while the screen is open immediately enables the correct button
        /// without having to close and re-open the screen.
        ///
        /// We store the generated handler delegate per screen instance in a Dictionary so that
        /// OnSubmenuClosed can look up and remove the exact same delegate — extension method calls
        /// create a new delegate object each time, so storing the instance is the only safe way to
        /// unsubscribe correctly.
        /// </summary>
        [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened), [])]
        public static class SubscribeToUnlockEventOnOpen
        {
            private static readonly FieldInfo CharButtonContainerField =
                typeof(NCharacterSelectScreen)
                .GetField("_charButtonContainer", BindingFlags.NonPublic | BindingFlags.Instance)!;

            /// <summary>
            /// Per-screen-instance handler storage.
            /// Keyed on the screen instance so UnsubscribeFromUnlockEventOnClose can remove the exact delegate.
            /// </summary>
            internal static readonly Dictionary<NCharacterSelectScreen, Action<APItemCharID>> Handlers = new();

            [HarmonyPostfix]
            public static void Postfix(NCharacterSelectScreen __instance)
            {
                if (__instance == null)
                {
                    LogUtility.Debug("SubscribeToUnlockEventOnOpen: __instance is null — skipping subscription");
                    return;
                }

                /// If there's already a handler registered for this instance, remove the old one first
                /// (guards against double-open without a matching close, which shouldn't happen but is defensive)
                if (Handlers.TryGetValue(__instance, out var existing))
                {
                    LogUtility.Debug("SubscribeToUnlockEventOnOpen: Found stale handler for this instance — removing before re-subscribing");
                    ArchipelagoClient.CharacterUnlocked -= existing;
                    Handlers.Remove(__instance);
                }

                // Create a closure-bound handler and store it so we can unsubscribe the exact same delegate later
                Action<APItemCharID> handler = id => HandleCharacterUnlocked(__instance, id);
                Handlers[__instance] = handler;
                ArchipelagoClient.CharacterUnlocked += handler;
                LogUtility.Debug($"SubscribeToUnlockEventOnOpen: Subscribed CharacterUnlocked handler for screen instance. Total active handlers: {Handlers.Count}");
            }

            /// <summary>
            /// Called when a character unlock item arrives while this screen is open.
            /// Finds the corresponding button by its raw game name and calls UnlockIfPossible() on it.
            /// </summary>
            public static void HandleCharacterUnlocked(NCharacterSelectScreen screen, APItemCharID charId)
            {
                LogUtility.Debug($"HandleCharacterUnlocked: Received unlock event for charId={charId} on screen instance {screen?.GetInstanceId()}");

                if (screen == null)
                {
                    LogUtility.Debug("HandleCharacterUnlocked: screen is null — ignoring");
                    return;
                }

                if (CharButtonContainerField.GetValue(screen) is not Control container)
                {
                    LogUtility.Debug("HandleCharacterUnlocked: Could not find _charButtonContainer on screen");
                    return;
                }

                /// Build the expected button name from the APItemCharID (e.g. APItemCharID.Silent → "silent_button").
                /// We use case-insensitive comparison as a safety net, since the game's Id.Entry casing
                /// could vary (the node dump above will confirm the real casing in the logs).
                string buttonName = charId.ToString().ToLower() + "_button";
                LogUtility.Debug($"HandleCharacterUnlocked: Looking for button matching '{buttonName}' (case-insensitive)");

                var button = container.GetChildren()
                    .OfType<NCharacterSelectButton>()
                    .FirstOrDefault(b => string.Equals(b.Name.ToString(), buttonName, StringComparison.OrdinalIgnoreCase));

                if (button == null)
                {
                    LogUtility.Debug($"HandleCharacterUnlocked: No button found matching '{buttonName}' (case-insensitive) — check the node dump above for real button names. Unlock will take effect next time the screen opens.");
                    return;
                }

                LogUtility.Debug($"HandleCharacterUnlocked: Found button '{buttonName}'. IsLocked={button.IsLocked}. Calling UnlockIfPossible()...");

                // UnlockIfPossible checks the unlock state internally — safe to call even if already unlocked
                button.UnlockIfPossible();
                LogUtility.Success($"HandleCharacterUnlocked: Called UnlockIfPossible() on button '{buttonName}'");
            }
        }

        /// <summary>
        /// Unsubscribes from `ArchipelagoClient.CharacterUnlocked` when the character select screen closes,
        /// so we don't hold a stale reference to a closed screen.
        /// Uses the Handlers dictionary to look up the exact delegate that was registered on open.
        /// </summary>
        [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuClosed), [])]
        public static class UnsubscribeFromUnlockEventOnClose
        {
            [HarmonyPostfix]
            public static void Postfix(NCharacterSelectScreen __instance)
            {
                LogUtility.Debug($"UnsubscribeFromUnlockEventOnClose: OnSubmenuClosed postfix fired for instance {__instance?.GetInstanceId()}");

                if (__instance == null)
                {
                    LogUtility.Debug("UnsubscribeFromUnlockEventOnClose: __instance is null — nothing to unsubscribe");
                    return;
                }

                if (SubscribeToUnlockEventOnOpen.Handlers.TryGetValue(__instance, out var handler))
                {
                    ArchipelagoClient.CharacterUnlocked -= handler;
                    SubscribeToUnlockEventOnOpen.Handlers.Remove(__instance);
                    LogUtility.Debug($"UnsubscribeFromUnlockEventOnClose: Unsubscribed and removed handler. Remaining active handlers: {SubscribeToUnlockEventOnOpen.Handlers.Count}");
                }
                else
                {
                    LogUtility.Debug("UnsubscribeFromUnlockEventOnClose: No handler found in dictionary for this instance — nothing to unsubscribe");
                }
            }
        }
    }
}
