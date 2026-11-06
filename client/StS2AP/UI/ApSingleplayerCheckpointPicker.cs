using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Rooms;
using StS2AP.UI.RunGraph;
using StS2AP.Utils;
using static StS2AP.UI.ApCampaignUi;
using static StS2AP.UI.RunGraph.RunGraphStyle;

namespace StS2AP.UI;

/// <summary>Character selection opens its nine shared checkpoint positions directly.</summary>
public sealed partial class ApSingleplayerCheckpointPicker : Control, IScreenContext
{
    private const float CheckpointHeight = 128f;

    /// <summary>One checkpoint position: what is saved there, if anything, and whether it may load.</summary>
    internal sealed record CheckpointChoice(
        string Key,
        SingleplayerCheckpointBank.Snapshot? Snapshot,
        bool Allowed,
        ApSaveSummary? Summary);

    /// <summary>The character's checkpoint bank as the picker shows it, or why it could not be read.</summary>
    internal sealed record CheckpointList(
        SingleplayerCheckpointBank.BankKey? BankKey,
        IReadOnlyList<CheckpointChoice> Choices,
        string? Error);

    private NCharacterSelectScreen _screen = null!;
    private string _character = "";
    private CheckpointList _list = null!;
    private bool _loading;
    private Control? _defaultFocus;
    public Control? DefaultFocusedControl => _defaultFocus;

    public static void Show(NCharacterSelectScreen screen, string character) =>
        Show(screen, character, ReadCheckpoints(character));

    internal static void Show(NCharacterSelectScreen screen, string character, CheckpointList list)
    {
        var picker = new ApSingleplayerCheckpointPicker { _screen = screen, _character = character, _list = list };
        picker.Build();
        var container = NModalContainer.Instance
            ?? throw new InvalidOperationException("The modal container is unavailable.");
        container.Clear();
        container.Add(picker, true);
        // Escape closes the picker rather than leaving character select underneath it.
        ApOverlayLayer.BindBack(picker, picker.Cancel);
    }

    private static CheckpointList ReadCheckpoints(string character)
    {
        SingleplayerCheckpointBank.BankKey? bankKey = null;
        try
        {
            bankKey = new SingleplayerCheckpointBank.BankKey(ApSingleplayerSaves.CurrentIdentity(), character);
            var bank = ApSingleplayerSaves.Bank;
            var saved = bank.Read(bankKey);
            var choices = SingleplayerCheckpointBank.Milestones.Select(key =>
            {
                saved.Checkpoints.TryGetValue(key, out var snapshot);
                bool allowed = ApSingleplayerSaves.CanLoad(key, character, snapshot?.StartOfAct ?? false);
                return new CheckpointChoice(key, snapshot, allowed, snapshot == null ? null : ReadSummary(bank, bankKey, key));
            }).ToList();
            return new CheckpointList(bankKey, choices, null);
        }
        catch (Exception ex)
        {
            // A damaged local bank still leaves the remote save to load.
            return new CheckpointList(bankKey, Array.Empty<CheckpointChoice>(), ex.Message);
        }
    }

