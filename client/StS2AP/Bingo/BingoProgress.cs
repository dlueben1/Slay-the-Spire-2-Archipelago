using System.Text.Json;
using Godot;
using StS2AP.Persistence;
using StS2AP.UI;
using StS2AP.Utils;

namespace StS2AP.Bingo;

/// <summary>
/// The best value reached so far on each "reach a number" square ("Play 8 cards in one turn":
/// best 6 of 8), so the board can show how close the player has come. Kept per AP slot across
/// runs, in the slot's local folder as <c>bingo-progress.json</c>.
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

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static Dictionary<string, Entry> _entries = new();
    // The file the entries belong to; null until loaded for a connected slot.
    private static string? _loadedPath;
    private static bool _saveScheduled;

    /// <summary>The square's best so far, or null before any progress was recorded.</summary>
    public static Entry? For(string key)
    {
        EnsureLoaded();
        return _entries.TryGetValue(key, out Entry entry) ? entry : null;
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
