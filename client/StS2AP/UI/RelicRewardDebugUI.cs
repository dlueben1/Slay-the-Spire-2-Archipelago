using Godot;
using StS2AP.Utils;

namespace StS2AP.UI
{
    /// <summary>
    /// Opt-in developer overlay for inspecting progressive Relic receipt/bank state in real time.
    /// Toggle it from the developer console with <c>aprelicdebug</c>.
    /// </summary>
    public static class RelicRewardDebugUI
    {
        private static CanvasLayer? _canvasLayer;
        private static Label? _counterLabel;
        private static Godot.Timer? _refreshTimer;

        private const string FontPath = "res://fonts/kreon_regular.ttf";
        private const int CanvasLayerIndex = 100;
        private const int FontSize = 22;
        private const int OutlineSize = 6;
        private const double RefreshIntervalSeconds = 0.1;

        private static readonly Color TextColor = new Color(0.65f, 1.0f, 0.65f);

        public static bool IsVisible =>
            _canvasLayer != null
            && GodotObject.IsInstanceValid(_canvasLayer)
            && _canvasLayer.Visible;

        public static void Show()
        {
            try
            {
                if (_canvasLayer != null && GodotObject.IsInstanceValid(_canvasLayer))
                {
                    _canvasLayer.Visible = true;
                    Refresh();
                    return;
                }

                var sceneTree = Engine.GetMainLoop() as SceneTree;
                var root = sceneTree?.Root;
                if (root == null)
                {
                    LogUtility.Error("[RelicDebug] Could not find the scene root");
                    return;
                }

                _canvasLayer = new CanvasLayer
                {
                    Name = "APRelicRewardDebugLayer",
                    Layer = CanvasLayerIndex,
                };

                _counterLabel = new Label
                {
                    Name = "APRelicRewardDebugCounters",
                    Position = new Vector2(24f, 160f),
                    Size = new Vector2(420f, 120f),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                _counterLabel.AddThemeColorOverride("font_color", TextColor);
                _counterLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
                _counterLabel.AddThemeFontSizeOverride("font_size", FontSize);
                _counterLabel.AddThemeConstantOverride("outline_size", OutlineSize);

                try
                {
                    var font = GD.Load<Font>(FontPath);
                    if (font != null)
                        _counterLabel.AddThemeFontOverride("font", font);
                }
                catch (Exception ex)
                {
                    LogUtility.Warn($"[RelicDebug] Could not load font: {ex.Message}");
                }

                _refreshTimer = new Godot.Timer
                {
                    Name = "APRelicRewardDebugRefreshTimer",
                    WaitTime = RefreshIntervalSeconds,
                    OneShot = false,
                    Autostart = true,
                };
                _refreshTimer.Timeout += Refresh;

                _canvasLayer.AddChild(_counterLabel);
                _canvasLayer.AddChild(_refreshTimer);
                root.AddChild(_canvasLayer);
                Refresh();
            }
            catch (Exception ex)
            {
                LogUtility.Error($"[RelicDebug] Could not show overlay: {ex}");
                Hide();
            }
        }

        public static void Hide()
        {
            if (_canvasLayer != null && GodotObject.IsInstanceValid(_canvasLayer))
                _canvasLayer.QueueFree();

            _canvasLayer = null;
            _counterLabel = null;
            _refreshTimer = null;
        }

        private static void Refresh()
        {
            if (_counterLabel == null || !GodotObject.IsInstanceValid(_counterLabel))
                return;

            var player = GameUtility.CurrentPlayer;
            var receiptCount = player == null
                ? 0
                : RelicRewardUtility.CountWaitingReceiptsForNaturalReward(player);
            var receivedCount = player == null
                ? 0
                : RelicRewardUtility.CountReceivedRelics(player);
            var progress = ArchipelagoClient.Progress;

            _counterLabel.Text =
                $"Relic receipts waiting: {receiptCount}\n" +
                $"Banked relics: {progress.BankedRelicRewards}\n" +
                $"Relic reward attempts: {progress.RelicRewardsAttempted}\n" +
                $"Relics received: {receivedCount}";
        }
    }
}
