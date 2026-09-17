using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using StS2AP.Utils;
using static StS2AP.UI.ApCampaignUi;

namespace StS2AP.UI;

/// <summary>Character selection opens its six shared checkpoint positions directly.</summary>
public sealed partial class ApSingleplayerCheckpointPicker : Control, IScreenContext
{
    private NCharacterSelectScreen _screen = null!;
    private string _character = "";
    private VBoxContainer _list = null!;
    private bool _loading;
    private Control? _defaultFocus;
    public Control? DefaultFocusedControl => _defaultFocus;

    public static void Show(NCharacterSelectScreen screen, string character)
    {
        var picker = new ApSingleplayerCheckpointPicker { _screen = screen, _character = character };
        picker.Build();
        var container = NModalContainer.Instance
            ?? throw new InvalidOperationException("The modal container is unavailable.");
        container.Clear();
        container.Add(picker, true);
    }

    private void Build()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        // Both AP trackers live in separate layer-0 canvases. A higher canvas
        // layer keeps the picker above them regardless of scene insertion order.
        // Keep it owned by the modal so clearing the picker also frees its canvas.
        var canvas = new CanvasLayer { Name = "ApCheckpointPickerLayer", Layer = 1 };
        AddChild(canvas);
        var overlay = new Control { MouseFilter = MouseFilterEnum.Stop };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        canvas.AddChild(overlay);
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.OffsetLeft = -530; panel.OffsetRight = 530;
        panel.OffsetTop = -350; panel.OffsetBottom = 350;
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
        overlay.AddChild(panel);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        panel.AddChild(root);
        root.AddChild(CreateLabel($"AP Singleplayer — {_character}", 30, HorizontalAlignment.Center));
        root.AddChild(CreateLabel("Choose a checkpoint or start again. Existing checkpoints stay until you reach and overwrite them.", 19));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_list);
        var cancel = CreateButton("Cancel");
        cancel.Pressed += () => { if (!_loading) NModalContainer.Instance?.Clear(); };
        root.AddChild(cancel);
        ShowCheckpoints();
    }

    private void ShowCheckpoints()
    {
        var start = CreateButton("Start New Run", primary: true);
        start.Pressed += () =>
        {
            if (_loading) return;
            if (!ArchipelagoClient.CanSelectCharacter(BetaMainCompatibility.GetLocalCharacter(_screen.Lobby), out string reason))
            { NotificationUtility.ShowRawText(reason); return; }
            NModalContainer.Instance?.Clear();
            _screen.Lobby.SetReady(ready: true);
        };
        _list.AddChild(start);
        _defaultFocus = start;
        if (IsInsideTree()) start.GrabFocus();
        try
        {
            var bankKey = new SingleplayerCheckpointBank.BankKey(ApSingleplayerSaves.CurrentIdentity(), _character);
            if (ApRemoteSingleplayerSave.IsEnabled)
            {
                var remote = CreateButton("Load Remote Save");
                remote.TooltipText = "Download and load the latest remote checkpoint for this character.";
                remote.Pressed += () => { if (!_loading) _ = LoadRemote(bankKey); };
                _list.AddChild(remote);
            }

            var bank = ApSingleplayerSaves.Bank.Read(bankKey);
            foreach (string key in SingleplayerCheckpointBank.Milestones)
            {
                string label = key switch
                {
                    "1-ancient" => "Act 1 — Initial Ancient",
                    "1-boss" => "Act 1 — Boss defeated",
                    "2-boss" => "Act 2 — Boss defeated",
                    _ => $"Act {key[0]} — Treasure",
                };
                bool exists = bank.Checkpoints.TryGetValue(key, out var snapshot);
                bool allowed = ApSingleplayerSaves.CanLoad(key, _character, snapshot?.StartOfAct ?? false);
                var button = CreateButton(label + (exists ? $" · Floor {snapshot!.Floor}" : " · Not reached"));
                button.Disabled = !exists || !allowed;
                button.TooltipText = !allowed ? "Start of Act Ancient progression is locked."
                    : exists ? $"Saved {snapshot!.SavedAt.ToLocalTime():g}" : "No checkpoint recorded.";
                button.Pressed += () => { if (!_loading) _ = Load(bankKey, key); };
                _list.AddChild(button);
            }
        }
        catch (Exception ex) { _list.AddChild(CreateLabel($"Cannot read checkpoints: {ex.Message}", 18)); }
    }

    private async Task Load(SingleplayerCheckpointBank.BankKey bankKey, string key)
    {
        _loading = true;
        NModalContainer.Instance?.Clear();
        try
        {
            await ApSingleplayerSaves.Load(bankKey, key);
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Failed to load local AP checkpoint {bankKey.Character}/{key}: {ex}");
            NotificationUtility.ShowRawText($"Could not load checkpoint: {ex.Message}. Saved checkpoints were preserved.");
            // Setup may have partially initialized the native run. Return through its cleanup
            // with AP ownership still active, instead of allowing a second setup on stale state.
            if (ApSingleplayerSaves.IsHandlingSingleplayerRun)
                if (MegaCrit.Sts2.Core.Nodes.NGame.Instance is { } game)
                    await game.ReturnToMainMenuAfterRun();
        }
        finally { _loading = false; }
    }

    private async Task LoadRemote(SingleplayerCheckpointBank.BankKey bankKey)
    {
        _loading = true;
        NModalContainer.Instance?.Clear();
        try
        {
            await ApSingleplayerSaves.LoadRemote(bankKey);
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Failed to load remote AP checkpoint {bankKey.Character}: {ex}");
            NotificationUtility.ShowRawText($"Could not load remote save: {ex.Message}");
            if (ApSingleplayerSaves.IsHandlingSingleplayerRun)
                if (MegaCrit.Sts2.Core.Nodes.NGame.Instance is { } game)
                    await game.ReturnToMainMenuAfterRun();
        }
        finally { _loading = false; }
    }
}
