using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Models;

/// <summary>
/// Turns the slot's Run Modifiers option into run modifiers on each new AP run.
/// </summary>
public static class ApRunModifiers
{
    // Keys are the APWorld's RunModifiers option names.
    private static readonly IReadOnlyDictionary<string, Func<ModifierModel>> CanonicalByOptionName =
        new Dictionary<string, Func<ModifierModel>>(StringComparer.Ordinal)
        {
            ["RisingStrength"] = ModelDb.Modifier<ApRisingStrength>,
            ["EscalatingBuffs"] = ModelDb.Modifier<ApEscalatingBuffs>,
            ["ShrinkingRests"] = ModelDb.Modifier<ApShrinkingRests>,
            ["MountingCurses"] = ModelDb.Modifier<ApMountingCurses>,
            ["MountingBanes"] = ModelDb.Modifier<ApMountingBanes>,
            ["CursedRun"] = ModelDb.Modifier<ApCursedRun>,
            ["ExtraAscendersBane"] = ModelDb.Modifier<ApExtraAscendersBane>,
            ["SlowStart"] = ModelDb.Modifier<ApSlowStart>,
            ["FrailStart"] = ModelDb.Modifier<ApFrailStart>,
            ["DangerousUnknowns"] = ModelDb.Modifier<ApDangerousUnknowns>,
        };

    /// <summary>Whether this modifier came from the Run Modifiers option.</summary>
    public static bool IsArchipelagoModifier(ModifierModel modifier) =>
        modifier.GetType().Assembly == typeof(ApRunModifiers).Assembly;

    /// <summary>The image path of a base-game modifier's icon, by its model ID entry.</summary>
    public static string ModifierIconPath(string modifierEntry) =>
        ImageHelper.GetImagePath($"packed/modifiers/{modifierEntry.ToLowerInvariant()}.png");

    /// <summary>
    /// Adds the configured modifiers to a new standard run. Call this before the base game
    /// initializes the run's modifiers; in multiplayer, pass the host's settings so every peer
    /// builds the same run.
    /// </summary>
    public static void AddToNewRun(RunState runState, ArchipelagoSettings? settings)
    {
        if (settings == null || runState.GameMode != GameMode.Standard)
            return;

        var added = new List<ModifierModel>();
        foreach (string optionName in settings.RunModifiers)
        {
            if (CanonicalByOptionName.TryGetValue(optionName, out Func<ModifierModel>? canonical))
                added.Add(canonical().ToMutable());
            else
                LogUtility.Warn($"Ignoring unknown run modifier '{optionName}' from slot data.");
        }
        if (settings.EnemyHpScaling > 0)
        {
            var hpScaling = (ApEnemyHpScaling)ModelDb.Modifier<ApEnemyHpScaling>().ToMutable();
            hpScaling.Percent = settings.EnemyHpScaling;
            added.Add(hpScaling);
        }
        if (settings.EnemyDamageScaling > 0)
        {
            var damageScaling = (ApEnemyDamageScaling)ModelDb.Modifier<ApEnemyDamageScaling>().ToMutable();
            damageScaling.Percent = settings.EnemyDamageScaling;
            added.Add(damageScaling);
        }

        // Keep one of each type, including any the run already has.
        added = added
            .Where(modifier => runState.Modifiers.All(existing => existing.GetType() != modifier.GetType()))
            .DistinctBy(modifier => modifier.GetType())
            .ToList();
        if (added.Count == 0)
            return;

        runState.Modifiers = runState.Modifiers.Concat(added).ToList();
        LogUtility.Info(
            $"Added run modifiers: {string.Join(", ", added.Select(modifier => modifier.Id.Entry))}"
        );
    }
}
