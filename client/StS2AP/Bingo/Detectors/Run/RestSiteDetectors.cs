using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

namespace StS2AP.Bingo.Detectors.Run;

/// <summary>
/// Rest site squares. Choices are read from the history's RestSiteChoices, which the game's
/// RestSiteSynchronizer fills after an option succeeds and right before it asks
/// <see cref="Hook.ShouldDisableRemainingRestSiteOptions"/>; that hook is the first moment a
/// choice is on record. Each room entry re-checks too, for choices made before the board
/// loaded. HP squares watch the option's own moment instead.
/// </summary>
internal static class RestSiteDetectors
{
    [HarmonyPatch(typeof(Hook), nameof(Hook.ShouldDisableRemainingRestSiteOptions))]
    private static class OptionChosen
    {
        [HarmonyPostfix]
        private static void Postfix(IRunState runState, Player player)
        {
            if (LocalContext.IsMe(player))
                RunSquare.Detect("rest site choice", () => CheckChoices(runState, player));
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeRoomEntered))]
    private static class RoomEntered
    {
        [HarmonyPrefix]
        private static void Prefix(IRunState runState) =>
            RunSquare.Detect("rest site choices", () =>
            {
                if (RunHistory.LocalPlayer(runState) is Player me)
                    CheckChoices(runState, me);
            });
    }

    /// <summary>
    /// I-E07: Rest chosen at full HP. The heal runs inside this method, so a prefix sees the HP
    /// before it. Heals that only mimic a rest (isMimicked, from cards and relics) are not the
    /// Rest option.
    /// </summary>
    [HarmonyPatch(typeof(HealRestSiteOption), nameof(HealRestSiteOption.ExecuteRestSiteHeal))]
    private static class RestHeal
    {
        [HarmonyPrefix]
        private static void Prefix(Player player, bool isMimicked)
        {
            if (!isMimicked)
                RunSquare.Detect("rest at full HP", () =>
                    RunSquare.CompleteIf("I-E07", player,
                        () => player.Creature.CurrentHp >= player.Creature.MaxHp));
        }
    }

    /// <summary>I-E08: Smith chosen at 25% HP or less (the hook runs once the card is upgraded).</summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterRestSiteSmith))]
    private static class Smith
    {
        [HarmonyPrefix]
        private static void Prefix(Player player) =>
            RunSquare.Detect("smith at low HP", () =>
                RunSquare.CompleteIf("I-E08", player,
                    () => player.Creature.CurrentHp * 4 <= player.Creature.MaxHp));
    }

    private static void CheckChoices(IRunState runState, Player player)
    {
        IReadOnlyList<MapPointHistoryEntry> act = RunHistory.CurrentAct(runState);
        IEnumerable<MapPointHistoryEntry> run = RunHistory.WholeRun(runState);

        // I-E06: Smith chosen twice in the current act.
        RunSquare.CompleteIf("I-E06", player,
            () => RunHistory.CountRestChoices(act, player, RestChoices.Smith) >= 2);
        // I-M10: Smith chosen five times in the run.
        RunSquare.CompleteIf("I-M10", player,
            () => RunHistory.CountRestChoices(run, player, RestChoices.Smith) >= 5);

        // I-H04: three different rest site options in the run, Rest and Smith included,
        // Mend and the mod's own campfire options not.
        RunSquare.CompleteIf("I-H04", player, () =>
            ChoicesInRun(run, player)
                .Where(choice => choice != RestChoices.Mend)
                .Distinct()
                .Count() >= 3);

        // M27: any rest site option other than Rest, Smith or Mend (relic options such as Lift,
        // Dig, Cook or Hatch, and Clone), never the mod's own campfire options.
        RunSquare.CompleteIf("M27", player, () =>
            ChoicesInRun(run, player).Any(choice =>
                choice is not (RestChoices.Heal or RestChoices.Smith or RestChoices.Mend)));
    }

    /// <summary>The game's own rest site options the player chose in the run, in order.</summary>
    private static IEnumerable<string> ChoicesInRun(IEnumerable<MapPointHistoryEntry> run, Player player) =>
        RunHistory.StatsFor(run, player)
            .SelectMany(stats => stats.RestSiteChoices)
            .Where(choice => !RestChoices.IsArchipelagoOption(choice));
}
