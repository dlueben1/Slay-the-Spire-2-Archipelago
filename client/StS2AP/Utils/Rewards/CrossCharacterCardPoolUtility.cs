using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace StS2AP.Utils;

/// <summary>
/// Builds the character card-pool set used by cross-character rewards.
///
/// Archipelago replaces <c>UnlockState.Characters</c> with the characters configured for
/// the slot. That is correct for character selection, but effects such as Prismatic Gem
/// should still see every built-in character. Installed modded characters remain opt-in:
/// they are included only when the reward owner's AP slot selected them.
/// </summary>
internal static class CrossCharacterCardPoolUtility
{
    // Preserve the base event's explicit color order. Keeping a fixed order also makes the
    // event's RNG consumption deterministic when the same mods are installed on each client.
    private static readonly string[] DefaultCharacterIds =
    [
        "NECROBINDER",
        "IRONCLAD",
        "REGENT",
        "SILENT",
        "DEFECT",
    ];

    /// <summary>
    /// Returns all five built-in pools followed by installed modded pools selected by the
    /// reward owner's AP slot. Returns <see langword="false"/> before slot settings are available so
    /// callers can fail open to the base-game behavior.
    /// </summary>
    public static bool TryGetPools(Player player, out IReadOnlyList<CardPoolModel> pools)
    {
        pools = Array.Empty<CardPoolModel>();
        if ((!MultiplayerSupport.IsRealMultiplayerRun && MultiplayerSupport.IsLocalGuest) ||
            !ApPlayerContextResolver.TryGetRewardSettings(player, out var settings))
            return false;

        try
        {
            var playableCharacters = ModelDb.AllCharacters
                .Where(character => character.IsPlayable)
                .ToList();
            var result = new List<CardPoolModel>();
            var seenPoolIds = new HashSet<ModelId>();

            foreach (var characterId in DefaultCharacterIds)
            {
                if (!AddCharacterPool(characterId, playableCharacters, result, seenPoolIds))
                {
                    LogUtility.Error(
                        $"Could not resolve the built-in character pool for {characterId}; " +
                        "using base-game pools."
                    );
                    return false;
                }
            }

            foreach (var characterId in settings.Characters.Values
                         .Where(config => config.ModNum > 0)
                         .Select(config => config.OfficialName)
                         .Where(characterId => !string.IsNullOrWhiteSpace(characterId))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(characterId => characterId, StringComparer.OrdinalIgnoreCase))
            {
                AddCharacterPool(characterId, playableCharacters, result, seenPoolIds);
            }

            pools = result;
            return result.Count > 0;
        }
        catch (Exception ex)
        {
            LogUtility.Error(
                $"Could not build the cross-character card pool; using base-game pools. {ex}"
            );
            return false;
        }
    }

    private static bool AddCharacterPool(
        string characterId,
        IEnumerable<CharacterModel> playableCharacters,
        ICollection<CardPoolModel> result,
        ISet<ModelId> seenPoolIds)
    {
        var character = playableCharacters.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Id.Entry,
                characterId,
                StringComparison.OrdinalIgnoreCase
            )
        );
        if (character is not null && seenPoolIds.Add(character.CardPool.Id))
        {
            result.Add(character.CardPool);
            return true;
        }

        return false;
    }
}
