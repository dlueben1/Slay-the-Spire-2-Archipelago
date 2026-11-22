using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils.Combat;

namespace StS2AP.Patches;

/// <summary>
/// Hidden Relics also makes relics unknown until a Relic Sight arrives: their name, text, keyword
/// tooltips and art are replaced everywhere, owned or offered. Every screen reads these from the
/// relic model, so patching the model's getters covers the top bar, rewards, chests, shops, Ancient
/// options, the inspect screen and teammates' relics at once. The mod's own relics stay known,
/// because they show AP state such as banked relic rewards.
/// </summary>
public static class Patches_UnknownRelics
{
    private static readonly string UnknownIconPath = ImageHelper.GetImagePath("packed/common_ui/locked_model.png");

    private static Texture2D UnknownIcon => ResourceLoader.Load<Texture2D>(UnknownIconPath, null, ResourceLoader.CacheMode.Reuse);

    private static LocString UnknownTitle => new("gameplay_ui", "AP_RELIC_SIGHT_UNKNOWN.title");
    private static LocString UnknownDescription => new("gameplay_ui", "AP_RELIC_SIGHT_UNKNOWN.description");

    /// <summary>
    /// Whether a relic shows as unknown. Only during a run, so the Compendium and run history stay
    /// as they are.
    /// </summary>
    internal static bool Hides(RelicModel relic) =>
        RunManager.Instance.IsInProgress
        && RelicSight.IsHidden
        && relic.GetType().Assembly != typeof(Patches_UnknownRelics).Assembly;

    /// <summary>Sea Glass and Wuppy override Title, so their getters are patched as well.</summary>
    [HarmonyPatch]
    public static class HideTitle
    {
        [HarmonyTargetMethods]
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
        [
            AccessTools.PropertyGetter(typeof(RelicModel), nameof(RelicModel.Title)),
            AccessTools.PropertyGetter(typeof(SeaGlass), nameof(SeaGlass.Title)),
#if STS2_AT_LEAST_0_113_0
            AccessTools.PropertyGetter(typeof(Wuppy), nameof(Wuppy.Title)),
#endif
        ];

        [HarmonyPostfix]
        public static void Postfix(RelicModel __instance, ref LocString __result)
        {
            if (Hides(__instance))
                __result = UnknownTitle;
        }
    }

    [HarmonyPatch(typeof(RelicModel), nameof(RelicModel.DynamicDescription), MethodType.Getter)]
    public static class HideDescription
    {
        [HarmonyPostfix]
        public static void Postfix(RelicModel __instance, ref LocString __result)
        {
            if (Hides(__instance))
                __result = UnknownDescription;
        }
    }

    /// <summary>
    /// The relic's own tooltip. Rebuilt without the canonical model, which also keeps the game from
    /// marking the relic as seen in the Compendium, and without the "used up" line.
    /// </summary>
    [HarmonyPatch(typeof(RelicModel), nameof(RelicModel.HoverTip), MethodType.Getter)]
    public static class HideHoverTip
    {
        [HarmonyPostfix]
        public static void Postfix(RelicModel __instance, ref HoverTip __result)
        {
            if (Hides(__instance))
                __result = new HoverTip(UnknownTitle, UnknownDescription);
        }
    }

    /// <summary>Keyword tooltips (Strength, Vigor and so on) would hint at what the relic does.</summary>
    [HarmonyPatch(typeof(RelicModel), nameof(RelicModel.HoverTips), MethodType.Getter)]
    public static class HideKeywordTips
    {
        [HarmonyPostfix]
        public static void Postfix(RelicModel __instance, ref IEnumerable<IHoverTip> __result)
        {
            if (Hides(__instance))
                __result = [__instance.HoverTip];
        }
    }

    /// <summary>The inspect screen lists the keyword tooltips through this getter.</summary>
    [HarmonyPatch(typeof(RelicModel), nameof(RelicModel.HoverTipsExcludingRelic), MethodType.Getter)]
    public static class HideInspectKeywordTips
    {
        [HarmonyPostfix]
        public static void Postfix(RelicModel __instance, ref IEnumerable<IHoverTip> __result)
        {
            if (Hides(__instance))
                __result = [];
        }
    }

    /// <summary>
    /// Both icon sizes become the Compendium's locked-relic icon. A dark silhouette, the Compendium's
    /// look for unseen relics, would still give a relic away by its shape.
    /// </summary>
    [HarmonyPatch]
    public static class HideIcon
    {
        [HarmonyTargetMethods]
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
        [
            AccessTools.PropertyGetter(typeof(RelicModel), nameof(RelicModel.Icon)),
            AccessTools.PropertyGetter(typeof(RelicModel), nameof(RelicModel.BigIcon)),
        ];

        [HarmonyPostfix]
        public static void Postfix(RelicModel __instance, ref Texture2D __result)
        {
            if (Hides(__instance))
                __result = UnknownIcon;
        }
    }

    /// <summary>
    /// The relic node also sets its textures itself. The icon getters are small enough to be
    /// inlined, and in game the top bar kept real icons, so NRelic.Reload is patched directly
    /// rather than trusting the getter patches to reach it.
    /// </summary>
    [HarmonyPatch(typeof(NRelic), nameof(NRelic.Reload))]
    public static class HideNodeIcon
    {
        [HarmonyPostfix]
        public static void Postfix(NRelic __instance)
        {
            if (!__instance.IsNodeReady() || __instance._model is not { } relic || !Hides(relic))
                return;
            __instance.Icon.Texture = UnknownIcon;
            __instance.Outline.Texture = null;
        }
    }

    /// <summary>Ancient options draw their relic's icon and outline straight from the model.</summary>
    [HarmonyPatch(typeof(NEventOptionButton), nameof(NEventOptionButton._Ready))]
    public static class HideAncientOptionIcon
    {
        [HarmonyPostfix]
        public static void Postfix(NEventOptionButton __instance)
        {
            if (__instance.Event is not AncientEventModel || __instance.Option?.Relic is not { } relic || !Hides(relic))
                return;
            TextureRect icon = __instance.GetNode<TextureRect>("%RelicIcon");
            icon.SetTexture(UnknownIcon);
            icon.GetNode<TextureRect>("%Outline").SetTexture(null);
        }
    }

    /// <summary>
    /// The inspect screen also shows the relic's rarity, as a label and as its frame colour, and
    /// its flavor text. These are blanked the way the screen blanks relics you haven't discovered.
    /// </summary>
    [HarmonyPatch(typeof(NInspectRelicScreen), nameof(NInspectRelicScreen.UpdateRelicDisplay))]
    public static class HideInspectRarityAndFlavor
    {
        [HarmonyPostfix]
        public static void Postfix(NInspectRelicScreen __instance)
        {
            if (!Hides(__instance._relics[__instance._index]))
                return;
            __instance._rarityLabel.SetTextAutoSize(string.Empty);
            __instance._flavor.Text = string.Empty;
            __instance.SetRarityVisuals(RelicRarity.Common);
        }
    }

    /// <summary>The outline follows the relic's shape, so unknown relics draw none.</summary>
    [HarmonyPatch(typeof(RelicModel), nameof(RelicModel.IconOutline), MethodType.Getter)]
    public static class HideOutline
    {
        [HarmonyPostfix]
        public static void Postfix(RelicModel __instance, ref Texture2D? __result)
        {
            if (Hides(__instance))
                __result = null;
        }
    }
}
