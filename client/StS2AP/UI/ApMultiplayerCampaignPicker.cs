using static StS2AP.UI.ApCampaignUi;
using static StS2AP.UI.RunGraph.RunGraphStyle;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using StS2AP.UI.RunGraph;
using StS2AP.Utils;
using CampaignMetadata = StS2AP.Multiplayer.ApMultiplayerCampaignStore.CampaignMetadata;

namespace StS2AP.UI;

/// <summary>AP-specific replacement for the native Host / Host from Save split.</summary>
public sealed partial class ApMultiplayerCampaignPicker : Control, IScreenContext
{
    private readonly NMultiplayerHostSubmenu _hostSubmenu;
    private readonly GameMode _gameMode;
    private readonly bool _isCustom;
    private readonly IReadOnlyList<ApMultiplayerCampaignStore.CampaignEntry> _entries;
    private readonly Func<CampaignMetadata, bool> _isCurrentSlot;
    private bool _built;
    private Control? _defaultFocusedControl;

    public Control? DefaultFocusedControl => _defaultFocusedControl;

    private ApMultiplayerCampaignPicker(
        NMultiplayerHostSubmenu hostSubmenu,
        GameMode gameMode,
        bool isCustom,
        IReadOnlyList<ApMultiplayerCampaignStore.CampaignEntry> entries,
        Func<CampaignMetadata, bool> isCurrentSlot)
    {
        _hostSubmenu = hostSubmenu;
        _gameMode = gameMode;
        _isCustom = isCustom;
        _entries = entries;
        _isCurrentSlot = isCurrentSlot;
        Name = "ApMultiplayerCampaignPicker";
    }

    public static void Show(NMultiplayerHostSubmenu hostSubmenu, GameMode gameMode, bool isCustom) =>
        Show(hostSubmenu, gameMode, isCustom, ApMultiplayerCampaignStore.ListCampaigns(),
            ApMultiplayerCampaignStore.IsCurrentApIdentity);

    /// <summary>Shows <paramref name="entries"/>, with <paramref name="isCurrentSlot"/> picking the connected slot's own.</summary>
    internal static void Show(
        NMultiplayerHostSubmenu hostSubmenu,
        GameMode gameMode,
        bool isCustom,
        IReadOnlyList<ApMultiplayerCampaignStore.CampaignEntry> entries,
        Func<CampaignMetadata, bool> isCurrentSlot)
    {
        NModalContainer container = NModalContainer.Instance
            ?? throw new InvalidOperationException("The modal container is unavailable.");
        var picker = new ApMultiplayerCampaignPicker(hostSubmenu, gameMode, isCustom, entries, isCurrentSlot);
        picker.BuildUi();
        container.Clear();
        container.Add(picker, true);
        // Escape closes the picker rather than leaving the host menu underneath it.
        ApOverlayLayer.BindBack(picker, picker.CloseFromBack);
    }

    public override void _Ready() => BuildUi();

    private void BuildUi()
    {
        if (_built)
            return;
        _built = true;

        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ZAsRelative = false;
        ZIndex = 100;

        var window = CreateWindow(this, 1640f, 860f);
        window.AddChild(CreateHeader(null, "Archipelago multiplayer", "Campaigns", Gold, SlotInfo()));

        var body = HBox(28);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        var padded = Padded(body, 32, 24);
        padded.SizeFlagsVertical = SizeFlags.ExpandFill;
        window.AddChild(padded);
        body.AddChild(BuildActions());
        body.AddChild(new ColorRect { Color = PanelBorder, CustomMinimumSize = new Vector2(1f, 0f) });

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        body.AddChild(scroll);
        // Room on the right for the scroll bar, so it never covers a card's buttons.
        var gutter = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        gutter.AddThemeConstantOverride("margin_right", 16);
        scroll.AddChild(gutter);
        var list = VBox(14);
        gutter.AddChild(list);

        AddActiveSection(list);
        AddHistorySection(list);
        AddOtherSlotsSection(list);
        AddCorruptSection(list);
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
        var caption = Caps($"Team {MultiplayerSupport.PreparedApTeamId} · slot {MultiplayerSupport.PreparedApSlotId}", 11, TextMuted);
        caption.HorizontalAlignment = HorizontalAlignment.Right;
        info.AddChild(caption);
        return info;
    }

