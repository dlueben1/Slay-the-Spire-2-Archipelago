using Godot;
using HarmonyLib;
using MegaCrit.sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using StS2AP.Utils;

namespace StS2AP.Patches;

public static class Patches_APBossPresentation
{
    private static bool DoubleBossDisabled =>
        MultiplayerSupport.ShouldRunReplicatedConstruction(MultiplayerFeature.AscensionEffects)
        && AscensionMultiplayer.TryHasLevel(AscensionLevel.DoubleBoss, out bool enabled)
        && !enabled;

    public static void Refresh()
    {
        Callable.From(() =>
        {
            RedrawMapWithoutSecondBoss();

            var icon = NRun.Instance?.GlobalUi?.TopBar?.BossIcon;
            if (icon == null || !GodotObject.IsInstanceValid(icon))
                return;
            icon.RefreshBossIcon();
            if (DoubleBossDisabled)
            {
                NHoverTipSet.Remove(icon);
                if (icon.IsEnabled && icon.IsVisibleInTree()
                    && (icon._isHovered || icon._isControllerFocused))
                    ShowActiveBossTooltip.Prefix(icon);
            }
        }).CallDeferred();
    }

    /// <summary>
    /// The map screen builds its boss nodes only when an act starts. Without a redraw, a Double
    /// Boss removed mid-act stays drawn above the first boss, and the map even marks it
    /// travelable after the first boss is beaten.
    /// </summary>
    private static void RedrawMapWithoutSecondBoss()
    {
        var mapScreen = NMapScreen.Instance;
        if (!DoubleBossDisabled
            || mapScreen == null
            || !GodotObject.IsInstanceValid(mapScreen)
            || mapScreen._secondBossPointNode == null)
            return;

        // SetMap assigns this field only when the map has a second boss and never clears it.
        // Clear it first so the redraw links focus and travelability to the first boss alone;
        // the old node is freed along with the other map points.
        mapScreen._secondBossPointNode = null;
        var runState = mapScreen._runState;
        // The game redraws the same way when it changes the boss mid-act (MapCmd.SetBossEncounter).
        mapScreen.SetMap(runState.Map, runState.Rng.Seed, clearDrawings: false);
    }

    [HarmonyPatch(typeof(NTopBarBossIcon), nameof(NTopBarBossIcon.RefreshBossIcon))]
    public static class HideInactiveSecondBoss
    {
        [HarmonyPostfix]
        public static void Postfix(NTopBarBossIcon __instance)
        {
            // The native icon reads the encounter, whereas AP disables its map point.
            // Preserve encounter/RNG state and apply the same AP rule to presentation.
            if (!DoubleBossDisabled)
                return;
            __instance._secondBossIcon?.Hide();
            __instance._secondBossIconOutline?.Hide();
        }
    }

    [HarmonyPatch(typeof(NTopBarBossIcon), "OnFocus")]
    public static class ShowActiveBossTooltip
    {
        [HarmonyPrefix]
        public static bool Prefix(NTopBarBossIcon __instance)
        {
            if (!DoubleBossDisabled)
                return true;
            var boss = __instance._runState.Act.BossEncounter;
            var title = new LocString("static_hover_tips", "BOSS.title");
            var description = new LocString("static_hover_tips", "BOSS.description");
            title.Add("BossName", boss.Title);
            description.Add("BossName", boss.Title);
            NHoverTipSet.CreateAndShow(__instance, new HoverTip(title, description))
                ?.SetGlobalPosition(__instance._bossIcon.GlobalPosition
                    + new Vector2(0f, __instance.Size.Y + 20f));
            return false;
        }
    }
}
