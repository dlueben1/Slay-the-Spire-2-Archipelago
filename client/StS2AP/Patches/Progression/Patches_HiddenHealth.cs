using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.sts2.Core.Nodes.TopBar;
using StS2AP.Utils;
using StS2AP.Utils.Combat;

namespace StS2AP.Patches;

/// <summary>
/// The Hidden Health challenge. Health bars stay full and show "?" instead of numbers, and the
/// top bar and the map's visited-floor tooltips show "?" for the player's HP. Block is unchanged.
/// Known leaks left on purpose: multiplayer teammates' panel bars, the low-HP screen border, bar
/// widths that grow with max HP, rest site heal amounts, and the damage, healing and max HP lines
/// of floor tooltips.
/// </summary>
public static class Patches_HiddenHealth
{
    private const string HiddenText = "?";

    private static bool IsHiddenAlive(Creature? creature) =>
        creature is { CurrentHp: > 0 } && HealthSight.IsHidden(creature);

    /// <summary>
    /// Every bar level (creation, resizing, foreground and the delayed middleground) is measured
    /// through GetFgWidth, so reporting a full width keeps the whole bar full.
    /// </summary>
    [HarmonyPatch(typeof(NHealthBar), nameof(NHealthBar.GetFgWidth), [typeof(int), typeof(float)])]
    public static class FillBar
    {
        [HarmonyPostfix]
        public static void Postfix(NHealthBar __instance, float maxFgWidth, ref float __result)
        {
            if (IsHiddenAlive(__instance._creature))
                __result = maxFgWidth;
        }
    }

    /// <summary>
    /// Lethal poison and doom recolour the bar and its label, which would reveal low health.
    /// </summary>
    [HarmonyPatch(typeof(NHealthBar), nameof(NHealthBar.IsPoisonLethal))]
    public static class HidePoisonLethal
    {
        [HarmonyPostfix]
        public static void Postfix(NHealthBar __instance, ref bool __result)
        {
            if (IsHiddenAlive(__instance._creature))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(NHealthBar), nameof(NHealthBar.IsDoomLethal))]
    public static class HideDoomLethal
    {
        [HarmonyPostfix]
        public static void Postfix(NHealthBar __instance, ref bool __result)
        {
            if (IsHiddenAlive(__instance._creature))
                __result = false;
        }
    }

    /// <summary>The poison and doom chunks are sized by health, so hide them with it.</summary>
    [HarmonyPatch(typeof(NHealthBar), nameof(NHealthBar.RefreshForeground))]
    public static class HidePoisonAndDoom
    {
        [HarmonyPostfix]
        public static void Postfix(NHealthBar __instance)
        {
            if (!IsHiddenAlive(__instance._creature))
                return;
            __instance._poisonForeground.Visible = false;
            __instance._doomForeground.Visible = false;
            __instance._hpForeground.Visible = true;
        }
    }

    [HarmonyPatch(typeof(NHealthBar), nameof(NHealthBar.RefreshText))]
    public static class HideBarNumbers
    {
        [HarmonyPostfix]
        public static void Postfix(NHealthBar __instance)
        {
            if (IsHiddenAlive(__instance._creature) && __instance._creature!.HpDisplay.ShowsNumbers())
                __instance._hpLabel.SetTextAutoSize(HiddenText);
        }
    }

    /// <summary>
    /// The top bar writes "current/max" from UpdateHealth, from the Neow lerp before its first
    /// await, and from each step of that lerp. Overwrite all three while the player's side is
    /// hidden.
    /// </summary>
    [HarmonyPatch]
    public static class HideTopBarHp
    {
        [HarmonyTargetMethods]
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
        [
            AccessTools.Method(typeof(NTopBarHp), nameof(NTopBarHp.UpdateHealth)),
            AccessTools.Method(typeof(NTopBarHp), nameof(NTopBarHp.UpdateHpTween)),
            AccessTools.Method(typeof(NTopBarHp), nameof(NTopBarHp.LerpAtNeow)),
        ];

        [HarmonyPostfix]
        public static void Postfix(NTopBarHp __instance)
        {
            if (__instance._player?.Creature is { } creature && HealthSight.IsHidden(creature))
                __instance._hpLabel.SetTextAutoSize(HiddenText);
        }
    }

    /// <summary>
    /// Hovering a visited map node shows that floor's history, including the player's HP and max
    /// HP when they left it. Run History builds the same tooltip for finished runs, which have no
    /// run in progress, so only the current run's player is hidden.
    /// </summary>
    [HarmonyPatch(typeof(NMapPointHistoryHoverTip), nameof(NMapPointHistoryHoverTip._Ready))]
    public static class HideMapFloorHp
    {
        [HarmonyPostfix]
        public static void Postfix(NMapPointHistoryHoverTip __instance)
        {
            if (RunManager.Instance.DebugOnlyGetState() is not RunState runState
                || LocalContext.GetMe(runState) is not { } player
                || player.NetId != __instance._playerId
                || !IsHiddenAlive(player.Creature))
                return;

            LocString stats = __instance._mapPointPlayerStatsLoc;
            stats.Add("HP", HiddenText);
            stats.Add("MaxHP", HiddenText);
            __instance._playerStats.Text = stats.GetFormattedText();
        }
    }
}