    /// <summary>The left column: a new campaign, and leaving.</summary>
    private Control BuildActions()
    {
        var column = VBox(14);
        column.CustomMinimumSize = new Vector2(340f, 0f);

        var content = VBox(6);
        content.AddChild(Label("Start new campaign", 24, Gold, bold: true));
        content.AddChild(CreateLabel("Host a new run for this AP slot. Saved campaigns are kept.", 15, TextMuted));
        var startNew = CreateCard(content, 128f, Gold);
        startNew.AddThemeStyleboxOverride("normal", Box(Card, Gold.Darkened(0.3f), 12, 0f, borderWidth: 2));
        startNew.Pressed += StartNewCampaign;
        column.AddChild(startNew);
        _defaultFocusedControl = startNew;

        column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        Button cancel = CreateButton("Cancel");
        cancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cancel.Pressed += Close;
        column.AddChild(cancel);
        return column;
    }

    private CampaignMetadata[] Usable(Func<CampaignMetadata, bool> where) => _entries
        .Where(entry => entry.IsUsable && entry.Metadata != null)
        .Select(entry => entry.Metadata!)
        .Where(where)
        .ToArray();

    private void AddActiveSection(VBoxContainer list)
    {
        list.AddChild(CreateSectionHeading("Active campaigns", "for this AP slot"));
        CampaignMetadata[] active = Usable(metadata =>
            metadata.Status == ApMultiplayerCampaignStore.CampaignStatus.Active && _isCurrentSlot(metadata));
        if (active.Length == 0)
        {
            list.AddChild(EmptyNote("No resumable campaigns for this AP slot."));
            return;
        }
        foreach (CampaignMetadata campaign in active)
            list.AddChild(ActiveCampaignCard(campaign));
    }

    private Control ActiveCampaignCard(CampaignMetadata campaign)
    {
        var card = CreatePanel();
        var box = VBox(16);
        card.AddChild(box);

        // The newest save says where everyone stands; the checkpoint is older or the same.
        var summary = SummaryOf(campaign, ApMultiplayerCampaignStore.SaveKind.FloorRecovery)
            ?? SummaryOf(campaign, ApMultiplayerCampaignStore.SaveKind.ApCheckpoint);

        var top = HBox(20);
        var roster = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        roster.AddThemeConstantOverride("h_separation", 14);
        roster.AddThemeConstantOverride("v_separation", 12);
        foreach (var player in OrderedRoster(campaign))
            roster.AddChild(PlayerChip(player, summary?.Players.FirstOrDefault(p => p.NetId == player.NetId)));
        top.AddChild(roster);
        var where = VBox(0);
        where.AddChild(RightCaps($"Act {campaign.Act}"));
        var floor = Label($"Floor {campaign.CompletedFloorCount}", 30, Text, bold: true);
        floor.HorizontalAlignment = HorizontalAlignment.Right;
        where.AddChild(floor);
        top.AddChild(Centered(where));
        box.AddChild(top);

        var buttons = HBox(10);
        buttons.AddChild(CreateResumeButton(campaign, ApMultiplayerCampaignStore.SaveKind.FloorRecovery));
        buttons.AddChild(CreateResumeButton(campaign, ApMultiplayerCampaignStore.SaveKind.ApCheckpoint));
        Button abandon = CreateButton("Abandon", danger: true);
        abandon.Pressed += () => ShowAbandonChoices(campaign);
        buttons.AddChild(abandon);
        box.AddChild(buttons);

        box.AddChild(Label($"Last saved {FormatSaved(campaign.LastSavedAtUtc)} · started {campaign.CreatedAtUtc.ToLocalTime():MMM d}",
            13, TextFaint));
        return card;
    }

