using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Models;

/// <summary>
/// Turns the slot's Run Modifiers and Boons options into run modifiers on each new AP run. Boons
/// with uses to track are relics instead, so the relic bar shows their counter and used-up state.
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

    /// <summary>
    /// A Boon is a run modifier, or a relic given to each player when it has uses to track.
    /// Singleplayer-only Boons hold state or choices that belong to one player, or change the
    /// shared map, so multiplayer runs leave them out.
    /// </summary>
    private sealed record Boon(
        Func<ModifierModel>? Modifier = null, Func<RelicModel>? Relic = null, bool SingleplayerOnly = false);

    // Keys are the APWorld's Boons option names.
    private static readonly IReadOnlyDictionary<string, Boon> BoonsByOptionName =
        new Dictionary<string, Boon>(StringComparer.Ordinal)
        {
            ["EmberEnchanting"] = new(Modifier: ModelDb.Modifier<ApEmberEnchanting>, SingleplayerOnly: true),
            ["CompoundInterest"] = new(Modifier: ModelDb.Modifier<ApCompoundInterest>),
            ["Gilded"] = new(Modifier: ModelDb.Modifier<ApGilded>),
            ["SpringCleaning"] = new(Modifier: ModelDb.Modifier<ApSpringCleaning>, SingleplayerOnly: true),
            ["LoyalCustomer"] = new(Modifier: ModelDb.Modifier<ApLoyalCustomer>),
            ["CampfireBrewing"] = new(Modifier: ModelDb.Modifier<ApCampfireBrewing>, SingleplayerOnly: true),
            ["EndlessUpgrades"] = new(Modifier: ModelDb.Modifier<ApEndlessUpgrades>),
            ["Wayfinder"] = new(Relic: ModelDb.Relic<ApWayfinder>, SingleplayerOnly: true),
            ["SecondWind"] = new(Relic: ModelDb.Relic<ApSecondWind>, SingleplayerOnly: true),
            ["InkBottle"] = new(Relic: ModelDb.Relic<ApInkBottle>),
            ["MedicalKit"] = new(Relic: ModelDb.Relic<ApMedicalKit>),
            ["BlueCandle"] = new(Relic: ModelDb.Relic<ApBlueCandle>),
            ["PreservedInsect"] = new(Relic: ModelDb.Relic<ApPreservedInsect>),
            ["Torii"] = new(Relic: ModelDb.Relic<ApTorii>),
            ["Turnip"] = new(Relic: ModelDb.Relic<ApTurnip>),
            ["Amphorae"] = new(Relic: ModelDb.Relic<ApAmphorae>),
            ["CursedKettle"] = new(Relic: ModelDb.Relic<ApCursedKettle>),
        };

    /// <summary>Whether this modifier came from the Run Modifiers or Boons option.</summary>
    public static bool IsArchipelagoModifier(ModifierModel modifier) =>
        modifier.GetType().Assembly == typeof(ApRunModifiers).Assembly;

    /// <summary>The image path of a base-game modifier's icon, by its model ID entry.</summary>
    public static string ModifierIconPath(string modifierEntry) =>
        ImageHelper.GetImagePath($"packed/modifiers/{modifierEntry.ToLowerInvariant()}.png");

    /// <summary>
    /// Adds the configured modifiers to a new standard or Dread run. Call this before the base game
    /// initializes the run's modifiers; in multiplayer, pass the host's settings so every peer
    /// builds the same run.
    /// </summary>
    public static void AddToNewRun(RunState runState, ArchipelagoSettings? settings)
    {
        // Log the skips: without them, a player's log cannot show why a run had no modifiers.
        if (settings == null)
        {
            LogUtility.Info("Skipped run modifiers: no AP slot settings for this run.");
            return;
        }
        // Run Modifiers stack with a Dread run's own rules, so Mounting Banes adds a second
        // Ascender's Bane in Acts 2 and 3.
        if (runState.GameMode != GameMode.Standard && !DreadRuns.IsDreadRun(runState))
        {
            LogUtility.Info($"Skipped run modifiers: game mode is {runState.GameMode}.");
            return;
        }

        var added = new List<ModifierModel>();
        foreach (string optionName in settings.RunModifiers)
        {
            if (CanonicalByOptionName.TryGetValue(optionName, out Func<ModifierModel>? canonical))
                added.Add(canonical().ToMutable());
            else
                LogUtility.Warn($"Ignoring unknown run modifier '{optionName}' from slot data.");
        }
        foreach (string boonName in settings.Boons)
        {
            if (!BoonsByOptionName.TryGetValue(boonName, out Boon? boon))
                LogUtility.Warn($"Ignoring unknown boon '{boonName}' from slot data.");
            else if (runState.Players.Count > 1 && boon.SingleplayerOnly)
                LogUtility.Info($"Skipped boon '{boonName}': it is singleplayer only.");
            else if (boon.Relic != null)
                GiveToEveryPlayer(runState, boon.Relic);
            else
                added.Add(boon.Modifier!().ToMutable());
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

    /// <summary>
    /// Adds a boon relic after each player's starting relics. The players exist with their
    /// starting relics at this point, before the run begins, so direct insertion is enough: there
    /// is no relic bar to update yet and no pickup to record in the run history.
    /// </summary>
    private static void GiveToEveryPlayer(RunState runState, Func<RelicModel> canonical)
    {
        foreach (Player player in runState.Players)
        {
            RelicModel relic = canonical();
            if (player.Relics.Any(owned => owned.Id == relic.Id))
                continue;
            RelicModel mutable = relic.ToMutable();
            mutable.FloorAddedToDeck = 1;
            player.AddRelicInternal(mutable, silent: true);
            LogUtility.Info($"Added boon relic {relic.Id.Entry}");
        }
    }
}
