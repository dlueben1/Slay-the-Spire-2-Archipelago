using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using StS2AP.Patches;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace StS2AP.UI
{
    public class ContinueSavePopupUI : NAbandonRunConfirmPopup
    {
        private NVerticalPopup _verticalPopup;

        private readonly NCharacterSelectScreen _charSelect;
        public ContinueSavePopupUI(NCharacterSelectScreen charSelect)
        {
            _charSelect = charSelect;
        }
        public override void _Ready()
        {
            _verticalPopup = GetNode<NVerticalPopup>("VerticalPopup");
            _verticalPopup.SetText(new LocString("main_menu_ui", "CONTINUE_RUN.header"),
                new LocString("main_menu_ui", "CONTINUE_RUN.body"));
            Type parent = typeof(NAbandonRunConfirmPopup);
            FieldInfo finfo = parent.GetField("_verticalPopup", BindingFlags.NonPublic | BindingFlags.Instance);
            finfo.SetValue(_verticalPopup, this);
            _verticalPopup.InitYesButton(new LocString("main_menu_ui", "GENERIC_POPUP.confirm"), OnYesButtonPressed);
            _verticalPopup.InitNoButton(new LocString("main_menu_ui", "GENERIC_POPUP.cancel"), OnNoButtonPressed);
        }

        private void OnYesButtonPressed(NButton _)
        {
            TaskHelper.RunSafely(ContinueRun());
        }

        private async Task ContinueRun()
        {
            try
            {
                NAudioManager.Instance?.StopMusic();
                // TODO: need to actually set to the run
                string saveStr;
                var charName = _charSelect.Lobby.LocalPlayer.character.GetType().Name;
                if(GameUtility.APSaves.TryGetValue(charName, out saveStr)) {
                    var unzipped = Patches_RunSaveManager.SaveRun.Unzip(saveStr);
                    SerializableRun serializableRun = JsonSerializer.Deserialize<SerializableRun>(unzipped);
                    RunState runState = RunState.FromSerializable(serializableRun);
                    RunManager.Instance.SetUpSavedSinglePlayer(runState, serializableRun);
                    Log.Info($"Continuing run with character: {serializableRun.Players[0].CharacterId}");
                    SfxCmd.Play(runState.Players[0].Character.CharacterTransitionSfx);
                    await NGame.Instance.Transition.FadeOut(0.8f, runState.Players[0].Character.CharacterSelectTransitionPath);
                    NGame.Instance.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
                    await NGame.Instance.LoadRun(runState, serializableRun.PreFinishedRoom);
                    await NGame.Instance.Transition.FadeIn();
                }
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to load AP save: {ex.Message}");
                DisplayLoadSaveError();
                throw;
            }
        }

        private void DisplayLoadSaveError()
        {
            NErrorPopup modalToCreate = NErrorPopup.Create(new LocString("main_menu_ui", "INVALID_SAVE_POPUP.title"), new LocString("main_menu_ui", "INVALID_SAVE_POPUP.description_run"), new LocString("main_menu_ui", "INVALID_SAVE_POPUP.dismiss"), showReportBugButton: true);
            NModalContainer.Instance.Add(modalToCreate);
            NModalContainer.Instance.ShowBackstop();
        }

        private void OnNoButtonPressed(NButton _)
        {
            _charSelect.Lobby.SetReady(ready: true);
        }
    }
}
