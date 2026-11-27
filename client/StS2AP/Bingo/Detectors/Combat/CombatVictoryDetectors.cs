using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using static StS2AP.Bingo.Detectors.Combat.CombatDetectorSupport;

namespace StS2AP.Bingo.Detectors.Combat;

/// <summary>
/// Squares judged when a combat is won. Most read the game's own combat history
/// (<see cref="CombatManager.History"/>), which records every card play, Block gain and potion
/// use of the current combat and is cleared right after <see cref="Hook.AfterCombatEnd"/>. The
/// rest read the run's map point history, which the save file keeps, so a save-and-quit loses
/// nothing: reloading restarts the combat from its start anyway.
/// </summary>
/// <remarks>
/// <see cref="Hook.AfterCombatEnd"/> fires only on a victory (a loss ends the run through a
/// different path). A prefix runs before any relic or power reacts to the win, so HP is still
/// what the last blow left, before Burning Blood style heals.
/// </remarks>
internal static class CombatVictoryDetectors
{
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
    private static class RecordCombatStart
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState? combatState) =>
            Guard("combat start", () => CombatNotes.Begin(combatState));
    }

    /// <summary>
    /// In multiplayer a player who died during the fight is healed to 1 HP just before
    /// <see cref="Hook.AfterCombatEnd"/>. They did not win that combat, and without this they would
    /// finish the "exactly 1 HP" square for free.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.ReviveBeforeCombatEnd))]
    private static class RecordFallenPlayers
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance) =>
            Guard("fallen player", () =>
            {
                if (__instance.Creature.IsDead)
                    CombatNotes.MarkFallen(__instance);
            });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
    private static class JudgeVictory
    {
        [HarmonyPrefix]
        private static void Prefix(IRunState runState, ICombatState? combatState, CombatRoom room) =>
            Guard("combat victory", () => OnVictory(runState, combatState, room));
    }

    private static void OnVictory(IRunState runState, ICombatState? combatState, CombatRoom room)
    {
        if (!BingoSquares.IsActive || combatState == null)
            return;
        // Only the local player's squares count, so only they are judged.
        Player? me = LocalContext.GetMe(combatState);
        if (me == null || CombatNotes.HasFallen(me))
            return;

        Creature body = me.Creature;
        CombatHistory history = CombatManager.Instance.History;
        bool boss = room.RoomType == RoomType.Boss;
        bool elite = room.RoomType == RoomType.Elite;
        int turnNumber = me.PlayerCombatState?.TurnNumber ?? int.MaxValue;

        // Card plays include auto-plays (Havoc, Mayhem and the like): the game records them as
        // plays, and the user left the choice to whatever is simplest.
        bool PlayedCardOfType(CardType type) =>
            history.CardPlaysStarted.Any(e => e.CardPlay.Player == me && e.CardPlay.Card.Type == type);
        // Osty's summons are not Block; Frost orbs are (accepted).
        bool GainedBlock() =>
            history.Entries.OfType<BlockGainedEntry>().Any(e => e.Receiver == body && e.Amount > 0);
        bool UsedPotion() =>
            history.Entries.OfType<PotionUsedEntry>().Any(e => e.Actor == body);

        // E02: no Skill card play by this player in the combat history.
        CompleteIf("E02", me, () => !PlayedCardOfType(CardType.Skill));
        // E03: the player's first turn, and the win came on the player side (not from Thorns
        // during the enemies' first turn).
        CompleteIf("E03", me, () => turnNumber == 1 && combatState.CurrentSide == CombatSide.Player);
        // E07 / M05: HP at the moment of victory.
        CompleteIf("E07", me, () => body.CurrentHp <= 5);
        CompleteIf("M05", me, () => body.CurrentHp == 1);
        // R-E11: no Block gained by the player in the combat history.
        CompleteIf("R-E11", me, () => !GainedBlock());

        if (elite)
        {
            // M04: the map point's damage total, the game's own "Perfect" badge measure. It
            // counts unblocked damage to the player only (Osty's hits don't count) and includes
            // self-inflicted damage such as Bloodletting.
            CompleteIf("M04", me, () => DamageTakenHere(runState, me) == 0);
            CompleteIf("F-M10", me, () => !GainedBlock());
            // F-E08: TurnNumber counts this player's turns, extra turns included.
            CompleteIf("F-E08", me, () => turnNumber <= 4);
            // R-M01: card plays per turn are watched in CardPlayDetectors.
            CompleteIf("R-M01", me, () => !CombatNotes.PlayedMoreThanTwoInATurn(me));
        }

        if (boss)
        {
            CompleteIf("H05", me, () => DamageTakenHere(runState, me) == 0);
            CompleteIf("E26", me, () => body.CurrentHp * 2 >= body.MaxHp);
            // R-E10: HP recorded when the boss combat began.
            CompleteIf("R-E10", me, () => CombatNotes.StartedBelowPercent(me, 30));
            CompleteIf("F-E07", me, () => !UsedPotion());
            CompleteIf("F-H01", me, () => !PlayedCardOfType(CardType.Attack));
            CompleteIf("F-H02", me, () => !GainedBlock());
            // I-M09: every slot full at the win, before the combat reward offers a potion.
            CompleteIf("I-M09", me, () => me.MaxPotionCount > 0 && me.PotionSlots.All(p => p != null));
        }

        // R-M02: this combat and the two before it in the run, all without damage.
        ReachIfOpen("R-M02", me, () => CleanCombatStreak(runState, me), 3);
    }

    /// <summary>Damage the player took at the current map point, or null when it isn't recorded.</summary>
    private static int? DamageTakenHere(IRunState runState, Player player) =>
        runState.CurrentMapPointHistoryEntry?.PlayerStats
            .FirstOrDefault(e => e.PlayerId == player.NetId)?.DamageTaken;

    /// <summary>
    /// Counts the run's most recent combats, newest first, that the player won without taking
    /// damage, stopping at the first one where they took some. Reads the run's map point history,
    /// which survives save-and-quit. Rooms without a fight in between don't break the streak.
    /// </summary>
    private static int CleanCombatStreak(IRunState runState, Player player)
    {
        int streak = 0;
        for (int act = runState.MapPointHistory.Count - 1; act >= 0; act--)
        {
            var points = runState.MapPointHistory[act];
            for (int i = points.Count - 1; i >= 0; i--)
            {
                var point = points[i];
                bool hadCombat = point.HasRoomOfType(RoomType.Monster)
                    || point.HasRoomOfType(RoomType.Elite)
                    || point.HasRoomOfType(RoomType.Boss);
                if (!hadCombat)
                    continue;
                // Damage is recorded per map point, so damage from an event before its fight
                // counts against that fight.
                int? damage = point.PlayerStats.FirstOrDefault(e => e.PlayerId == player.NetId)?.DamageTaken;
                if (damage != 0)
                    return streak;
                streak++;
                if (streak >= 3)
                    return streak;
            }
        }
        return streak;
    }
}

