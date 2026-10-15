using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace StS2AP.Utils;

/// <summary>Adapts IntoTheSpireverse's shared character buttons to the local AP roster.</summary>
internal static class IntoTheSpireverseSelection
{
    private const string AltInterface = "IntoTheSpireverse.IntoTheSpireverseCode.Character.IAltCharacter";
    private const string ArrowType = "IntoTheSpireverse.IntoTheSpireverseCode.Ui.NCharAltArrow";
    private const string ButtonPatches = "IntoTheSpireverse.IntoTheSpireverseCode.Patches.NCharacterSelectButtonPatches";

    [ThreadStatic] private static CharacterModel? requestedCharacter;

    private static bool IsActive => !MultiplayerSupport.IsRealMultiplayerRun
        && (MultiplayerSupport.IsLocalGuest || ArchipelagoClient.Settings is not null);

    // These types belong to an optional mod, not to the publicized game API.
    // Resolve its small interface/UI contract at runtime to avoid requiring its DLL.
    private static CharacterModel? GetBaseCharacter(CharacterModel character) =>
        character.GetType().GetInterface(AltInterface)?.GetProperty("BaseCharacterModel")
            ?.GetValue(character) as CharacterModel;

    private static List<CharacterModel> GetFamily(CharacterModel character)
    {
        var baseCharacter = GetBaseCharacter(character) ?? character;
        var alternates = ModelDb.AllCharacters
            .Where(candidate => GetBaseCharacter(candidate)?.Id == baseCharacter.Id).ToList();
        return alternates.Count == 0 ? [] : [baseCharacter, .. alternates];
    }

    private static List<CharacterModel> GetChoices(List<CharacterModel> family) =>
        MultiplayerSupport.IsLocalGuest ? family : family.Where(character =>
            ArchipelagoClient.Settings!.Characters.ContainsKey(character.Id.Entry)).ToList();

    private static bool IsUnlocked(CharacterModel character) =>
        MultiplayerSupport.IsLocalGuest || ArchipelagoClient.CanSelectCharacter(character, out _);

    /// <summary>Runs after the other mod restores its saved alternate in Init.</summary>
    internal static void ResolveInit(ref CharacterModel character, ICharacterSelectButtonDelegate del)
    {
        if (!IsActive || del is not NCharacterSelectScreen)
            return;
        try
        {
            var requested = requestedCharacter ?? character;
            var family = GetFamily(requested);
            if (family.Count > 0)
                character = SharedCharacterSelectionPolicy.Choose(
                    GetChoices(family), requested, IsUnlocked, preferUnlocked: false) ?? requested;
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Could not resolve IntoTheSpireverse character selection: {ex.Message}");
        }
    }

    /// <summary>Returns true when this is a shared button and its visibility was handled.</summary>
    internal static bool RefreshButton(NCharacterSelectButton button, NCharacterSelectScreen screen,
        string? unlockedCharacterId = null)
    {
        if (!IsActive)
            return false;
        try
        {
            var family = GetFamily(button.Character);
            if (family.Count == 0)
                return false;
            if (unlockedCharacterId is not null && !family.Any(character =>
                    string.Equals(character.Id.Entry, unlockedCharacterId, StringComparison.OrdinalIgnoreCase)))
                return false;
            var selected = SharedCharacterSelectionPolicy.Choose(
                GetChoices(family), button.Character, IsUnlocked, preferUnlocked: true);
            button.Visible = selected is not null;
            if (selected is null)
                return true;

            var previousRequest = requestedCharacter;
            try
            {
                // Init's saved-selection prefix must not undo this slot's chosen member.
                requestedCharacter = selected;
                button.Init(selected, screen);
            }
            finally
            {
                requestedCharacter = previousRequest;
            }

            // Native Select() does nothing for an already-selected button. Refresh the
            // lobby, background, ascension and AP tracker against the new member too.
            if (button.IsSelected)
                screen.SelectCharacter(button, button.Character);
            return true;
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Could not refresh IntoTheSpireverse character button: {ex.Message}");
            return false;
        }
    }

    /// <summary>Runs after the other mod creates/synchronizes its arrow.</summary>
    internal static void RefreshArrow(NCharacterSelectButton button, ICharacterSelectButtonDelegate del)
    {
        if (!IsActive || del is not NCharacterSelectScreen)
            return;
        try
        {
            var family = GetFamily(button.Character);
            if (family.Count == 0)
                return;
            var choices = GetChoices(family);
            var arrow = button.GetNodeOrNull<Control>("CharAltArrow");
            if (arrow is null && choices.Count > 1)
            {
                // Its profile gate can omit the arrow even when AP configured both
                // members. Reuse its UI factory without changing saved unlock settings.
                var createArrow = family[1].GetType().Assembly.GetType(ButtonPatches)?.GetMethod(
                    "InitPostfix", BindingFlags.Public | BindingFlags.Static, null,
                    [typeof(NCharacterSelectButton), typeof(CharacterModel),
                        typeof(ICharacterSelectButtonDelegate), typeof(bool)], null);
                if (createArrow is null)
                    throw new MissingMethodException(ButtonPatches, "InitPostfix");
                createArrow.Invoke(null, [button, button.Character, del, false]);
                arrow = button.GetNodeOrNull<Control>("CharAltArrow");
            }
            if (arrow is not null && arrow.GetType().FullName == ArrowType)
            {
                var type = arrow.GetType();
                var characters = type.GetField("Characters");
                var syncIndex = type.GetMethod("SyncIndexToCharacter", [typeof(CharacterModel)]);
                if (characters?.FieldType != typeof(List<CharacterModel>) || syncIndex is null)
                    throw new InvalidOperationException("IntoTheSpireverse arrow API has changed.");
                // Retain a nonempty list even on a hidden button: queued input must
                // never reach the mod's modulo-by-Characters.Count with a zero count.
                characters.SetValue(arrow, choices.Count > 0 ? choices : family);
                syncIndex.Invoke(arrow, [button.Character]);
                arrow.Visible = choices.Count > 1;
            }
            // Native Init does not clear a previously visible lock icon when switching
            // to an unlocked member. This also enables buttons on a reused menu.
            button.UnlockIfPossible();
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Could not refresh IntoTheSpireverse alternate arrow: {ex.Message}");
        }
    }
}
