using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using StS2AP.Utils;

namespace StS2AP.UI;

/// <summary>AP-specific replacement for the native Host / Host from Save split.</summary>
public sealed partial class ApMultiplayerCampaignPicker : Control, IScreenContext
{
    private readonly NMultiplayerHostSubmenu _hostSubmenu;
    private readonly GameMode _gameMode;
    private bool _built;
    private Control? _defaultFocusedControl;

    public Control? DefaultFocusedControl => _defaultFocusedControl;

    private ApMultiplayerCampaignPicker(
        NMultiplayerHostSubmenu hostSubmenu,
        GameMode gameMode)
    {
        _hostSubmenu = hostSubmenu;
        _gameMode = gameMode;
        Name = "ApMultiplayerCampaignPicker";
    }

    public static void Show(NMultiplayerHostSubmenu hostSubmenu, GameMode gameMode)
    {
        NModalContainer container = NModalContainer.Instance
            ?? throw new InvalidOperationException("The modal container is unavailable.");
        var picker = new ApMultiplayerCampaignPicker(hostSubmenu, gameMode);
        picker.BuildUi();
        container.Clear();
        container.Add(picker, true);
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

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(1060f, 700f),
            MouseFilter = MouseFilterEnum.Stop,
            ZIndex = 101,
        };
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.OffsetLeft = -530f;
        panel.OffsetTop = -350f;
        panel.OffsetRight = 530f;
        panel.OffsetBottom = 350f;
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
        AddChild(panel);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.AddThemeConstantOverride("separation", 12);
        panel.AddChild(root);

        var title = CreateLabel("AP Multiplayer Campaigns", 34, HorizontalAlignment.Center);
        title.AddThemeColorOverride("font_color", new Color(0.96f, 0.78f, 0.3f));
        root.AddChild(title);

        string slotLabel = $"Connected slot: {ArchipelagoClient.PlayerName}  "
            + $"(team {MultiplayerSupport.PreparedApTeamId}, slot {MultiplayerSupport.PreparedApSlotId})";
        root.AddChild(CreateLabel(slotLabel, 19, HorizontalAlignment.Center));

        Button startNew = CreateButton("Start New Campaign", primary: true);
        startNew.Pressed += StartNewCampaign;
        root.AddChild(startNew);
        _defaultFocusedControl = startNew;

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(list);

        IReadOnlyList<ApMultiplayerCampaignStore.CampaignEntry> entries =
            ApMultiplayerCampaignStore.ListCampaigns();
        AddActiveSection(list, entries);
        AddHistorySection(list, entries);
        AddOtherSlotsSection(list, entries);
        AddCorruptSection(list, entries);

