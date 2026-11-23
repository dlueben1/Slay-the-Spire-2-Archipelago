using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Models;
using StS2AP.Utils.Combat;

namespace StS2AP.Patches;

/// <summary>
/// Hidden Relics makes relics unknown until a Relic Sight arrives: their name, text, keyword
/// tooltips and icon are replaced everywhere, owned or offered, though Ancient relics keep their
/// icon. Every screen reads these from the relic model, so patching the model's getters covers
/// rewards, chests, shops, events, the inspect screen and teammates' relics at once; a few nodes
/// that set textures themselves are patched too.
/// Relics still flash, glow and grey out, so the player can work them out by watching. The mod's
/// relics are hidden too, except the two that only show AP state (<see cref="ShowsApState"/>).
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
        && !ShowsApState(relic);

    /// <summary>
    /// The mod's relics that only present AP state stay known: Relic Coupons counts banked relic
    /// rewards, and AP item relics in shops name another player's item. The mod's gameplay relics
    /// (Boons, Sight bonuses) are hidden like the game's, so every relic that does something works
    /// the same way.
    /// </summary>
    internal static bool ShowsApState(RelicModel relic) => relic is RelicCoupons or ApItemRelicModel;

    /// <summary>
    /// Whether a relic's icon is replaced as well. Ancient relics keep their art: an Ancient offer
    /// has to be picked before the relic can be watched, so the icon is all a player has to go on.
    /// </summary>
    internal static bool HidesIcon(RelicModel relic) => Hides(relic) && relic.Rarity != RelicRarity.Ancient;

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

    /// <summary>Events that offer a relic describe it with the event variant of its text.</summary>
    [HarmonyPatch]
    public static class HideDescription
    {
        [HarmonyTargetMethods]
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
        [
            AccessTools.PropertyGetter(typeof(RelicModel), nameof(RelicModel.DynamicDescription)),
            AccessTools.PropertyGetter(typeof(RelicModel), nameof(RelicModel.DynamicEventDescription)),
        ];

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
            if (HidesIcon(__instance))
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
            if (!__instance.IsNodeReady() || __instance._model is not { } relic || !HidesIcon(relic))
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
            if (__instance.Event is not AncientEventModel || __instance.Option?.Relic is not { } relic || !HidesIcon(relic))
                return;
            TextureRect icon = __instance.GetNode<TextureRect>("%RelicIcon");
            icon.SetTexture(UnknownIcon);
            icon.GetNode<TextureRect>("%Outline").SetTexture(null);
        }
    }

    /// <summary>
    /// The burst over the top bar when a relic triggers or is picked up uses the relic's art as its
    /// particles. Rebuilt with the unknown icon, because the texture is set on a node made inside the
    /// method. The rest matches the game's DoFlash on both supported versions.
    /// </summary>
    [HarmonyPatch(typeof(NRelicInventoryHolder), nameof(NRelicInventoryHolder.DoFlash))]
    public static class HideTopBarFlashArt
    {
        [HarmonyPrefix]
        public static bool Prefix(NRelicInventoryHolder __instance)
        {
            if (!HidesIcon(__instance._relic.Model))
                return true;
            if (NRun.Instance is not { } run)
                return false;
            Node2D flash = PreloadManager.Cache.GetScene(NRelicInventoryHolder._flashPath)
                .Instantiate<Node2D>(PackedScene.GenEditState.Disabled);
            flash.GetNode<GpuParticles2D>("Particles").Texture = UnknownIcon;
            flash.GlobalPosition = __instance.GlobalPosition + __instance.Size * 0.5f;
            run.GlobalUi.AboveTopBarVfxContainer.AddChildSafely(flash);
            return false;
        }
    }

    /// <summary>
    /// The flash over a creature shows the relic's art three times. _Ready sets those textures before
    /// the effect's first await, so they can be replaced right after.
    /// </summary>
    [HarmonyPatch(typeof(NRelicFlashVfx), nameof(NRelicFlashVfx._Ready))]
    public static class HideCreatureFlashArt
    {
        [HarmonyPostfix]
        public static void Postfix(NRelicFlashVfx __instance)
        {
            if (__instance._relic is not { } relic || !HidesIcon(relic))
                return;
            __instance._sprite.Texture = UnknownIcon;
            __instance._sprite2.Texture = UnknownIcon;
            __instance._sprite3.Texture = UnknownIcon;
        }
    }

    /// <summary>The outline follows the relic's shape, so unknown relics draw none.</summary>
    [HarmonyPatch(typeof(RelicModel), nameof(RelicModel.IconOutline), MethodType.Getter)]
    public static class HideOutline
    {
        [HarmonyPostfix]
        public static void Postfix(RelicModel __instance, ref Texture2D? __result)
        {
            if (HidesIcon(__instance))
                __result = null;
        }
    }
}
