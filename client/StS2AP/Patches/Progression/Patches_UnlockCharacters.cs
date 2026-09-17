using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Unlocks;

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
                if (!MultiplayerSupport.IsRealMultiplayerRun && MultiplayerSupport.IsLocalGuest)
                {
                    __result = ModelDb.AllCharacters;
                    return;
                }

                // During the lobby this local override controls only this process's selectable
                // characters. Once a multiplayer run launches, preserve each serialized remote
                // player's own UnlockState instead of replacing it with the local AP list.
                // CHANGE: please change this to actually use the AP progress of the individual people
                if (MultiplayerSupport.IsRealMultiplayerRun)
                    return;

                LogUtility.Debug($"OverrideUnlockedCharacterData: Overriding unlocked characters. UnlockedCharacters count: {ArchipelagoClient.Progress.UnlockedCharacters.Count}");
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
            [HarmonyPostfix]
            public static void Postfix(NCharacterSelectScreen __instance)
            {
                RefreshForCurrentParticipation(__instance);
            }

            internal static void RefreshForCurrentParticipation(
                NCharacterSelectScreen __instance)
            {
                if (MultiplayerSupport.IsLocalGuest)
                {
                    if (__instance._charButtonContainer is Control guestContainer)
                    {
                        foreach (NCharacterSelectButton button in guestContainer
                                     .GetChildren()
                                     .OfType<NCharacterSelectButton>())
                        {
                            button.Visible = true;
                            button.UnlockIfPossible();
                        }
                    }
                    return;
                }

                if (ArchipelagoClient.Settings?.Characters == null)
                {
                    LogUtility.Warn(
                        "OverrideCharacterSelectMenuOptions: AP settings are not available yet"
                    );
                    return;
                }

                LogUtility.Debug($"OverrideCharacterSelectMenuOptions: OnSubmenuOpened postfix fired. AvailableCharacters: [{string.Join(", ", ArchipelagoClient.Settings.Characters.Values)}]");

                if (__instance._charButtonContainer is not Control container)
                {
                    LogUtility.Debug("OverrideCharacterSelectMenuOptions: Could not find _charButtonContainer — skipping");
                    return;
                }

                LogUtility.Debug($"OverrideCharacterSelectMenuOptions: Found character button container '{container.Name}'. Iterating through buttons...");
                var buttons = container.GetChildren().OfType<NCharacterSelectButton>().ToArray();
                foreach (NCharacterSelectButton button in buttons)
                {
                    var charModel = button.Character;
                    LogUtility.Info($"Character Model id: {charModel.Id.Entry}");
                    var name = charModel.Id.Entry;
                    LogUtility.Info($"OverrideCharacterSelectMenuOptions: Checking button with character '{name}'");

                    // Hide any character that isn't in the available characters list for this Archipelago slot
                    bool isVisible = ArchipelagoClient.Settings.Characters.ContainsKey(name);
                    LogUtility.Info($"OverrideCharacterSelectMenuOptions: '{name}' isVisible={isVisible}");
                    LogUtility.Info($"Current Configured Characters: {string.Join(",", ArchipelagoClient.Settings.Characters.Keys)}");

                    button.Visible = isVisible;

                    // The main menu owns one character-select screen for the lifetime of the
                    // process. UnlockIfPossible is intentionally one-way, so a character from a
                    // departed AP slot otherwise remains unlocked on this reused button. Re-run
                    // the native initialization against the current patched UnlockState first.
                    bool wasLocked = button.IsLocked;
                    button.Init(charModel, __instance);
                    if (!wasLocked && button.IsLocked)
                    {
                        LogUtility.Info(
                            $"Relocked stale character button {name} for the current AP slot"
                        );
                    }
                    if (!isVisible)
                        LogUtility.Debug($"OverrideCharacterSelectMenuOptions: Hiding button for character '{name}' (character not in slot)");
                    else
                        button.UnlockIfPossible();
                }

                bool hasValidSelection = buttons.Any(button =>
                    button.IsSelected
                    && button.Visible
                    && !button.IsRandom
                    && ArchipelagoClient.CanSelectCharacter(button.Character, out _)
                );
                if (hasValidSelection)
                    return;

                NCharacterSelectButton? replacement = buttons.FirstOrDefault(button =>
                    button.Visible
                    && !button.IsRandom
                    && ArchipelagoClient.CanSelectCharacter(button.Character, out _)
                );
                if (replacement != null)
                {
                    LogUtility.Info(
                        $"Selecting {replacement.Character.Id.Entry} because the previous "
                            + "character is unavailable for this AP slot"
                    );
                    replacement.Select();
                    replacement.GrabFocus();
                    return;
                }

                __instance.GetNode<NConfirmButton>("ConfirmButton").Disable();
                LogUtility.Error("No selectable character is available for this AP slot");
            }
        }

        /// <summary>
        /// Subscribes to `Patches_ItemProcessor.CharacterUnlocked` when the character select screen opens,
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
            /// <summary>
            /// Per-screen-instance handler storage.
            /// Keyed on the screen instance so UnsubscribeFromUnlockEventOnClose can remove the exact delegate.
            /// </summary>
            internal static readonly Dictionary<NCharacterSelectScreen, Action<CharacterConfig>> Handlers = new();

            [HarmonyPostfix]
            public static void Postfix(NCharacterSelectScreen __instance)
            {
                if (MultiplayerSupport.IsLocalGuest)
                    return;

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
                    Patches_ItemProcessor.CharacterUnlocked -= existing;
                    Handlers.Remove(__instance);
                }

                // Create a closure-bound handler and store it so we can unsubscribe the exact same delegate later
                Action<CharacterConfig> handler = config => HandleCharacterUnlocked(__instance, config);
                Handlers[__instance] = handler;
                Patches_ItemProcessor.CharacterUnlocked += handler;
                LogUtility.Debug($"SubscribeToUnlockEventOnOpen: Subscribed CharacterUnlocked handler for screen instance. Total active handlers: {Handlers.Count}");
            }

            /// <summary>
            /// Called when a character unlock item arrives while this screen is open.
            /// Finds the corresponding button by its raw game name and calls UnlockIfPossible() on it.
            /// </summary>
            public static void HandleCharacterUnlocked(NCharacterSelectScreen? screen, CharacterConfig config)
            {
                // Null check
                if (screen is null)
                {
                    LogUtility.Debug("HandleCharacterUnlocked: screen is null — ignoring");
                    return;
                }

                // Check if we're a stale handler (screen is disposed)
                if (!GodotObject.IsInstanceValid(screen))
                {
                    // Remove this handler
                    if (Handlers.TryGetValue(screen, out var handler))
                    {
                        Patches_ItemProcessor.CharacterUnlocked -= handler;
                    }
                    Handlers.Remove(screen);

                    // And ignore the rest of the function
                    return;
                }

                LogUtility.Debug($"HandleCharacterUnlocked: Received unlock event for {config.OfficialName} on screen instance {screen.GetInstanceId()}");

                if (screen._charButtonContainer is not Control container)
                {
                    LogUtility.Debug("HandleCharacterUnlocked: Could not find _charButtonContainer on screen");
                    return;
                }

                var button = container.GetChildren()
                    .OfType<NCharacterSelectButton>()
                    .FirstOrDefault(b => string.Equals(b.Character.Id.Entry, config.OfficialName, StringComparison.OrdinalIgnoreCase));

                if (button == null)
                {
                    LogUtility.Debug($"HandleCharacterUnlocked: No button found matching '{config.OfficialName}' (case-insensitive) — check the node dump above for real button names. Unlock will take effect next time the screen opens.");
                    return;
                }

                LogUtility.Debug($"HandleCharacterUnlocked: Found button '{config.OfficialName}'. IsLocked={button.IsLocked}. Calling UnlockIfPossible()...");

                // UnlockIfPossible checks the unlock state internally — safe to call even if already unlocked
                button.UnlockIfPossible();
                LogUtility.Success($"HandleCharacterUnlocked: Called UnlockIfPossible() on button '{config.OfficialName}'");
            }
        }

        /// <summary>
        /// Unsubscribes from `Patches_ItemProcessor.CharacterUnlocked` when the character select screen closes,
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
                    Patches_ItemProcessor.CharacterUnlocked -= handler;
                    SubscribeToUnlockEventOnOpen.Handlers.Remove(__instance);
                    LogUtility.Debug($"UnsubscribeFromUnlockEventOnClose: Unsubscribed and removed handler. Remaining active handlers: {SubscribeToUnlockEventOnOpen.Handlers.Count}");
                }
                else
                {
                    LogUtility.Debug("UnsubscribeFromUnlockEventOnClose: No handler found in dictionary for this instance — nothing to unsubscribe");
                }
            }
        }

        [HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.GetUnlockText))]
        public static class OverrideUnlockText
        {
            [HarmonyPrefix]
            public static bool Prefix(ref LocString __result)
            {
                __result = new LocString("characters", "APCHARACTER.unlockText");

                return false;
            }
        }

        [HarmonyPatch(typeof(NCharacterSelectScreen), "UpdateRandomCharacterVisibility")]
        public static class DisableRitsuStuff
        {
            [HarmonyPrefix]
            public static bool DoNothing()
            {
                return false;
            }
        }
    }
}