        Button cancel = CreateButton("Cancel");
        cancel.Pressed += Close;
        root.AddChild(cancel);
    }

    private void AddActiveSection(
        VBoxContainer list,
        IReadOnlyList<ApMultiplayerCampaignStore.CampaignEntry> entries)
    {
        list.AddChild(CreateSectionLabel("Active Campaigns"));
        ApMultiplayerCampaignStore.CampaignMetadata[] active = entries
            .Where(entry => entry.IsUsable && entry.Metadata != null)
            .Select(entry => entry.Metadata!)
            .Where(metadata =>
                metadata.Status == ApMultiplayerCampaignStore.CampaignStatus.Active
                && ApMultiplayerCampaignStore.IsCurrentApIdentity(metadata))
            .ToArray();
        if (active.Length == 0)
        {
            list.AddChild(CreateLabel("No resumable campaigns for this AP slot.", 18));
            return;
        }

        foreach (ApMultiplayerCampaignStore.CampaignMetadata campaign in active)
        {
            list.AddChild(CreateLabel(FormatCampaignSummary(campaign), 19));
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(CreateResumeButton(campaign, ApMultiplayerCampaignStore.SaveKind.FloorRecovery));
            row.AddChild(CreateResumeButton(campaign, ApMultiplayerCampaignStore.SaveKind.ApCheckpoint));
            Button abandon = CreateButton("Abandon");
            abandon.Pressed += () => ShowAbandonChoices(campaign);
            row.AddChild(abandon);
            list.AddChild(row);
        }
    }

    private Button CreateResumeButton(
        ApMultiplayerCampaignStore.CampaignMetadata campaign,
        ApMultiplayerCampaignStore.SaveKind kind)
    {
        bool recovery = kind == ApMultiplayerCampaignStore.SaveKind.FloorRecovery;
        string label = recovery ? "Floor Recovery" : "AP Checkpoint";
        var snapshot = ApMultiplayerCampaignStore.GetSnapshot(campaign, kind);
        string? error = ApMultiplayerCampaignStore.GetSnapshotError(campaign, kind);
        Button button = CreateButton(snapshot == null ? label :
            $"{label} — Act {snapshot.Act}, Floor {snapshot.CompletedFloorCount}", primary: recovery);
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        button.Disabled = error != null;
        button.TooltipText = error ?? $"Saved: {snapshot!.SavedAtUtc.ToLocalTime():g}\n"
            + (recovery ? "Latest native floor save." : "Last eligible AP checkpoint; later floor saves do not replace it.");
        button.Pressed += () => ContinueCampaign(campaign, kind);
        return button;
    }

    private void AddHistorySection(
        VBoxContainer list,
        IReadOnlyList<ApMultiplayerCampaignStore.CampaignEntry> entries)
    {
        list.AddChild(CreateSectionLabel("History"));
        ApMultiplayerCampaignStore.CampaignMetadata[] history = entries
            .Where(entry => entry.IsUsable && entry.Metadata != null)
            .Select(entry => entry.Metadata!)
            .Where(metadata =>
                metadata.Status != ApMultiplayerCampaignStore.CampaignStatus.Active
                && ApMultiplayerCampaignStore.IsCurrentApIdentity(metadata))
            .ToArray();
        if (history.Length == 0)
        {
            list.AddChild(CreateLabel("No completed or archived campaigns.", 18));
            return;
        }

        foreach (ApMultiplayerCampaignStore.CampaignMetadata campaign in history)
        {
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            Button details = CreateButton(
                $"{campaign.Status} — {FormatCampaignSummary(campaign)}"
            );
            details.TooltipText = FormatCampaignDetails(campaign);
            details.Disabled = true;
            details.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(details);
            Button delete = CreateButton("Delete Permanently", danger: true);
            delete.Pressed += () => ShowDeleteChoice(campaign);
            row.AddChild(delete);
            list.AddChild(row);
        }
    }

    private void AddOtherSlotsSection(
        VBoxContainer list,
        IReadOnlyList<ApMultiplayerCampaignStore.CampaignEntry> entries)
    {
        ApMultiplayerCampaignStore.CampaignMetadata[] other = entries
            .Where(entry => entry.Metadata != null)
            .Select(entry => entry.Metadata!)
            .Where(metadata => !ApMultiplayerCampaignStore.IsCurrentApIdentity(metadata))
            .ToArray();
        if (other.Length == 0)
            return;

        list.AddChild(CreateSectionLabel("Other AP Slots (unavailable)"));
        foreach (ApMultiplayerCampaignStore.CampaignMetadata campaign in other)
        {
            Button button = CreateButton(
                $"{campaign.ApSlotName} — {campaign.HostCharacterId} — "
                    + $"Act {campaign.Act}, Floor {campaign.CompletedFloorCount}"
            );
            button.Disabled = true;
            list.AddChild(button);
        }
    }

    private static void AddCorruptSection(
        VBoxContainer list,
        IReadOnlyList<ApMultiplayerCampaignStore.CampaignEntry> entries)
    {
        ApMultiplayerCampaignStore.CampaignEntry[] corrupt = entries
            .Where(entry => !entry.IsUsable)
            .ToArray();
        if (corrupt.Length == 0)
            return;

        list.AddChild(CreateSectionLabel("Unavailable Campaigns"));
        foreach (ApMultiplayerCampaignStore.CampaignEntry entry in corrupt)
        {
            Button button = CreateButton(
                $"{entry.Metadata?.HostCharacterId ?? entry.CampaignId} — {entry.Error}"
            );
            button.Disabled = true;
            list.AddChild(button);
        }
    }

    private void StartNewCampaign()
    {
        ApMultiplayerCampaignStore.BeginNewCampaign();
        Close();
        ApMultiplayerCampaignFlow.ResumeNewCampaign(_hostSubmenu, _gameMode);
    }

    private void ContinueCampaign(
        ApMultiplayerCampaignStore.CampaignMetadata campaign,
        ApMultiplayerCampaignStore.SaveKind kind)
    {
        try
        {
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

    private void ShowAbandonChoices(ApMultiplayerCampaignStore.CampaignMetadata campaign)
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

    private void ShowDeleteChoice(ApMultiplayerCampaignStore.CampaignMetadata campaign)
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
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(700f, 300f) };
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.OffsetLeft = -350f;
        panel.OffsetTop = -150f;
        panel.OffsetRight = 350f;
        panel.OffsetBottom = 150f;
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
        overlay.AddChild(panel);
        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.AddThemeConstantOverride("separation", 12);
        panel.AddChild(root);
        root.AddChild(CreateLabel(title, 28, HorizontalAlignment.Center));
        root.AddChild(CreateLabel(body, 19, HorizontalAlignment.Center));
        foreach ((string label, bool danger, Action action) in actions)
        {
            Button button = CreateButton(label, danger: danger);
            button.Pressed += () =>
            {
                overlay.QueueFree();
                action();
            };
            root.AddChild(button);
        }
        Button cancel = CreateButton("Cancel");
        cancel.Pressed += overlay.QueueFree;
        root.AddChild(cancel);
    }

    private void Refresh()
    {
        Close();
        Show(_hostSubmenu, _gameMode);
    }

    private static string FormatCampaignSummary(
        ApMultiplayerCampaignStore.CampaignMetadata campaign)
    {
        string lineup = string.Join(" + ", campaign.Roster
            .OrderBy(player => player.NetId == campaign.HostNetId ? 0 : 1)
            .ThenBy(player => player.NetId)
            .Select(player => player.CharacterId));
        string players = campaign.Roster.Count == 1
            ? "1 player"
            : $"{campaign.Roster.Count} players";
        return $"{lineup} — Act {campaign.Act}, "
            + $"Floor {campaign.CompletedFloorCount} — {players}";
    }

    private static string FormatCampaignDetails(
        ApMultiplayerCampaignStore.CampaignMetadata campaign)
    {
        string roster = string.Join(", ", campaign.Roster.Select(player =>
            string.IsNullOrWhiteSpace(player.DisplayName)
                ? player.CharacterId
                : $"{player.DisplayName} ({player.CharacterId})"));
        return $"Players: {roster}\nLast saved: {campaign.LastSavedAtUtc.ToLocalTime():g}";
    }

    private static Label CreateSectionLabel(string text)
    {
        Label label = CreateLabel(text, 22);
        label.AddThemeColorOverride("font_color", new Color(0.96f, 0.78f, 0.3f));
        return label;
    }

    private static Label CreateLabel(
        string text,
        int fontSize,
        HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = alignment,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color(0.98f, 0.94f, 0.84f));
        return label;
    }

    private static Button CreateButton(
        string text,
        bool primary = false,
        bool danger = false)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(190f, 48f),
            FocusMode = FocusModeEnum.All,
        };
        Color normal = danger
            ? new Color(0.46f, 0.16f, 0.1f)
            : primary
                ? new Color(0.13f, 0.48f, 0.53f)
                : new Color(0.29f, 0.16f, 0.09f);
        button.AddThemeStyleboxOverride("normal", CreateButtonStyle(normal));
        button.AddThemeStyleboxOverride("hover", CreateButtonStyle(normal.Lightened(0.12f)));
        button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(normal.Darkened(0.12f)));
        button.AddThemeStyleboxOverride("disabled", CreateButtonStyle(new Color(0.08f, 0.08f, 0.08f, 0.7f)));
        button.AddThemeFontSizeOverride("font_size", 19);
        button.AddThemeColorOverride("font_color", new Color(0.98f, 0.94f, 0.84f));
        return button;
    }

    private static StyleBoxFlat CreatePanelStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.31f, 0.22f, 0.15f, 0.98f),
            BorderColor = new Color(0.17f, 0.1f, 0.06f),
            ShadowColor = new Color(0f, 0f, 0f, 0.65f),
            ShadowSize = 20,
        };
        style.SetBorderWidthAll(3);
        style.SetCornerRadiusAll(12);
        style.SetContentMarginAll(20f);
        return style;
    }

    private static StyleBoxFlat CreateButtonStyle(Color color)
    {
        var style = new StyleBoxFlat
        {
            BgColor = color,
            BorderColor = new Color(0.17f, 0.1f, 0.06f),
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(10);
        style.SetContentMarginAll(9f);
        return style;
    }

    private static PlatformType GetVanillaPlatform() =>
        SteamInitializer.Initialized && !CommandLineHelper.HasArg("fastmp")
            ? (PlatformType)1
            : (PlatformType)0;

    private static void Close() => NModalContainer.Instance?.Clear();
}