    /// <summary>One player: their character, name and, when the save can be read, how they are doing.</summary>
    private static Control PlayerChip(ApMultiplayerCampaignStore.CampaignRosterEntry player, ApSavePlayer? state)
    {
        var character = state?.Character ?? FindCharacter(player.CharacterId);
        Color color = character?.NameColor ?? TextMuted;
        string characterName = CharacterName(character, player.CharacterId);

        var chip = HBox(12);
        chip.CustomMinimumSize = new Vector2(300f, 0f);
        chip.AddChild(ApRunRecapView.CharacterIcon(character, characterName, color, 56f));
        var text = VBox(4);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        string name = string.IsNullOrWhiteSpace(player.DisplayName) ? characterName : player.DisplayName;
        var nameRow = HBox(8);
        nameRow.AddChild(Label(name, 18, Text, bold: true));
        nameRow.AddChild(Centered(Label(characterName, 13, color.Lightened(0.3f))));
        text.AddChild(nameRow);
        if (state != null)
        {
            text.AddChild(CreateHpBar(state.Hp, state.MaxHp));
            var stats = HBox(10);
            stats.AddChild(Centered(Label($"{state.Gold} gold", 13, Gold)));
            stats.AddChild(Centered(Label($"{state.DeckSize} cards", 13, TextMuted)));
            stats.AddChild(CreateRelicStrip(state.Relics, 4, 22f));
            text.AddChild(stats);
        }
        chip.AddChild(text);
        return chip;
    }

    private Button CreateResumeButton(
        CampaignMetadata campaign,
        ApMultiplayerCampaignStore.SaveKind kind)
    {
        bool recovery = kind == ApMultiplayerCampaignStore.SaveKind.FloorRecovery;
        string label = recovery ? "Floor Recovery" : "AP Checkpoint";
        var snapshot = ApMultiplayerCampaignStore.GetSnapshot(campaign, kind);
        string? error = ApMultiplayerCampaignStore.GetSnapshotError(campaign, kind);
        Button button = CreateButton(snapshot == null ? label :
            $"{label} · Act {snapshot.Act}, Floor {snapshot.CompletedFloorCount}", primary: recovery);
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        button.Disabled = error != null;
        button.TooltipText = error ?? $"Saved: {snapshot!.SavedAtUtc.ToLocalTime():g}\n"
            + (recovery ? "Latest native floor save." : "Last eligible AP checkpoint; later floor saves do not replace it.");
        button.Pressed += () => ContinueCampaign(campaign, kind);
        return button;
    }

    private void AddHistorySection(VBoxContainer list)
    {
        list.AddChild(CreateSectionHeading("History", "completed and archived"));
        CampaignMetadata[] history = Usable(metadata =>
            metadata.Status != ApMultiplayerCampaignStore.CampaignStatus.Active && _isCurrentSlot(metadata));
        if (history.Length == 0)
        {
            list.AddChild(EmptyNote("No completed or archived campaigns."));
            return;
        }

        foreach (CampaignMetadata campaign in history)
        {
            bool completed = campaign.Status == ApMultiplayerCampaignStore.CampaignStatus.Completed;
            var row = CompactRow(campaign, completed ? Gold.Darkened(0.45f) : PanelBorder);
            var line = (HBoxContainer)row.GetChild(0);
            line.AddChild(Centered(ApRunRecapView.Pill(campaign.Status.ToString(), completed ? Gold : TextMuted)));
            Button delete = CreateButton("Delete Permanently", danger: true);
            delete.Pressed += () => ShowDeleteChoice(campaign);
            line.AddChild(delete);
            list.AddChild(row);
        }
    }

    private void AddOtherSlotsSection(VBoxContainer list)
    {
        CampaignMetadata[] other = _entries
            .Where(entry => entry.Metadata != null)
            .Select(entry => entry.Metadata!)
            .Where(metadata => !_isCurrentSlot(metadata))
            .ToArray();
        if (other.Length == 0)
            return;

        list.AddChild(CreateSectionHeading("Other AP slots", "unavailable while connected to this slot"));
        foreach (CampaignMetadata campaign in other)
        {
            var row = CompactRow(campaign, PanelBorder, faded: true);
            ((HBoxContainer)row.GetChild(0)).AddChild(Centered(Label(campaign.ApSlotName, 16, TextMuted, bold: true)));
            list.AddChild(row);
        }
    }

