using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.sts2.Core.Nodes.TopBar;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// The Hidden Health challenge. Health bars stay full and show "?" instead of numbers, and the
/// top bar shows "?" for the player's HP. Block is unchanged. Known leaks left on purpose:
/// multiplayer teammates' panel bars, the low-HP screen border, bar widths that grow with max
/// HP, and rest site heal amounts.
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
}
