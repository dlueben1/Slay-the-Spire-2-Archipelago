using StS2AP.Data;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using StS2AP.Patches;
using StS2AP.Utils;
using STS2RitsuLib.Networking.Sidecar;

namespace StS2AP.Persistence;

internal static class ApSingleplayerSaves
{
    private static SingleplayerCheckpointBank.BankKey? _selected;
    private static SingleplayerCheckpointBank? _bank;
    // Set before native setup, whose reload counter writes through the native save manager.
    internal static bool IsHandlingSingleplayerRun => _selected != null;

    private static string CurrentProfileRoot => ProjectSettings.GlobalizePath(
        $"user://ArchipelagoSingleplayerCheckpoints/profile-{SaveManager.Instance.CurrentProfileId}");

    internal static SingleplayerCheckpointBank Bank => new(CurrentProfileRoot);

    internal static bool DeleteAllLocalCheckpointsForCurrentProfile()
    {
        if (IsHandlingSingleplayerRun)
            throw new InvalidOperationException("Local AP checkpoints cannot be deleted during a run.");
        if (!Directory.Exists(CurrentProfileRoot))
            return false;

        Directory.Delete(CurrentProfileRoot, recursive: true);
        return true;
    }

    internal static SingleplayerCheckpointBank.Identity CurrentIdentity()
    {
        var session = ArchipelagoClient.Session
            ?? throw new InvalidOperationException("Connect to the AP slot before selecting a checkpoint.");
        if (!ArchipelagoClient.IsConnected || string.IsNullOrWhiteSpace(session.RoomState.Seed))
            throw new InvalidOperationException("The AP slot is not connected.");
        return new(session.RoomState.Seed, session.ConnectionInfo.Team, session.ConnectionInfo.Slot,
            CoopSlot.PlayerNumber);
    }

    internal static void BeginNew(string character)
    {
        // Selecting a new attempt does not write or clear any checkpoint positions.
        _selected = new(CurrentIdentity(), character);
        _bank = Bank;
    }

    internal static bool CanLoad(string key, string character, bool startOfAct)
    {
        var settings = ArchipelagoClient.Settings;
        if (settings == null || !settings.Characters.TryGetValue(character, out CharacterConfig? config))
            return false;
        return SingleplayerCheckpointBank.IsAllowed(key,
            startOfAct && settings.APWorldVersion > Constants.VERSION_0_5_3,
            ArchipelagoClient.Progress.MaxProgressiveAncientLevel(config.CharOffset));
    }

    internal static async Task Load(SingleplayerCheckpointBank.BankKey key, string checkpoint)
    {
        if (key.Owner != CurrentIdentity())
            throw new InvalidOperationException("The connected AP slot changed. Reopen the checkpoint picker.");
        var bank = Bank;
        var saved = bank.Load(key, checkpoint);
        if (!CanLoad(checkpoint, key.Character, saved.Snapshot.StartOfAct))
            throw new InvalidOperationException("This checkpoint's Start of Act Ancient is locked.");
        _selected = key;
        _bank = bank;
        // Keep ownership on setup failure: native cleanup must still preserve the unrelated save.
        await Patches_NCharacterSelectScreen.RestoreRun(saved.Payload,
            $"local AP checkpoint {checkpoint}", key.Character);
    }

    internal static Task LoadRemote(SingleplayerCheckpointBank.BankKey key)
    {
        if (key.Owner != CurrentIdentity())
            throw new InvalidOperationException("The connected AP slot changed. Reopen the checkpoint picker.");

        // DataStorage completes away from Godot's thread. Publish both success and failure
        // back on the main loop so the caller can safely update the modal or run lifecycle.
        var completion = new TaskCompletionSource();
        _ = DownloadAndRestore();
        return completion.Task;

        async Task DownloadAndRestore()
        {
            try
            {
                string payload = await ApRemoteSingleplayerSave.Download(key.Character);
                bool posted = RitsuLibSidecarGodotMainLoopScheduling.TryPostToMainLoop(
                    () => _ = RestoreOnMainLoop(payload)
                );
                if (!posted)
                    completion.TrySetException(
                        new InvalidOperationException("The Godot main loop is unavailable.")
                    );
            }
            catch (Exception ex)
            {
                bool posted = RitsuLibSidecarGodotMainLoopScheduling.TryPostToMainLoop(
                    () => completion.TrySetException(ex)
                );
                if (!posted)
                    completion.TrySetException(ex);
            }
        }

        async Task RestoreOnMainLoop(string payload)
        {
            try
            {
                if (key.Owner != CurrentIdentity())
                    throw new InvalidOperationException(
                        "The connected AP slot changed while downloading the remote save."
                    );
                _selected = key;
                _bank = Bank;
                // Keep ownership on setup failure so native cleanup preserves unrelated saves.
                await Patches_NCharacterSelectScreen.RestoreRun(
                    payload,
                    "remote AP checkpoint",
                    key.Character
                );
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }
    }

    internal static void Save(SerializableRun snapshot, string kind)
    {
        string? payload = null;
        string? character = null;
        try
        {
            var bankKey = _selected ?? throw new InvalidOperationException("No AP singleplayer character is selected.");
            var bank = _bank ?? throw new InvalidOperationException("No AP save directory is selected.");
            string key = $"{snapshot.CurrentActIndex + 1}-{kind}";
            bool startOfAct = AncientSettingsUtility.Current.Location == AncientRelicLocation.StartOfAct;
            if (!CanLoad(key, bankKey.Character, startOfAct)) return;
            character = bankKey.Character;
            payload = Patches_RunSaveManager.SaveRun.SerializeAndCompress(snapshot);
            bank.Save(bankKey, key, payload,
                snapshot.MapPointHistory?.Sum(act => act.Count) ?? 0, startOfAct);
            LogUtility.Info($"AP local checkpoint saved: character={bankKey.Character}, checkpoint={key}");
            NotificationUtility.ShowRawText("[font_size=80]GAME SAVED[/font_size]", timeout: 3.5,
                priority: NotificationUtility.NotificationPriority.High, includeInDevConsole: false);
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Failed to save local AP checkpoint: {ex}");
            NotificationUtility.ShowRawText("AP checkpoint save failed. Previous checkpoints were preserved; check the log.");
        }

        // The opt-in remote copy is deliberately independent: a DataStorage failure must never
        // undo the local checkpoint, and a local publication failure can still leave a backup.
        if (payload != null && character != null)
            ApRemoteSingleplayerSave.Upload(character, payload);
    }

    internal static void ClearSelection() { _selected = null; _bank = null; }
}
