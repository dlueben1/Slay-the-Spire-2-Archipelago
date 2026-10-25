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
        if (settings.EnemyHpPercent > 100)
        {
            var enemyHp = (ApEnemyHp)ModelDb.Modifier<ApEnemyHp>().ToMutable();
            enemyHp.HpPercent = settings.EnemyHpPercent;
            added.Add(enemyHp);
        }
        if (settings.EnemyDamagePercent > 100)
        {
            var enemyDamage = (ApEnemyDamage)ModelDb.Modifier<ApEnemyDamage>().ToMutable();
            enemyDamage.DamagePercent = settings.EnemyDamagePercent;
            added.Add(enemyDamage);
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
