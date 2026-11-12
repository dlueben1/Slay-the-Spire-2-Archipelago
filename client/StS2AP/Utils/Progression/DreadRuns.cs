using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
#if STS2_AT_LEAST_0_113_0
using MegaCrit.Sts2.Core.Models.Dread;
#endif
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Utils;

/// <summary>
/// The slot's Dread Mode option. A Dread AP run starts from the standard character select, so
/// character locks, checkpoints and the AP panel stay as they are; only the run's game mode
/// changes. The game then adds its own Dread rules: a Dominion in each act, stronger bosses and
/// an Ascender's Bane on entering Acts 2 and 3. Ascension levels still come from the slot's
/// Ascension option and Ascension Down items, not from Dread's forced Ascension 10.
/// Game versions before 0.113 have no Dread; there every member answers "no".
/// </summary>
internal static class DreadRuns
{
    /// <summary>
    /// Whether this slot's new singleplayer runs are Dread runs. Multiplayer runs stay standard:
    /// a lobby shares one game mode, and the Dread lobby screen has no AP support yet.
    /// </summary>
    public static bool IsRequestedForSingleplayer =>
#if STS2_AT_LEAST_0_113_0
        ArchipelagoClient.Settings?.DreadMode == true;
#else
        false;
#endif

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
    /// The game mode line and Dread rules for the portrait tooltip of a Dread run, or null.
    /// The game's first rule, "All Ascension modifiers are enabled", is left out because an AP
    /// slot sets its own Ascension levels.
    /// </summary>
    public static string? DescribeRules(IRunState? runState)
    {
#if STS2_AT_LEAST_0_113_0
        if (!IsDreadRun(runState))
            return null;

        var modeLine = new LocString("ascension", "PORTRAIT_GAME_MODE");
        modeLine.Add("gameMode", new LocString("ascension", "PORTRAIT_GAME_MODE.dread"));
        IEnumerable<string> rules = new[] { "DREAD_RULE_02", "DREAD_RULE_03", "DREAD_RULE_04" }
            // Each rule's text starts with its number in the game's own list.
            .Select(key => Regex.Replace(new LocString("ascension", key).GetFormattedText(), @"^\s*\d+\.\s*", ""))
            .Select(rule => "- " + rule);
        return modeLine.GetFormattedText() + "\n" + string.Join("\n", rules);
#else
        return null;
#endif
    }
}
