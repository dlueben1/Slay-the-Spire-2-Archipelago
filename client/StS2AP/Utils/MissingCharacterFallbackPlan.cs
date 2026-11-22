using StS2AP.Data;

namespace StS2AP.Utils;

/// <summary>
/// Decides how a slot copes with characters whose mods are not loaded, so a broken or missing
/// character mod never locks a player out of the slot.
///
/// Like the Slay the Spire 1 Archipelago mod, every playable character's location checks and
/// victories also count for every missing character. One installed vanilla character is also
/// guaranteed to be playable: it uses its own slot configuration when the slot has one, and
/// otherwise borrows the configuration (items, ascension, seed) of the first missing character.
///
/// The plan is derived from the installed characters each time slot settings are prepared and is
/// never saved, so installing or removing character mods takes effect on the next connection.
/// </summary>
internal sealed class MissingCharacterFallbackPlan
{
    // Id = Character's Internal ID in the game
    public readonly record struct SlotCharacter(string Id, long Number);

    public static readonly MissingCharacterFallbackPlan None =
        new(string.Empty, Array.Empty<SlotCharacter>(), null);

    private readonly HashSet<long> _missingNumbers;

    private MissingCharacterFallbackPlan(
        string fallbackCharacterId,
        IReadOnlyList<SlotCharacter> missingCharacters,
        SlotCharacter? borrowedCharacter)
    {
        FallbackCharacterId = fallbackCharacterId;
        MissingCharacters = missingCharacters;
        BorrowedCharacter = borrowedCharacter;
        _missingNumbers = missingCharacters.Select(character => character.Number).ToHashSet();
    }

    /// <summary>The game character ID that is always playable while characters are missing.</summary>
    public string FallbackCharacterId { get; }

    /// <summary>Configured characters that are not installed, in AP character-number order.</summary>
    public IReadOnlyList<SlotCharacter> MissingCharacters { get; }

    /// <summary>
    /// The missing character whose slot configuration the fallback character plays with, or
    /// null when the fallback character has its own configuration in this slot.
    /// </summary>
    public SlotCharacter? BorrowedCharacter { get; }

    public bool IsActive => MissingCharacters.Count > 0;

    public static MissingCharacterFallbackPlan Create(
        IEnumerable<SlotCharacter> configuredCharacters,
        Func<string, bool> isInstalled,
        string fallbackCharacterId)
    {
        SlotCharacter[] configured = configuredCharacters.ToArray();
        SlotCharacter[] missing = configured
            .Where(character => string.IsNullOrWhiteSpace(character.Id) || !isInstalled(character.Id))
            .OrderBy(character => character.Number)
            .ToArray();
        if (missing.Length == 0)
            return None;

        SlotCharacter? ownConfiguration = configured
            .Where(character => string.Equals(
                character.Id, fallbackCharacterId, StringComparison.OrdinalIgnoreCase))
            .Select(character => (SlotCharacter?)character)
            .FirstOrDefault();
        SlotCharacter? borrowed = ownConfiguration == null ? missing[0] : null;

        return new MissingCharacterFallbackPlan(fallbackCharacterId, missing, borrowed);
    }

    public bool IsMissing(long characterNumber) => _missingNumbers.Contains(characterNumber);

    /// <summary>
    /// Returns the requested locations plus the same location in every other missing character's
    /// block. Every character location is mirrored, whoever earned it; a borrowed fallback's
    /// locations sit in the first missing character's block and reach the others the same way.
    /// </summary>
    public IEnumerable<long> WithMirroredLocations(IEnumerable<long> locationIds)
    {
        foreach (long locationId in locationIds)
        {
            yield return locationId;
            if (!IsActive || ArchipelagoIdCodec.IsArchipelagoReservedId(locationId))
                continue;

            long sourceCharacterNumber = ArchipelagoIdCodec.GetAPCharacterNumberFromLocationId(locationId);
            long locationIdOffset = ArchipelagoIdCodec.GetLocationIdOffset(locationId);
            foreach (SlotCharacter missing in MissingCharacters)
            {
                if (missing.Number != sourceCharacterNumber
                    && ArchipelagoIdCodec.TryComposeLocationId(locationIdOffset, missing.Number, out long mirrored))
                {
                    yield return mirrored;
                }
            }
        }
    }

    /// <summary>
    /// Returns the slot characters credited when <paramref name="playedCharacterId"/> wins a run:
    /// the characters it represents plus every missing character.
    /// </summary>
    public IReadOnlyList<string> GoalCharacterIds(string playedCharacterId)
    {
        if (!IsActive)
            return new[] { playedCharacterId };

        return RepresentedCharacterIds(playedCharacterId)
            .Concat(MissingCharacterIds())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Returns the slot characters whose goals stand for <paramref name="playedCharacterId"/>'s own.
    /// A borrowed fallback is not a slot character itself, so it stands for the missing characters.
    /// </summary>
    public IReadOnlyList<string> RepresentedCharacterIds(string playedCharacterId)
    {
        bool isBorrowedFallback = BorrowedCharacter != null
            && string.Equals(playedCharacterId, FallbackCharacterId, StringComparison.OrdinalIgnoreCase);
        return isBorrowedFallback ? MissingCharacterIds().ToArray() : new[] { playedCharacterId };
    }

    private IEnumerable<string> MissingCharacterIds() =>
        MissingCharacters
            .Select(character => character.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id));
}
