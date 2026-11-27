using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using StS2AP.Bingo.Detectors.Combat;
using StS2AP.Utils;

namespace StS2AP.Bingo.Detectors.Run;

/// <summary>Whether a streak square can still be done in the current act or run.</summary>
internal enum StreakState
{
    /// <summary>Still possible, done already, not a streak square, or no run in progress.</summary>
    Alive,

    /// <summary>Broken in the fight in progress; the next fight starts it over.</summary>
    BrokenThisFight,

    /// <summary>Broken in this act; the next act starts it over.</summary>
    BrokenThisAct,

    /// <summary>Broken in this run; the next run starts it over.</summary>
    BrokenThisRun,
}

/// <summary>
/// Streak squares are the ones a single slip ruins until the next act or run: "Reach a boss
/// taking the leftmost path all act", "Win without entering a shop". The board asks this class
/// whether such a square is still alive, so a player can see they need to try again later.
/// Fight streaks ("Win a combat without playing a Skill") are marked only while a fight they
/// count for is in progress.
/// </summary>
/// <remarks>
/// Everything is read from the run as it stands (map point history, visited map points, the
/// half-HP record), the same sources the squares' detectors judge from, so the answer survives
/// save-and-quit, follows checkpoint reloads and needs nothing saved. Squares whose condition
/// can be repaired later in the act or run (deck size, Curses, relic count) are not streaks.
/// </remarks>
internal static class BingoStreaks
{
    /// <summary>Act index of Act 2 (the game counts acts from 0).</summary>
    private const int Act2 = 1;

    // Judged at an act's boss: broken once the act shows the slip, or once its boss room was
    // entered without the square being done.
    private static readonly Dictionary<string, Func<RunState, Player, bool>> ActStreaks = new()
    {
        ["E21"] = (run, _) => RunHistory.CurrentAct(run).Count(entry => entry.MapPointType == MapPointType.Unknown) > 1,
        ["E22"] = (run, _) => !BossArrivalDetectors.EdgePath.Followed(run, leftmost: true),
        ["E23"] = (run, _) => !BossArrivalDetectors.EdgePath.Followed(run, leftmost: false),
        ["E25"] = (run, me) => RunHistory.CountRestChoices(RunHistory.CurrentAct(run), me, RestChoices.Heal) > 0,
        ["R-E07"] = (run, me) => AddedOtherThan(run, me, CardType.Attack),
        ["R-E08"] = (run, me) => AddedOtherThan(run, me, CardType.Skill),
        ["R-E09"] = (run, me) => HalfHpWatch.DroppedThisAct(run, me),
    };

    // Judged at the win: broken once the run shows the slip.
    private static readonly Dictionary<string, Func<RunState, Player, bool>> RunStreaks = new()
    {
        ["H17"] = (run, me) => RunHistory.CountRestChoices(RunHistory.WholeRun(run), me, RestChoices.Heal) > 0,
        ["H18"] = (run, _) => RunHistory.CountRooms(RunHistory.WholeRun(run), RoomType.Shop) > 0,
    };

    // Judged at the Act 2 boss: broken once the run so far shows the slip, or once that boss is
    // behind the player. Squares with no slip to watch for (deck size, upgrades) can be repaired
    // until then, so only the boss rule applies to them.
    private static readonly Dictionary<string, Func<RunState, Player, bool>?> Act2BossStreaks = new()
    {
        ["R-M03"] = (run, me) => RunHistory.StatsFor(RunHistory.WholeRun(run), me).Sum(stats => stats.GoldSpent) > 0,
        ["R-M04"] = (run, me) => RunHistory.CountRestChoices(RunHistory.WholeRun(run), me, RestChoices.Heal) > 0,
        ["R-M07"] = (run, me) => RunHistory.CardsAdded(RunHistory.WholeRun(run), me)
            .Count(card => card.Type != CardType.Curse) > 5,
        ["R-M08"] = (run, me) => RunHistory.StatsFor(RunHistory.WholeRun(run), me).Any(stats => stats.PotionUsed.Count > 0),
        ["M18"] = null,
        ["R-M06"] = null,
    };

    /// <summary>A square judged when a fight is won, and the fights it counts for.</summary>
    private readonly record struct FightStreak(Func<RoomType, bool> CountsFor, Func<CombatHistory, RunState, Player, bool> Slipped);

    private static bool AnyFight(RoomType room) => room is RoomType.Monster or RoomType.Elite or RoomType.Boss;
    private static bool EliteFight(RoomType room) => room == RoomType.Elite;
    private static bool BossFight(RoomType room) => room == RoomType.Boss;

