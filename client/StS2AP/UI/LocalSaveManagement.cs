using Godot;
using STS2RitsuLib.Settings;
using StS2AP.Persistence;
using StS2AP.UI.RunGraph;
using StS2AP.Utils;

namespace StS2AP.UI;

/// <summary>
/// The settings panel's local data buttons. Everything for one AP slot sits in that slot's
/// folder, so forgetting a seed deletes its checkpoints, multiplayer campaigns, run graphs and
/// unsent checks together. The connected slot is never forgotten.
/// </summary>
internal static class LocalSaveManagement
{
    internal const int UnplayedDays = 30;
    private const int ListedSlots = 8;

    internal static bool CanForget() =>
        !GameUtility.IsInRun && !MultiplayerSupport.IsMultiplayerScope;

    internal static void OpenFolder()
    {
        try
        {
            ApRunGraphFiles.ShowInFileManager(ApLocalStorage.Root);
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Could not open the local AP data folder: {ex}");
            NotificationUtility.ShowRawText("Could not open the local AP data folder. Check the log.");
        }
    }

    /// <param name="onlyUnplayed">Only seeds not played for <see cref="UnplayedDays"/> days; otherwise every seed but the connected one.</param>
    internal static void ShowForgetConfirmation(bool onlyUnplayed, Action requestRefresh)
    {
        if (!CanForget())
        {
            NotificationUtility.ShowRawText("Return to the main menu before forgetting local AP seeds.");
            return;
        }

        IReadOnlyList<ApStorageLayout.SlotSummary> slots;
        try
        {
            slots = SelectSlots(onlyUnplayed);
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Could not list local AP seeds: {ex}");
            NotificationUtility.ShowRawText("Could not list local AP seeds. Check the log.");
            return;
        }
        if (slots.Count == 0)
        {
            NotificationUtility.ShowRawText(onlyUnplayed
                ? $"No AP seed on this computer has gone unplayed for {UnplayedDays} days."
                : "There are no other AP seeds on this computer.");
            return;
        }

        // RitsuLib's dialog draws on its own layer above the settings screen. The game's popup
        // would open behind it, on the main menu, and only show once settings closed.
        ModSettingsUiFactory.ShowStyledConfirm(
            ((SceneTree)Engine.GetMainLoop()).Root,
            onlyUnplayed ? $"Forget AP Seeds Not Played in {UnplayedDays} Days?" : "Forget All Other AP Seeds?",
            DescribeForget(slots),
            cancelText: "Cancel",
            confirmText: "Forget Permanently",
            confirmIsDanger: true,
            onConfirm: () => ForgetConfirmed(slots, requestRefresh));
    }

    private static IReadOnlyList<ApStorageLayout.SlotSummary> SelectSlots(bool onlyUnplayed)
    {
        string? connected = ArchipelagoClient.IsConnected && ArchipelagoClient.Session is { } session
            ? ApLocalStorage.SlotDirectory(session.RoomState.Seed, session.ConnectionInfo.Team, session.ConnectionInfo.Slot)
            : null;
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-UnplayedDays);
        return ApStorageLayout.ListSlots(ApLocalStorage.Root)
            .Where(slot => slot.Directory != connected)
            .Where(slot => !onlyUnplayed || slot.LastActivityUtc < cutoff)
            .ToArray();
    }

    private static string DescribeForget(IReadOnlyList<ApStorageLayout.SlotSummary> slots)
    {
        var lines = slots.Take(ListedSlots).Select(slot =>
        {
            string line = $"{slot.DisplayName}: last played {slot.LastActivityUtc.ToLocalTime():MMM d yyyy}, {FormatSize(slot.Bytes)}";
            return slot.UnsentChecks > 0
                ? $"{line}, {slot.UnsentChecks} UNSENT {(slot.UnsentChecks == 1 ? "CHECK" : "CHECKS")}"
                : line;
        }).ToList();
        if (slots.Count > ListedSlots)
            lines.Add($"and {slots.Count - ListedSlots} more");
        return string.Join("\n", lines)
            + $"\n\nThis deletes their local checkpoints, multiplayer campaigns, run graphs and unsent checks "
            + $"({FormatSize(slots.Sum(slot => slot.Bytes))} in all). It cannot be undone. "
            + "AP server cloud saves are kept.";
    }

    private static void ForgetConfirmed(IReadOnlyList<ApStorageLayout.SlotSummary> slots, Action requestRefresh)
    {
        if (!CanForget())
        {
            NotificationUtility.ShowRawText("Local AP seeds were kept because the game left the main menu.");
            return;
        }

        int forgotten = 0;
        long bytes = 0;
        try
        {
            foreach (var slot in slots)
            {
                Forget(slot);
                forgotten++;
                bytes += slot.Bytes;
            }
            NotificationUtility.ShowRawText(
                $"Forgot {forgotten} AP {(forgotten == 1 ? "seed" : "seeds")} ({FormatSize(bytes)}).");
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Could not forget every selected local AP seed: {ex}");
            NotificationUtility.ShowRawText(
                $"Forgot {forgotten} of {slots.Count} AP seeds, then failed. Check the log before trying again.");
        }
        finally
        {
            requestRefresh();
        }
    }

    private static void Forget(ApStorageLayout.SlotSummary slot)
    {
        // A multiplayer save of this slot would otherwise be imported back as a campaign.
        if (slot.Info is { Seed.Length: > 0, Team: int team, Slot: int apSlot } info)
        {
            bool deletedActiveSave = ApMultiplayerCampaignStore.ReleaseSlot(info.Seed, team, apSlot);
            if (deletedActiveSave)
                LogUtility.Info($"Deleted the current multiplayer save of AP seed {info.Seed}.");
        }
        if (Directory.Exists(slot.Directory))
            Directory.Delete(slot.Directory, recursive: true);
        LogUtility.Info($"Forgot local AP data for {slot.DisplayName}: {slot.Directory}");
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{Math.Max(1, bytes / 1024)} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };
}
