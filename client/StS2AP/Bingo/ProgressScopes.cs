using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Bingo.Detectors.Characters;

namespace StS2AP.Bingo;

/// <summary>What a "reach a number" square's current value is counted over.</summary>
internal enum ProgressScope
{
    /// <summary>The highest value this turn ("Play 8 cards in one turn"); shown only in combat.</summary>
    Turn,

    /// <summary>The highest value this fight, counts and "at once" values alike; shown only in combat.</summary>
    Combat,

    /// <summary>The highest value in the room the player is in (one shop visit).</summary>
    Room,

    /// <summary>The highest value this act ("Visit 4 ? rooms in one act").</summary>
    Act,

    /// <summary>The highest value this run ("Defeat 7 elites in one run").</summary>
    Run,

    /// <summary>The latest value, which can go down (gold held, deck size, a streak).</summary>
    RunState,
}

/// <summary>Which turn, fight, room, act and run a value was reported in.</summary>
internal readonly record struct ProgressStamp(int Run, int Act, int Room, int Combat, int Turn);

/// <summary>
/// The span each progress square is counted over, so the board can show the value for the
/// current turn, fight, act or run rather than the best ever. Squares not listed here show only
/// their best.
/// </summary>
internal static class ProgressScopes
{
    private static readonly Dictionary<string, ProgressScope> ByKey = new()
    {
        // In one turn.
        ["E04"] = ProgressScope.Turn, ["M01"] = ProgressScope.Turn, ["F-E06"] = ProgressScope.Turn,
        ["F-M06"] = ProgressScope.Turn, ["F-M11"] = ProgressScope.Turn, ["F-H03"] = ProgressScope.Turn,
        ["H22"] = ProgressScope.Turn, ["C-DE-E1"] = ProgressScope.Turn, ["C-FO-M1"] = ProgressScope.Turn,
        ["C-FO-M2"] = ProgressScope.Turn, ["C-IR-E1"] = ProgressScope.Turn, ["C-IR-M6"] = ProgressScope.Turn,
        ["C-RE-E3"] = ProgressScope.Turn, ["C-RE-H2"] = ProgressScope.Turn, ["C-RE-H4"] = ProgressScope.Turn,
        ["C-RE-H5"] = ProgressScope.Turn, ["C-RE-M4"] = ProgressScope.Turn, ["C-SI-H3"] = ProgressScope.Turn,
        ["C-SI-M3"] = ProgressScope.Turn, ["C-SI-M4"] = ProgressScope.Turn, ["C-SI-M5"] = ProgressScope.Turn,

        // In one fight: counts, single hits and kills, and "at once" values that only exist in combat.
        ["E05"] = ProgressScope.Combat, ["E06"] = ProgressScope.Combat, ["E08"] = ProgressScope.Combat,
        ["E09"] = ProgressScope.Combat, ["E11"] = ProgressScope.Combat, ["E12"] = ProgressScope.Combat,
        ["M08"] = ProgressScope.Combat, ["M09"] = ProgressScope.Combat, ["H24"] = ProgressScope.Combat,
        ["F-E02"] = ProgressScope.Combat, ["F-E04"] = ProgressScope.Combat, ["F-M02"] = ProgressScope.Combat,
        ["F-M07"] = ProgressScope.Combat, ["F-M09"] = ProgressScope.Combat, ["W-M01"] = ProgressScope.Combat,
        ["C-DE-H1"] = ProgressScope.Combat, ["C-DE-H3"] = ProgressScope.Combat, ["C-DE-M1"] = ProgressScope.Combat,
        ["C-DE-M2"] = ProgressScope.Combat, ["C-DE-M3"] = ProgressScope.Combat, ["C-DE-M5"] = ProgressScope.Combat,
        ["C-DE-M6"] = ProgressScope.Combat, ["C-DE-M7"] = ProgressScope.Combat,
        ["C-FO-E1"] = ProgressScope.Combat, ["C-FO-E2"] = ProgressScope.Combat, ["C-FO-H1"] = ProgressScope.Combat,
        ["C-FO-H3"] = ProgressScope.Combat, ["C-FO-H4"] = ProgressScope.Combat, ["C-FO-M3"] = ProgressScope.Combat,
        ["C-FO-M4"] = ProgressScope.Combat,
        ["C-IR-E2"] = ProgressScope.Combat, ["C-IR-E3"] = ProgressScope.Combat, ["C-IR-H1"] = ProgressScope.Combat,
        ["C-IR-H3"] = ProgressScope.Combat, ["C-IR-H4"] = ProgressScope.Combat, ["C-IR-H5"] = ProgressScope.Combat,
        ["C-IR-M1"] = ProgressScope.Combat, ["C-IR-M2"] = ProgressScope.Combat, ["C-IR-M3"] = ProgressScope.Combat,
        ["C-IR-M4"] = ProgressScope.Combat, ["C-IR-M5"] = ProgressScope.Combat,
        ["C-NE-E1"] = ProgressScope.Combat, ["C-NE-E3"] = ProgressScope.Combat, ["C-NE-E4"] = ProgressScope.Combat,
        ["C-NE-H2"] = ProgressScope.Combat, ["C-NE-H3"] = ProgressScope.Combat, ["C-NE-H5"] = ProgressScope.Combat,
        ["C-NE-H6"] = ProgressScope.Combat, ["C-NE-H7"] = ProgressScope.Combat, ["C-NE-M1"] = ProgressScope.Combat,
        ["C-NE-M2"] = ProgressScope.Combat, ["C-NE-M3"] = ProgressScope.Combat, ["C-NE-M4"] = ProgressScope.Combat,
        ["C-RE-E1"] = ProgressScope.Combat, ["C-RE-E2"] = ProgressScope.Combat, ["C-RE-E4"] = ProgressScope.Combat,
        ["C-RE-H1"] = ProgressScope.Combat, ["C-RE-H6"] = ProgressScope.Combat, ["C-RE-M1"] = ProgressScope.Combat,
        ["C-RE-M3"] = ProgressScope.Combat, ["C-SI-E2"] = ProgressScope.Combat, ["C-SI-H2"] = ProgressScope.Combat,

        // In one shop visit.
        ["AP-E07"] = ProgressScope.Room, ["R-M05"] = ProgressScope.Room,

        // In one act.
        ["E20"] = ProgressScope.Act, ["E24"] = ProgressScope.Act, ["M20"] = ProgressScope.Act,
        ["F-M15"] = ProgressScope.Act, ["I-E06"] = ProgressScope.Act, ["R-E03"] = ProgressScope.Act,

        // In one run.
        ["M12"] = ProgressScope.Run, ["M19"] = ProgressScope.Run, ["M24"] = ProgressScope.Run,
        ["H10"] = ProgressScope.Run, ["H13"] = ProgressScope.Run, ["H14"] = ProgressScope.Run,
        ["I-H04"] = ProgressScope.Run, ["I-M04"] = ProgressScope.Run, ["I-M10"] = ProgressScope.Run,

        // What the run holds right now.
        ["E14"] = ProgressScope.RunState, ["M11"] = ProgressScope.RunState, ["H09"] = ProgressScope.RunState,
        ["E18"] = ProgressScope.RunState, ["E19"] = ProgressScope.RunState, ["M14"] = ProgressScope.RunState,
        ["H08"] = ProgressScope.RunState, ["M15"] = ProgressScope.RunState, ["M16"] = ProgressScope.RunState,
        ["M17"] = ProgressScope.RunState, ["M22"] = ProgressScope.RunState, ["I-E01"] = ProgressScope.RunState,
        ["I-M01"] = ProgressScope.RunState, ["AP-M05"] = ProgressScope.RunState, ["AP-H02"] = ProgressScope.RunState,
        ["R-M02"] = ProgressScope.RunState,
    };

