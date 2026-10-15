using Godot;
using HarmonyLib;
using MegaCrit.sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
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
