using System.Text.Json;
using Godot;
using StS2AP.Persistence;
using StS2AP.UI;
using StS2AP.Utils;

namespace StS2AP.Bingo;

/// <summary>
/// Progress on each "reach a number" square ("Play 8 cards in one turn": 6 of 8). The board shows
/// the value for the square's current turn, fight, act or run (see <see cref="ProgressScopes"/>),
/// and otherwise the best value reached so far. Bests are kept per AP slot across runs, in the
/// slot's local folder as <c>bingo-progress.json</c>; current values are kept only in memory.
/// </summary>
/// <remarks>
/// Only this machine's play counts: another client on the same slot keeps its own bests. A best
/// is never lowered, so a checkpoint reload does not take it back; it is how close the player
/// came, not a record of the run. Writes are batched, since a best can rise many times in one
/// turn (damage dealt, cards played).
/// </remarks>
internal static class BingoProgress
{
    private const string FileName = "bingo-progress.json";
    private const double SaveDelaySeconds = 1.0;

    /// <summary>One square's best so far and the number it needs.</summary>
    internal readonly record struct Entry(int Best, int Target);

    /// <summary>What a square shows: the current value when <paramref name="Live"/>, else its best.</summary>
    internal readonly record struct Shown(int Value, int Target, bool Live);

    private readonly record struct Current(int Value, int Target, ProgressStamp Stamp);

    private static readonly Dictionary<string, Current> CurrentValues = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static Dictionary<string, Entry> _entries = new();
    // The file the entries belong to; null until loaded for a connected slot.
    private static string? _loadedPath;
    private static bool _saveScheduled;

    /// <summary>
    /// What the square shows: its value for the turn, fight, act or run in progress, or else its
    /// best so far. Null when there is neither.
    /// </summary>
    public static Shown? For(string key)
    {
        EnsureLoaded();
        if (CurrentValues.TryGetValue(key, out Current current)
            && ProgressScopes.Of(key) is { } scope
            && ProgressScopes.IsLive(scope, current.Stamp))
        {
            return new Shown(current.Value, current.Target, Live: true);
        }
        return _entries.TryGetValue(key, out Entry entry) ? new Shown(entry.Best, entry.Target, Live: false) : null;
    }

    /// <summary>
    /// Records the square's value in its current span: the highest this turn, fight, act or run,
    /// or the latest for values the run holds (gold, deck size), which can go down.
    /// </summary>
    public static void RecordCurrent(string key, int value, int target)
    {
        if (ProgressScopes.Of(key) is not { } scope || ProgressScopes.Now() is not { } now)
            return;
        bool sameSpan = CurrentValues.TryGetValue(key, out Current previous)
            && previous.Target == target
            && ProgressScopes.SameSpan(scope, previous.Stamp, now);
        if (sameSpan && (scope == ProgressScope.RunState ? previous.Value == value : previous.Value >= value))
            return;
        CurrentValues[key] = new Current(value, target, now);
        BingoBoardUI.RefreshSoon();
    }

    /// <summary>Raises the square's best to <paramref name="value"/> when that is higher.</summary>
    public static void Record(string key, int value, int target)
    {
        if (value <= 0 || !EnsureLoaded())
            return;
        if (_entries.TryGetValue(key, out Entry entry) && entry.Best >= value && entry.Target == target)
            return;

        // A changed target (the square's number was retuned) starts its best over.
        int best = entry.Target == target ? Math.Max(value, entry.Best) : value;
        _entries[key] = new Entry(best, target);
        ScheduleSave();
        BingoBoardUI.Refresh();
    }

    /// <summary>Called on slot changes: saves what is pending and forgets the old slot's bests.</summary>
    internal static void ResetSlotState()
    {
        SaveNow();
        _entries = new Dictionary<string, Entry>();
        CurrentValues.Clear();
        _loadedPath = null;
    }

    /// <summary>Loads the connected slot's file the first time it is needed. False when no slot is connected.</summary>
    private static bool EnsureLoaded()
    {
        string? path = CurrentPath();
        if (path == null)
            return false;
        if (path == _loadedPath)
            return true;

        SaveNow();
        _loadedPath = path;
        _entries = new Dictionary<string, Entry>();
        try
        {
            if (File.Exists(path))
                _entries = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"[Bingo] Could not read saved square progress, starting over: {ex.Message}");
        }
        return true;
    }

    private static string? CurrentPath()
    {
        if (ArchipelagoClient.Session?.ConnectionInfo is not { } info || string.IsNullOrWhiteSpace(ArchipelagoClient.Seed))
            return null;
        try
        {
            return Path.Combine(ApLocalStorage.SlotDirectory(ArchipelagoClient.Seed, info.Team, info.Slot), FileName);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static void ScheduleSave()
    {
        if (_saveScheduled)
            return;
        _saveScheduled = true;
        if (Engine.GetMainLoop() is SceneTree tree)
            tree.CreateTimer(SaveDelaySeconds).Timeout += SaveNow;
        else
            SaveNow();
    }

    private static void SaveNow()
    {
        if (!_saveScheduled || _loadedPath == null)
            return;
        _saveScheduled = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_loadedPath)!);
            string temporary = _loadedPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_entries, JsonOptions));
            File.Move(temporary, _loadedPath, overwrite: true);
        }
        catch (Exception ex)
        {
            // Only the progress display is lost; the squares themselves are checks on the server.
            LogUtility.Warn($"[Bingo] Could not save square progress: {ex.Message}");
        }
    }
}