/// <summary>
/// What the combat squares need to remember about the current combat that the game's combat
/// history doesn't hold. Reset when a combat begins; a reload restarts the combat, so nothing
/// is lost by keeping it in memory.
/// </summary>
internal static class CombatNotes
{
    private readonly record struct Health(int Current, int Max);

    private static readonly Dictionary<ulong, Health> HealthAtStart = new();
    private static readonly HashSet<ulong> Fallen = new();
    private static readonly HashSet<ulong> OverTwoCardsInATurn = new();

    public static void Begin(ICombatState? combatState)
    {
        HealthAtStart.Clear();
        Fallen.Clear();
        OverTwoCardsInATurn.Clear();
        if (combatState == null)
            return;
        foreach (Player player in combatState.Players)
            HealthAtStart[player.NetId] = new Health(player.Creature.CurrentHp, player.Creature.MaxHp);
    }

    public static void MarkFallen(Player player) => Fallen.Add(player.NetId);

    public static bool HasFallen(Player player) => Fallen.Contains(player.NetId);

    public static void MarkPlayedMoreThanTwoInATurn(Player player) => OverTwoCardsInATurn.Add(player.NetId);

    public static bool PlayedMoreThanTwoInATurn(Player player) => OverTwoCardsInATurn.Contains(player.NetId);

    /// <summary>True when the player began this combat with HP below the given percentage of Max HP.</summary>
    public static bool StartedBelowPercent(Player player, int percent) =>
        HealthAtStart.TryGetValue(player.NetId, out Health health)
        && health.Current * 100 < health.Max * percent;
}
