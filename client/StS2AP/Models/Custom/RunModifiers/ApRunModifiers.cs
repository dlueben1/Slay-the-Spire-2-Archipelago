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
            ["EliteStrength"] = ModelDb.Modifier<ApEliteStrength>,
            ["RandomEnemyBuffs"] = ModelDb.Modifier<ApRandomEnemyBuffs>,
            ["WeakerRests"] = ModelDb.Modifier<ApWeakerRests>,
            ["SlowStart"] = ModelDb.Modifier<ApSlowStart>,
            ["FrailStart"] = ModelDb.Modifier<ApFrailStart>,
            ["ExtraAscendersBane"] = ModelDb.Modifier<ApExtraAscendersBane>,
            ["CursedRun"] = ModelDb.Modifier<ApCursedRun>,
            ["DeadlyEvents"] = ModelDb.Modifier<ApDeadlyEvents>,
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
        if (settings == null
            || settings.RunModifiers.Count == 0
            || runState.GameMode != GameMode.Standard)
        {
            return;
        }

        var added = new List<ModifierModel>();
        foreach (string optionName in settings.RunModifiers)
        {
            if (!CanonicalByOptionName.TryGetValue(optionName, out Func<ModifierModel>? canonical))
            {
                LogUtility.Warn($"Ignoring unknown run modifier '{optionName}' from slot data.");
                continue;
            }
            ModifierModel modifier = canonical();
            if (runState.Modifiers.Concat(added).Any(existing => existing.GetType() == modifier.GetType()))
                continue;
            added.Add(modifier.ToMutable());
        }
        if (added.Count == 0)
            return;

        runState.Modifiers = runState.Modifiers.Concat(added).ToList();
        LogUtility.Info(
            $"Added run modifiers: {string.Join(", ", added.Select(modifier => modifier.Id.Entry))}"
        );
    }
}
