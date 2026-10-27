using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using StS2AP.Models;

namespace StS2AP.Utils;

/// <summary>
/// Applies <see cref="MissingCharacterFallbackPlan"/> to slot settings while configured characters'
/// mods are not loaded: every character's checks and wins count for them, and Defect is always
/// playable. Defect is vanilla, so it is always installed, and one fixed stand-in is easier for
/// players to remember than a per-slot choice.
///
/// The slot's character table is left exactly as the server sent it; only lookups by game
/// character go through <see cref="TryGetConfig"/>, which can answer for Defect with a borrowed
/// configuration.
/// </summary>
internal static class MissingCharacterFallback
{
    private sealed record Resolution(MissingCharacterFallbackPlan Plan, CharacterConfig? BorrowedConfig);

    // Keyed by the character map instance: ArchipelagoSettings replaces the map whenever its
    // characters are reassigned, so a cached plan never outlives the table it describes.
    private static readonly ConditionalWeakTable<IDictionary<string, CharacterConfig>, Resolution> Cache = new();

    public static CharacterModel FallbackCharacter => ModelDb.Character<Defect>();

    public static string FallbackCharacterId => FallbackCharacter.Id.Entry;

    public static MissingCharacterFallbackPlan GetPlan(ArchipelagoSettings settings) =>
        Resolve(settings).Plan;

    /// <summary>
    /// Returns the configuration Defect plays with when it borrows a missing character's slot
    /// configuration. Its <see cref="CharacterConfig.Name"/> stays the missing character's AP
    /// name, because location names (and so the earned location IDs) are built from it.
    /// </summary>
    public static bool TryGetBorrowedConfig(
        ArchipelagoSettings settings,
        string characterId,
        out CharacterConfig config)
    {
        Resolution resolution = Resolve(settings);
        if (resolution.BorrowedConfig != null
            && string.Equals(characterId, resolution.Plan.FallbackCharacterId, StringComparison.OrdinalIgnoreCase))
        {
            config = resolution.BorrowedConfig;
            return true;
        }

        config = null!;
        return false;
    }

    /// <summary>Lists the missing characters for player-facing text, e.g. "Hermit, Watcher".</summary>
    public static string DescribeMissingCharacters(ArchipelagoSettings settings)
    {
        MissingCharacterFallbackPlan plan = GetPlan(settings);
        return string.Join(", ", plan.MissingCharacters.Select(missing => DisplayName(settings, missing)));
    }

    public static string DescribeBorrowedCharacter(ArchipelagoSettings settings)
    {
        MissingCharacterFallbackPlan plan = GetPlan(settings);
        return plan.BorrowedCharacter is { } borrowed ? DisplayName(settings, borrowed) : string.Empty;
    }

    private static string DisplayName(ArchipelagoSettings settings, MissingCharacterFallbackPlan.SlotCharacter character)
    {
        CharacterConfig? config = settings.Characters.Values
            .FirstOrDefault(candidate => candidate.CharOffset == character.Number);
        if (!string.IsNullOrWhiteSpace(config?.Name))
            return config.Name;
        return string.IsNullOrWhiteSpace(character.Id) ? "<missing id>" : character.Id;
    }

    private static Resolution Resolve(ArchipelagoSettings settings) =>
        Cache.GetValue(settings.Characters, characters => Create(characters));

    private static Resolution Create(IDictionary<string, CharacterConfig> characters)
    {
        var installedIds = ModelDb.AllCharacters
            .Select(character => character.Id.Entry)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        MissingCharacterFallbackPlan plan = MissingCharacterFallbackPlan.Create(
            characters.Values.Select(config =>
                new MissingCharacterFallbackPlan.SlotCharacter(config.OfficialName, config.CharOffset)),
            installedIds.Contains,
            FallbackCharacterId);
        if (!plan.IsActive)
            return new Resolution(plan, null);

        CharacterConfig? borrowedConfig = null;
        if (plan.BorrowedCharacter is { } borrowed)
        {
            CharacterConfig source = characters.Values.First(config => config.CharOffset == borrowed.Number);
            borrowedConfig = source.Clone();
            borrowedConfig.OfficialName = plan.FallbackCharacterId;
            // Defect must stay playable even when the character it covers is still locked.
            borrowedConfig.Locked = false;
        }

        string fallbackConfiguration = plan.BorrowedCharacter is { } borrowedCharacter
            ? $"AP character #{borrowedCharacter.Number}'s configuration"
            : "its own configuration";
        LogUtility.Warn(
            $"Character mod(s) not loaded for AP characters [{string.Join(", ", plan.MissingCharacters)}]; "
                + $"every character's checks also count for them, and {plan.FallbackCharacterId} "
                + $"plays with {fallbackConfiguration}."
        );
        return new Resolution(plan, borrowedConfig);
    }
}
