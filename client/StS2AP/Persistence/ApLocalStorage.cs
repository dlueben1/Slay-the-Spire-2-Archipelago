using Godot;
using MegaCrit.Sts2.Core.Saves;
using StS2AP.Utils;

namespace StS2AP.Persistence;

/// <summary>
/// Resolves the account's <c>mod_data/Archipelago</c> folder (beside apsettings.json) and moves
/// data from the old scattered layout into it the first time it is used. See
/// <see cref="ApStorageLayout"/> for what goes where.
/// </summary>
internal static class ApLocalStorage
{
    private static readonly object Gate = new();
    private static string? _migratedRoot;

    /// <summary>The storage root. The first use for an account migrates its old data.</summary>
    internal static string Root
    {
        get
        {
            string accountRoot = ResolveAccountRoot();
            string root = Path.Combine(accountRoot, "mod_data", ModEntry.ModId);
            lock (Gate)
            {
                if (_migratedRoot != root)
                {
                    ApStorageMigration.Run(
                        ProjectSettings.GlobalizePath("user://"),
                        accountRoot,
                        root,
                        LogUtility.Info,
                        LogUtility.Warn
                    );
                    _migratedRoot = root;
                }
            }
            return root;
        }
    }

    internal static string SlotDirectory(string seed, int team, int slot) =>
        ApStorageLayout.SlotDirectory(Root, seed, team, slot);

    /// <summary>Marks the slot as just played. A failure only costs the cleanup list a date.</summary>
    internal static void NoteSlotPlayed(string seed, int team, int slot, string? slotName, string? server)
    {
        try
        {
            ApStorageLayout.NoteSlot(SlotDirectory(seed, team, slot), seed, team, slot, slotName, server, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Could not record the AP slot in local storage: {ex.Message}");
        }
    }

    /// <summary>
    /// The account folder the game saves into, such as <c>steam/&lt;id&gt;</c>, resolved the way the
    /// game builds its own save store. Multiplayer test instances each get their own.
    /// </summary>
    private static string ResolveAccountRoot() =>
        ProjectSettings.GlobalizePath(UserDataPathProvider.GetAccountScopedBasePath(null)).TrimEnd('/', '\\');
}