    // Judged at a victory by CombatVictoryDetectors, from the same checks: broken once the fight
    // in progress shows the slip.
    private static readonly Dictionary<string, FightStreak> FightStreaks = new()
    {
        ["E02"] = new(AnyFight, (history, _, me) => CombatVictoryDetectors.PlayedCardOfTypeThisFight(history, me, CardType.Skill)),
        ["R-E11"] = new(AnyFight, (history, _, me) => CombatVictoryDetectors.GainedBlockThisFight(history, me)),
        // Damage is recorded per map point, so damage from an event before its fight counts too.
        ["R-M02"] = new(AnyFight, (_, run, me) => TookDamageHere(run, me)),
        ["M04"] = new(EliteFight, (_, run, me) => TookDamageHere(run, me)),
        ["F-M10"] = new(EliteFight, (history, _, me) => CombatVictoryDetectors.GainedBlockThisFight(history, me)),
        ["R-M01"] = new(EliteFight, (_, _, me) => CombatNotes.PlayedMoreThanTwoInATurn(me)),
        ["H05"] = new(BossFight, (_, run, me) => TookDamageHere(run, me)),
        ["F-E07"] = new(BossFight, (history, _, me) => CombatVictoryDetectors.UsedPotionThisFight(history, me)),
        ["F-H01"] = new(BossFight, (history, _, me) => CombatVictoryDetectors.PlayedCardOfTypeThisFight(history, me, CardType.Attack)),
        ["F-H02"] = new(BossFight, (history, _, me) => CombatVictoryDetectors.GainedBlockThisFight(history, me)),
    };

    private static readonly HashSet<string> LoggedFailures = new();

    /// <summary>The square's streak state in the run in progress; Alive when there is none.</summary>
    public static StreakState StateOf(BingoSquareInfo square)
    {
        if (!IsStreak(square.Key) || BingoState.IsDone(square))
            return StreakState.Alive;
        if (RunManager.Instance?.DebugOnlyGetState() is not RunState run
            || RunHistory.LocalPlayer(run) is not Player me)
        {
            return StreakState.Alive;
        }

        try
        {
            return Judge(square.Key, run, me);
        }
        catch (Exception ex)
        {
            // The board refreshes often; one log line per square is enough.
            if (LoggedFailures.Add(square.Key))
                LogUtility.Error($"[Bingo] Streak check for {square.Key} failed: {ex}");
            return StreakState.Alive;
        }
    }

    private static bool IsStreak(string key) =>
        ActStreaks.ContainsKey(key) || RunStreaks.ContainsKey(key) || Act2BossStreaks.ContainsKey(key)
        || FightStreaks.ContainsKey(key);

    private static StreakState Judge(string key, RunState run, Player me)
    {
        if (FightStreaks.TryGetValue(key, out FightStreak fight))
        {
            // The combat history holds only the fight in progress and is cleared once it is won.
            CombatManager combat = CombatManager.Instance;
            bool counts = combat.IsInProgress && run.CurrentRoom is { } room && fight.CountsFor(room.RoomType);
            return counts && fight.Slipped(combat.History, run, me) ? StreakState.BrokenThisFight : StreakState.Alive;
        }

        if (ActStreaks.TryGetValue(key, out var actSlip))
        {
            if (!BossReachedThisAct(run) && !actSlip(run, me))
                return StreakState.Alive;
            // In the run's last act there is no next act to try again in.
            return run.CurrentActIndex >= run.Acts.Count - 1 ? StreakState.BrokenThisRun : StreakState.BrokenThisAct;
        }

        if (RunStreaks.TryGetValue(key, out var runSlip))
            return runSlip(run, me) ? StreakState.BrokenThisRun : StreakState.Alive;

        if (Act2BossStreaks.TryGetValue(key, out var bossSlip))
        {
            bool act2BossBehind = run.CurrentActIndex > Act2
                || (run.CurrentActIndex == Act2 && BossReachedThisAct(run));
            return act2BossBehind || (bossSlip?.Invoke(run, me) ?? false)
                ? StreakState.BrokenThisRun
                : StreakState.Alive;
        }

        return StreakState.Alive;
    }

    /// <summary>
    /// Whether the current act's boss room was entered. Its squares are judged on entry, so one
    /// still open after that missed this act.
    /// </summary>
    private static bool BossReachedThisAct(RunState run) =>
        RunHistory.CountRooms(RunHistory.CurrentAct(run), RoomType.Boss) > 0;

    private static bool TookDamageHere(RunState run, Player me) =>
        CombatVictoryDetectors.DamageTakenHere(run, me) > 0;

    /// <summary>Whether a card of another type than <paramref name="wanted"/> was added this act.</summary>
    private static bool AddedOtherThan(RunState run, Player me, CardType wanted) =>
        BossArrivalDetectors.CardsAddedThisAct(RunHistory.CurrentAct(run), me).Any(type => type != wanted);
}
