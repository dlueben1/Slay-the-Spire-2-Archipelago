namespace StS2AP.Utils;

/// <summary>
/// Game features that only some supported game versions have. The APWorld cannot know which
/// game version a player runs, so a slot names the features its settings need in the
/// required_game_features slot data list. A client build that lacks one still connects: those
/// settings change no checks, so the slot plays without them, and the player is told which.
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

    // What the player loses, by feature. A feature from a newer APWorld is shown by its name.
    private static readonly IReadOnlyDictionary<string, string> EffectWithout =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Dread] = "Dread Mode (runs are standard runs)",
            [BlitzMerchant] = "Blitz merchants (every shop is a normal shop)",
        };

    /// <summary>The required features <paramref name="supported"/> lacks, in the slot's order.</summary>
    public static IReadOnlyList<string> FindMissing(
        IEnumerable<string> required,
        IReadOnlySet<string> supported) =>
        required.Where(feature => !supported.Contains(feature)).Distinct().ToList();

    /// <summary>What to tell a player whose slot uses <paramref name="missing"/>.</summary>
    public static string DescribeMissing(IReadOnlyList<string> missing) =>
        "This game version does not have: "
            + string.Join(", ", missing.Select(feature => EffectWithout.GetValueOrDefault(feature, feature)))
            + ". The slot plays without them here. The game's beta branch in Steam has them.";
}