    private void AddCorruptSection(VBoxContainer list)
    {
        ApMultiplayerCampaignStore.CampaignEntry[] corrupt = _entries
            .Where(entry => !entry.IsUsable)
            .ToArray();
        if (corrupt.Length == 0)
            return;

        list.AddChild(CreateSectionHeading("Unavailable campaigns", "these saves could not be read"));
        foreach (ApMultiplayerCampaignStore.CampaignEntry entry in corrupt)
        {
            var card = CreatePanel(Death.Darkened(0.5f), 14f);
            card.Modulate = new Color(1f, 1f, 1f, 0.75f);
            card.AddChild(CreateLabel($"{entry.Metadata?.HostCharacterId ?? entry.CampaignId} · {entry.Error}", 15, DeathText));
            list.AddChild(card);
        }
    }

    /// <summary>A one-line campaign: its characters, where it ended and when. Callers add to the end of the row.</summary>
    private static PanelContainer CompactRow(CampaignMetadata campaign, Color border, bool faded = false)
    {
        var card = CreatePanel(border, 14f);
        if (faded)
            card.Modulate = new Color(1f, 1f, 1f, 0.6f);
        card.TooltipText = FormatCampaignDetails(campaign);
        var line = HBox(14);
        card.AddChild(line);

        var icons = HBox(6);
        foreach (var player in OrderedRoster(campaign))
        {
            var character = FindCharacter(player.CharacterId);
            string name = CharacterName(character, player.CharacterId);
            icons.AddChild(ApRunRecapView.CharacterIcon(character, name, character?.NameColor ?? TextMuted, 42f));
        }
        line.AddChild(icons);

        var text = VBox(2);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        text.AddChild(Label(FormatLineup(campaign), 17, Text, bold: true));
        string players = campaign.Roster.Count == 1 ? "1 player" : $"{campaign.Roster.Count} players";
        text.AddChild(Label($"Act {campaign.Act}, Floor {campaign.CompletedFloorCount} · {players} · last saved {FormatSaved(campaign.LastSavedAtUtc)}",
            13, TextMuted));
        line.AddChild(text);
        return card;
    }

    private static Control EmptyNote(string text)
    {
        var card = ApRunRecapView.Panel(PanelColor with { A = 0.5f }, PanelBorder with { A = 0.5f }, 12, 18f);
        card.AddChild(CreateLabel(text, 16, TextMuted));
        return card;
    }

    private static Label RightCaps(string text)
    {
        var label = Caps(text, 13, TextMuted, spacing: 3);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        return label;
    }

    private static IEnumerable<ApMultiplayerCampaignStore.CampaignRosterEntry> OrderedRoster(CampaignMetadata campaign) =>
        campaign.Roster
            .OrderBy(player => player.NetId == campaign.HostNetId ? 0 : 1)
            .ThenBy(player => player.NetId);

    private static ApSaveSummary? SummaryOf(CampaignMetadata campaign, ApMultiplayerCampaignStore.SaveKind kind) =>
        ApMultiplayerCampaignStore.TryReadSnapshotJson(campaign, kind) is { } json ? ApSaveSummary.FromRunJson(json) : null;

    private void StartNewCampaign()
    {
        ApMultiplayerCampaignStore.BeginNewCampaign();
        Close();
        ApMultiplayerCampaignFlow.ResumeNewCampaign(_hostSubmenu, _gameMode, _isCustom);
    }

    private void ContinueCampaign(
        CampaignMetadata campaign,
        ApMultiplayerCampaignStore.SaveKind kind)
    {
        try
        {
            string? compatibilityError = ApMultiplayerCampaignStore.GetCompatibilityError(campaign, kind);
            if (compatibilityError != null)
            {
                LogUtility.Warn($"Blocked AP campaign continue: {compatibilityError}");
                NotificationUtility.ShowRawText(compatibilityError);
                return;
            }
            ApMultiplayerCampaignStore.ActivateCampaign(campaign, kind);
            ReadSaveResult<SerializableRun> read = SaveManager.Instance
                .LoadAndCanonicalizeMultiplayerRunSave(
                    PlatformUtil.GetLocalPlayerId(GetVanillaPlatform())
                );
            if (!read.Success || read.SaveData == null)
                throw new InvalidDataException($"The activated save could not be loaded: {read.Status}");

            Close();
            NSubmenuStack submenuStack = MenuUtility.SubmenuStack
                ?? throw new InvalidOperationException(
                    "The main-menu submenu stack is unavailable."
                );
            submenuStack.GetSubmenuType<NMultiplayerSubmenu>()
                .StartHost(read.SaveData);
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Failed to continue AP multiplayer campaign: {ex}");
            NotificationUtility.ShowRawText(
                "The selected multiplayer campaign could not be loaded. It was preserved."
            );
        }
    }

