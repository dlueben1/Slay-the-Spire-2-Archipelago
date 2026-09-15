using MegaCrit.Sts2.Core.Saves;
using StS2AP.Utils;

namespace StS2AP.UI;

internal static class LocalSaveManagement
{
    internal static bool CanDelete() =>
        !GameUtility.IsInRun && !MultiplayerSupport.IsMultiplayerScope;

    internal static void ShowDeleteConfirmation(bool multiplayer, Action requestRefresh)
    {
        if (!CanDelete())
        {
            NotificationUtility.ShowRawText("Return to the main menu before deleting local AP saves.");
            return;
        }

        string key = multiplayer ? "AP_DELETE_MP_SAVES" : "AP_DELETE_SP_SAVES";
        string category = multiplayer ? "multiplayer campaigns" : "singleplayer checkpoints";
        string detail = multiplayer
            ? "A non-AP current multiplayer save will be preserved."
            : "AP server cloud backups will not be deleted.";
        TextUtility.RegisterLocString(
            key + "_HEADER",
            $"Delete All Local AP {category}?",
            "ap"
        );
        TextUtility.RegisterLocString(
            key + "_BODY",
            $"This permanently deletes every local AP {category} for STS profile "
                + $"{SaveManager.Instance.CurrentProfileId}. This cannot be undone. {detail}",
            "ap"
        );
        TextUtility.RegisterLocString(key + "_CONFIRM", "Delete Permanently", "ap");

        new ConfirmPopup
        {
            Header = TextUtility.GetLocString(key + "_HEADER", "ap"),
            Body = TextUtility.GetLocString(key + "_BODY", "ap"),
            YesString = TextUtility.GetLocString(key + "_CONFIRM", "ap"),
            ButtonPressed = confirmed => DeleteIfConfirmed(confirmed, multiplayer, requestRefresh),
        }.Show();
    }

    private static void DeleteIfConfirmed(bool confirmed, bool multiplayer, Action requestRefresh)
    {
        if (!confirmed)
            return;
        if (!CanDelete())
        {
            NotificationUtility.ShowRawText(
                "Local AP saves were preserved because the game left the main menu."
            );
            return;
        }

        try
        {
            if (multiplayer)
                DeleteMultiplayer();
            else
                DeleteSingleplayer();
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Could not delete local AP saves: {ex}");
            NotificationUtility.ShowRawText(
                "Could not delete every requested local AP save. Check the log before trying again."
            );
        }
        finally
        {
            requestRefresh();
        }
    }

    private static void DeleteMultiplayer()
    {
        ApMultiplayerCampaignStore.DeleteAllResult result =
            ApMultiplayerCampaignStore.DeleteAllLocalCampaignsForCurrentProfile();
        LogUtility.Info(
            $"Deleted {result.CampaignDirectories} local AP multiplayer campaign(s) "
                + $"for profile {SaveManager.Instance.CurrentProfileId}; "
                + $"activeApSaveDeleted={result.ActiveApSaveDeleted}."
        );
        NotificationUtility.ShowRawText(
            result.CampaignDirectories > 0
                ? $"Deleted {result.CampaignDirectories} local AP multiplayer campaign(s)."
                : result.ActiveApSaveDeleted
                    ? "Deleted the local AP multiplayer save."
                    : "No local AP multiplayer campaigns were found."
        );
    }

    private static void DeleteSingleplayer()
    {
        bool deleted = ApSingleplayerSaves.DeleteAllLocalCheckpointsForCurrentProfile();
        LogUtility.Info(
            $"Local AP singleplayer checkpoints for profile "
                + $"{SaveManager.Instance.CurrentProfileId}: deleted={deleted}."
        );
        NotificationUtility.ShowRawText(
            deleted
                ? "Deleted all local AP singleplayer checkpoints for this profile."
                : "No local AP singleplayer checkpoints were found."
        );
    }
}
