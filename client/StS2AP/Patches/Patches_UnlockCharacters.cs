using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Unlocks;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

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
                LogUtility.Debug($"DUMP OF AVAILABLE CHARACTERS: {string.Join(", ", ArchipelagoClient.Settings.AvailableCharacters)}");
                LogUtility.Debug("Postfix for NCharacterSelectScreen.OnSubmenuOpened called. Checking for buttons to hide...");
                if (CharButtonContainerField.GetValue(__instance) is not Control container)
                    return;

                LogUtility.Debug($"Found character button container: {container.Name}. Iterating through buttons...");

                foreach (NCharacterSelectButton button in container.GetChildren().OfType<NCharacterSelectButton>())
                {
                    // Button names are set as "{characterId.Entry}_button" during Init
                    string characterEntry = button.Name.ToString().Replace("_button", "").Capitalize();
                    LogUtility.Debug($"Checking button for character entry: {characterEntry}");

                    // Hide any character that isn't in the unlocked character list for this Archipelago slot
                    //bool isUnlocked = ArchipelagoClient.Progress.UnlockedCharacters
                    //    .Any(c => c.Id.Entry == characterEntry);

                    bool isUnlocked = ArchipelagoClient.Settings.AvailableCharacters.Contains(characterEntry);

                    if (!isUnlocked)
                    {
                        LogUtility.Debug($"Hiding button for character entry: {characterEntry}");
                        button.Visible = false;
                    }
                }
            }
        }
    }
}