    private void ShowAbandonChoices(CampaignMetadata campaign)
    {
        ShowChoiceOverlay(
            "Abandon Campaign",
            "Archive keeps both saves as view-only history. Delete Permanently removes the campaign from this machine.",
            ("Archive", false, () =>
            {
                ApMultiplayerCampaignStore.ArchiveCampaign(campaign.CampaignId);
                Refresh();
            }),
            ("Delete Permanently", true, () =>
            {
                ApMultiplayerCampaignStore.DeleteCampaign(campaign.CampaignId);
                Refresh();
            })
        );
    }

    private void ShowDeleteChoice(CampaignMetadata campaign)
    {
        ShowChoiceOverlay(
            "Delete Campaign Permanently?",
            "This removes both local campaign saves and cannot be undone.",
            ("Delete Permanently", true, () =>
            {
                ApMultiplayerCampaignStore.DeleteCampaign(campaign.CampaignId);
                Refresh();
            })
        );
    }

    private void ShowChoiceOverlay(
        string title,
        string body,
        params (string Label, bool Danger, Action Action)[] actions)
    {
        var overlay = new Control { MouseFilter = MouseFilterEnum.Stop, ZIndex = 200 };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(overlay);
        var dim = new ColorRect { Color = Backdrop with { A = 0.6f }, MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(dim);

        var window = CreateWindow(overlay, 760f, 290f);
        window.AddChild(CreateHeader(null, "Multiplayer campaign", title, Text));
        var content = VBox(20);
        content.SizeFlagsVertical = SizeFlags.ExpandFill;
        window.AddChild(Padded(content, 32, 24));
        var text = CreateLabel(body, 18, TextMuted);
        text.SizeFlagsVertical = SizeFlags.ExpandFill;
        content.AddChild(text);

        var buttons = HBox(10);
        buttons.Alignment = BoxContainer.AlignmentMode.End;
        Button cancel = CreateButton("Cancel");
        cancel.Pressed += overlay.QueueFree;
        buttons.AddChild(cancel);
        foreach ((string label, bool danger, Action action) in actions)
        {
            Button button = CreateButton(label, danger: danger);
            button.Pressed += () =>
            {
                overlay.QueueFree();
                action();
            };
            buttons.AddChild(button);
        }
        content.AddChild(buttons);
        cancel.CallDeferred(Control.MethodName.GrabFocus);
        // Bound after the picker's own, so Escape closes this dialog first.
        ApOverlayLayer.BindBack(overlay, overlay.QueueFree);
    }

    private void Refresh()
    {
        Close();
        Show(_hostSubmenu, _gameMode, _isCustom);
    }

    private static string FormatLineup(CampaignMetadata campaign) =>
        string.Join(" + ", OrderedRoster(campaign).Select(player =>
            CharacterName(FindCharacter(player.CharacterId), player.CharacterId)));

    private static string FormatCampaignDetails(CampaignMetadata campaign)
    {
        string roster = string.Join(", ", campaign.Roster.Select(player =>
            string.IsNullOrWhiteSpace(player.DisplayName)
                ? player.CharacterId
                : $"{player.DisplayName} ({player.CharacterId})"));
        return $"Players: {roster}\nLast saved: {campaign.LastSavedAtUtc.ToLocalTime():g}";
    }

    private static PlatformType GetVanillaPlatform() =>
        SteamInitializer.Initialized && !CommandLineHelper.HasArg("fastmp")
            ? (PlatformType)1
            : (PlatformType)0;

    private static void Close() => NModalContainer.Instance?.Clear();

    // An instance method, so each picker's binding is its own: the hotkey manager skips and
    // removes bindings by equality, and a refreshed picker must not lose its binding to the old one's cleanup.
    private void CloseFromBack() => Close();
}
