namespace StS2AP.Utils;

/// <summary>
/// Game features that only some supported game versions have. The APWorld cannot know which
/// game version a player runs, so a slot names the features its settings need in the
/// required_game_features slot data list, and a client build that lacks one refuses the slot
/// instead of playing it with that setting silently missing.
/// </summary>
internal static class GameFeatures
{
    /// <summary>The game's Dread mode, added in 0.113.0.</summary>
    public const string Dread = "dread";

    /// <summary>The Blitz merchant room, added with Blitz mode in 0.113.0.</summary>
    public const string BlitzMerchant = "blitz_merchant";

    /// <summary>The features of the game version this build was compiled against.</summary>
    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
#if STS2_AT_LEAST_0_113_0
        Dread,
        BlitzMerchant,
#endif
    };

    /// <summary>The required features <paramref name="supported"/> lacks, in the slot's order.</summary>
    public static IReadOnlyList<string> FindMissing(
        IEnumerable<string> required,
        IReadOnlySet<string> supported) =>
        required.Where(feature => !supported.Contains(feature)).Distinct().ToList();

    /// <summary>The connection error for a slot that needs <paramref name="missing"/>.</summary>
    public static string DescribeMissing(IReadOnlyList<string> missing) =>
        $"This AP slot needs game features this game version does not have: {string.Join(", ", missing)}. "
            + "Switch Slay the Spire 2 to its beta branch in Steam, or update the mod.";
}
