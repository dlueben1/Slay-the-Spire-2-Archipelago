using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
#if STS2_AT_LEAST_0_113_0
using MegaCrit.Sts2.Core.Models.Dread;
#endif
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Models;

namespace StS2AP.Utils;

/// <summary>
/// The slot's Dread Mode option. A Dread AP run starts from the standard
/// character select or lobby, so character locks, checkpoints and the AP panel stay as they are;
/// only the run's game mode changes. The game then adds its own Dread rules: a Dominion in each
/// act, stronger bosses and an Ascender's Bane on entering Acts 2 and 3. The APWorld turns every
/// Ascension on for a Dread slot and gives it no Ascension Down items, as the game's Dread screen
/// always plays Ascension 10.
/// Game versions before 0.113 have no Dread; there every member answers "no".
/// </summary>
internal static class DreadRuns
{
    /// <summary>
    /// Whether this slot has Dread Mode on. Its singleplayer runs are Dread runs, and so are the
    /// multiplayer runs it hosts.
    /// </summary>
    public static bool IsSlotSetting =>
#if STS2_AT_LEAST_0_113_0
        ArchipelagoClient.Settings?.DreadMode == true;
#else
        false;
#endif

#if STS2_AT_LEAST_0_113_0
    private static readonly MethodInfo? GameModeSetter =
        AccessTools.PropertySetter(typeof(RunState), nameof(RunState.GameMode));
    private static readonly MethodInfo? RunModelsSetter =
        AccessTools.PropertySetter(typeof(RunState), nameof(RunState.RunModels));
#endif

    /// <summary>
    /// Turns a new multiplayer run into a Dread run when the host has Dread Mode on. The AP
    /// lobby is a standard one, and guests first learn the host's settings from the run payload,
    /// after the game has created the run as a standard run. So every peer makes the same
    /// change here, before the game initializes the run: the mode, and the Dread run model the
    /// game would have added on creation. Nothing the game builds in between depends on
    /// the difference between a standard and a Dread run.
    /// </summary>
    public static void ApplyHostSetting(RunState runState, ArchipelagoSettings? hostSettings)
    {
#if STS2_AT_LEAST_0_113_0
        if (hostSettings?.DreadMode != true || runState.GameMode != GameMode.Standard)
            return;
        if (GameModeSetter == null || RunModelsSetter == null)
        {
            LogUtility.Error("Cannot start the host's Dread run: the run's mode cannot be set on this game version.");
            return;
        }

        var runModels = runState.RunModels.ToList();
        runModels.Add((RunModel)ModelDb.RunModel<DreadRunModel>().MutableClone());
        GameModeSetter.Invoke(runState, [GameMode.Dread]);
        RunModelsSetter.Invoke(runState, [runModels]);
        LogUtility.Info("Starting the AP multiplayer run as a Dread run: the host has Dread Mode on.");
#endif
    }

    public static bool IsDreadRun(IRunState? runState) =>
#if STS2_AT_LEAST_0_113_0
        runState?.GameMode == GameMode.Dread;
#else
        false;
#endif

    /// <summary>
    /// The boss icon's hover tips for the act's Dominion, as the game shows them in a Dread run,
    /// or null when the run has none to show. The game draws them itself except where the mod
    /// replaces the boss icon's tooltip.
    /// </summary>
    public static IReadOnlyList<IHoverTip>? DescribeDominion(IRunState? runState)
    {
#if STS2_AT_LEAST_0_113_0
        if (runState == null || !IsDreadRun(runState))
            return null;

        EncounterModel boss = runState.Act.BossEncounter;
        // The game takes the Dominion from the second boss when the act has one.
        if (runState.Act.SecondBossEncounter is { } secondBoss
            && runState.Dominion != null
            && secondBoss.GetDominion()?.Id == runState.Dominion.Id)
        {
            boss = secondBoss;
        }
        DominionModel? dominion = runState.Dominion ?? boss.GetDominion();
        if (dominion == null)
            return null;

        var title = new LocString("ascension", "DREAD.title");
        title.Add("BossName", boss.Title);
        var tips = new List<IHoverTip> { new HoverTip(title, dominion.Description) };
        tips.AddRange(dominion.ExtraHoverTips);
        return tips;
#else
        return null;
#endif
    }

    /// <summary>
    /// The game mode line and the game's Dread rules for the portrait tooltip of a Dread run, or
    /// null.
    /// </summary>
    public static string? DescribeRules(IRunState? runState)
    {
#if STS2_AT_LEAST_0_113_0
        if (!IsDreadRun(runState))
            return null;

        var modeLine = new LocString("ascension", "PORTRAIT_GAME_MODE");
        modeLine.Add("gameMode", new LocString("ascension", "PORTRAIT_GAME_MODE.dread"));
        IEnumerable<string> rules = new[] { "DREAD_RULE_01", "DREAD_RULE_02", "DREAD_RULE_03", "DREAD_RULE_04" }
            .Select(key => new LocString("ascension", key).GetFormattedText());
        return modeLine.GetFormattedText() + "\n" + string.Join("\n", rules);
#else
        return null;
#endif
    }
}