    /// <summary>What a checkpoint holds, for its card. A card without it still loads.</summary>
    internal static ApSaveSummary? ReadSummary(
        SingleplayerCheckpointBank bank, SingleplayerCheckpointBank.BankKey bankKey, string key)
    {
        try
        {
            return ApSaveSummary.FromCheckpoint(bank.Load(bankKey, key).Payload);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
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

        var window = CreateWindow(overlay, 1640f, 820f);
        var character = FindCharacter(_character);
        Color characterColor = character?.NameColor ?? Gold;
        window.AddChild(CreateHeader(
            ApRunRecapView.CharacterIcon(character, CharacterName(character, _character), characterColor, 64f),
            "Archipelago singleplayer",
            CharacterName(character, _character),
            characterColor,
            SlotInfo()));

        var body = HBox(28);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        var padded = Padded(body, 32, 24);
        padded.SizeFlagsVertical = SizeFlags.ExpandFill;
        window.AddChild(padded);
        body.AddChild(BuildActions());
        body.AddChild(new ColorRect { Color = PanelBorder, CustomMinimumSize = new Vector2(1f, 0f) });
        body.AddChild(BuildCheckpoints());
    }

    private static Control? SlotInfo()
    {
        string slot = ArchipelagoClient.PlayerName ?? "";
        if (slot.Length == 0)
            return null;
        var info = VBox(2);
        var name = Label(slot, 20, Text, bold: true);
        name.HorizontalAlignment = HorizontalAlignment.Right;
        info.AddChild(name);
        var caption = Caps("AP slot", 11, TextMuted);
        caption.HorizontalAlignment = HorizontalAlignment.Right;
        info.AddChild(caption);
        return info;
    }

    /// <summary>The left column: a new run, the remote save, and leaving.</summary>
    private Control BuildActions()
    {
        var column = VBox(14);
        column.CustomMinimumSize = new Vector2(340f, 0f);

        var start = CreateCard(ActionCardContent(
            "Start new run", "A fresh run with this character. Your checkpoints are kept.", Gold), 128f, Gold);
        start.AddThemeStyleboxOverride("normal", Box(Card, Gold.Darkened(0.3f), 12, 0f, borderWidth: 2));
        start.Pressed += StartNewRun;
        column.AddChild(start);
        _defaultFocus = start;
        Ready += () => start.GrabFocus();

        if (ApRemoteSingleplayerSave.IsEnabled && _list.BankKey != null)
        {
            var remote = CreateCard(ActionCardContent(
                "Load remote save", "Download and load the latest remote checkpoint for this character.", Useful), 118f, Useful);
            remote.Pressed += () => { if (!_loading) _ = LoadRemote(_list.BankKey); };
            column.AddChild(remote);
        }

        column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var cancel = CreateButton("Cancel");
        cancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cancel.Pressed += Cancel;
        column.AddChild(cancel);
        return column;
    }

    private void Cancel()
    {
        if (!_loading)
            NModalContainer.Instance?.Clear();
    }

    private static Control ActionCardContent(string title, string text, Color color)
    {
        var box = VBox(6);
        box.AddChild(Label(title, 24, color, bold: true));
        box.AddChild(CreateLabel(text, 15, TextMuted));
        return box;
    }

    /// <summary>The checkpoints, one column per act in the order a run reaches them.</summary>
    private Control BuildCheckpoints()
    {
        var area = VBox(16);
        area.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        area.AddChild(CreateSectionHeading("Checkpoints",
            "Choose a checkpoint or start again. Existing checkpoints stay until you reach and overwrite them."));

        if (_list.Error != null)
        {
            area.AddChild(CreateLabel($"Cannot read checkpoints: {_list.Error}", 18, DeathText));
            return area;
        }

        var acts = HBox(18);
        acts.SizeFlagsVertical = SizeFlags.ExpandFill;
        area.AddChild(acts);
        foreach (var act in _list.Choices.GroupBy(choice => choice.Key[0]))
        {
            var column = VBox(10);
            column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            column.AddChild(Caps($"Act {act.Key}", 13, TextMuted, spacing: 3));
            foreach (var choice in act)
                column.AddChild(CheckpointCard(choice));
            acts.AddChild(column);
        }
        return area;
    }

    private Control CheckpointCard(CheckpointChoice choice)
    {
        var snapshot = choice.Snapshot;
        var player = choice.Summary?.Players.FirstOrDefault();
        var content = VBox(10);

        var top = HBox(12);
        top.AddChild(RoomIcon(choice, dim: snapshot == null));
        var names = VBox(0);
        names.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        names.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        names.AddChild(Label(Title(choice.Key), 19, snapshot == null ? TextMuted : Text, bold: true));
        string detail = snapshot == null ? "Not reached yet"
            : !choice.Allowed ? "Locked: Start of Act Ancient progression"
            : $"Floor {snapshot.Floor} · saved {FormatSaved(snapshot.SavedAt)}";
        names.AddChild(Label(detail, 13, !choice.Allowed && snapshot != null ? DeathText : TextFaint));
        top.AddChild(names);
        content.AddChild(top);

        if (player != null)
        {
            content.AddChild(CreateHpBar(player.Hp, player.MaxHp));
            var stats = HBox(14);
            stats.AddChild(Centered(Label($"{player.Gold} gold", 14, Gold)));
            stats.AddChild(Centered(Label($"{player.DeckSize} cards", 14, TextMuted)));
            var relics = CreateRelicStrip(player.Relics, 5, 24f);
            relics.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            relics.Alignment = BoxContainer.AlignmentMode.End;
            stats.AddChild(relics);
            content.AddChild(stats);
        }

        var card = CreateCard(content, CheckpointHeight, choice.Allowed ? Progression : PanelBorder);
        card.Disabled = snapshot == null || !choice.Allowed;
        if (card.Disabled)
            card.FocusMode = FocusModeEnum.None;
        card.TooltipText = !choice.Allowed ? "Start of Act Ancient progression is locked."
            : snapshot != null ? $"Saved {snapshot.SavedAt.ToLocalTime():g}" : "No checkpoint recorded.";
        card.Pressed += () => { if (!_loading && _list.BankKey != null) _ = Load(_list.BankKey, choice.Key); };
        return card;
    }

    private static string Title(string key) => key[2..] switch
    {
        "ancient" => "Initial Ancient",
        "boss" => "Boss defeated",
        "campfire" => "Pre-boss Campfire",
        _ => "Treasure",
    };

    /// <summary>
    /// The room the checkpoint was saved in, read from the saved floors so a boss checkpoint shows
    /// that act's boss. A position not reached yet shows the room type's plain icon, if it has one.
    /// </summary>
    private static Control RoomIcon(CheckpointChoice choice, bool dim)
    {
        var (mapPoint, room) = choice.Key[2..] switch
        {
            "ancient" => (MapPointType.Ancient, RoomType.Event),
            "boss" => (MapPointType.Boss, RoomType.Boss),
            "campfire" => (MapPointType.RestSite, RoomType.RestSite),
            _ => (MapPointType.Treasure, RoomType.Treasure),
        };
        Texture2D? icon = choice.Summary?.Floors.LastOrDefault(floor => floor.MapPointType == mapPoint)?.Icon;
        if (icon == null && ImageHelper.GetRoomIconPath(mapPoint, room, null) is { } path && ResourceLoader.Exists(path))
            icon = PreloadManager.Cache.GetCompressedTexture2D(path);

        var frame = ApRunRecapView.Panel(Card.Darkened(0.25f), Card.Darkened(0.25f), 10, 4f);
        frame.CustomMinimumSize = new Vector2(48f, 48f);
        frame.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        if (icon != null)
        {
            frame.AddChild(new TextureRect
            {
                Texture = icon,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Modulate = dim ? new Color(1f, 1f, 1f, 0.3f) : Colors.White,
            });
        }
        return frame;
    }

    private void StartNewRun()
    {
        if (_loading) return;
        if (!ArchipelagoClient.CanSelectCharacter(_screen.Lobby.LocalPlayer.character, out string reason))
        { NotificationUtility.ShowRawText(reason); return; }
        NModalContainer.Instance?.Clear();
        _screen.Lobby.SetReady(ready: true);
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