    // Runs and rooms are told apart by object identity: a new run (or a reload from a checkpoint)
    // builds a new RunState, and every room entered is a new room object.
    private static readonly WeakReference<RunState?> LastRun = new(null);
    private static readonly WeakReference<object?> LastRoom = new(null);
    private static int _runSerial;
    private static int _roomSerial;
    private static int _combatSerial;
    private static int _turnSerial;

    static ProgressScopes()
    {
        TallyBoundaries.Register(TallyScope.Combat, () => _combatSerial++);
        TallyBoundaries.Register(TallyScope.Turn, () => _turnSerial++);
    }

    public static ProgressScope? Of(string key) => ByKey.TryGetValue(key, out ProgressScope scope) ? scope : null;

    /// <summary>Where play is now, or null outside a run.</summary>
    public static ProgressStamp? Now()
    {
        if (RunManager.Instance?.DebugOnlyGetState() is not RunState run)
            return null;
        if (!LastRun.TryGetTarget(out RunState? lastRun) || !ReferenceEquals(lastRun, run))
        {
            LastRun.SetTarget(run);
            _runSerial++;
        }
        object? room = run.CurrentRoom;
        if (!LastRoom.TryGetTarget(out object? lastRoom) || !ReferenceEquals(lastRoom, room))
        {
            LastRoom.SetTarget(room);
            _roomSerial++;
        }
        return new ProgressStamp(_runSerial, run.CurrentActIndex, _roomSerial, _combatSerial, _turnSerial);
    }

    /// <summary>Whether two stamps fall in the same turn, fight, room, act or run.</summary>
    public static bool SameSpan(ProgressScope scope, ProgressStamp a, ProgressStamp b) => scope switch
    {
        ProgressScope.Turn => a.Run == b.Run && a.Combat == b.Combat && a.Turn == b.Turn,
        ProgressScope.Combat => a.Run == b.Run && a.Combat == b.Combat,
        ProgressScope.Room => a.Run == b.Run && a.Room == b.Room,
        ProgressScope.Act => a.Run == b.Run && a.Act == b.Act,
        _ => a.Run == b.Run,
    };

    /// <summary>Whether a value stamped <paramref name="stamp"/> still describes the present.</summary>
    public static bool IsLive(ProgressScope scope, ProgressStamp stamp)
    {
        if (Now() is not { } now || !SameSpan(scope, stamp, now))
            return false;
        // Turn and fight values only mean something while that fight is on.
        return scope is not (ProgressScope.Turn or ProgressScope.Combat) || CombatManager.Instance.IsInProgress;
    }
}
