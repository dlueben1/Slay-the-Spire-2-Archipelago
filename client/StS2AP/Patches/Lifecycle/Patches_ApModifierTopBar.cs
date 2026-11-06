using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using StS2AP.Models;

namespace StS2AP.Patches
{
    /// <summary>
    /// The top bar shows one icon per run modifier, so a slot with many Run Modifiers fills it.
    /// When a run has two or more AP modifiers, their icons become a single AP icon whose hover
    /// tip lists them all. Base-game modifiers keep their own icons.
    /// </summary>
    public static class Patches_ApModifierTopBar
    {
        private const string ApIconPath = "res://images/APIcon.png";

        // The AP modifiers behind each collapsed icon.
        private static readonly ConditionalWeakTable<NTopBarModifier, IReadOnlyList<ModifierModel>> CollapsedModifiers = new();

        [HarmonyPatch(typeof(NTopBar), nameof(NTopBar.Initialize))]
        public static class CollapseApModifiers
        {
            // AddChildSafely may defer adding the modifier icons, so collapse them after any
            // deferred adds.
            [HarmonyPostfix]
            public static void Postfix(NTopBar __instance) =>
                Callable.From(() => Collapse(__instance)).CallDeferred();
        }

        // OnFocus is a protected override, which the publicizer leaves inaccessible.
        [HarmonyPatch(typeof(NTopBarModifier), "OnFocus")]
        public static class ShowCollapsedTips
        {
            [HarmonyPrefix]
            public static bool Prefix(NTopBarModifier __instance)
            {
                if (!CollapsedModifiers.TryGetValue(__instance, out IReadOnlyList<ModifierModel>? modifiers))
                    return true;

                // Built on each hover: the preload cache frees modifier icon textures when rooms
                // change, so tips kept from an earlier room would hold disposed textures.
                List<IHoverTip> tips = modifiers
                    .Select(modifier => (IHoverTip)new HoverTip(modifier.Title, modifier.Description, modifier.Icon))
                    .ToList();

                // Same placement as the base game's single-modifier tip.
                NHoverTipSet.CreateAndShow(__instance, tips)
                    ?.SetGlobalPosition(__instance.GlobalPosition + new Vector2(0f, __instance.Size.Y + 20f));
                return false;
            }
        }

        private static void Collapse(NTopBar topBar)
        {
            if (!GodotObject.IsInstanceValid(topBar))
                return;

            Control container = topBar._modifiersContainer;
            List<NTopBarModifier> apIcons = container.GetChildren()
                .OfType<NTopBarModifier>()
                .Where(icon => ApRunModifiers.IsArchipelagoModifier(icon._modifier))
                .ToList();
            if (apIcons.Count < 2)
                return;

            List<ModifierModel> apModifiers = apIcons.Select(icon => icon._modifier).ToList();
            NTopBarModifier? collapsed = NTopBarModifier.Create(apModifiers[0]);
            if (collapsed == null)
                return;

            int index = apIcons[0].GetIndex();
            foreach (NTopBarModifier icon in apIcons)
            {
                container.RemoveChild(icon);
                icon.QueueFree();
            }
            CollapsedModifiers.Add(collapsed, apModifiers);
            container.AddChild(collapsed);
            container.MoveChild(collapsed, index);
            // _Ready gave the icon the first modifier's texture.
            collapsed._icon.Texture = ResourceLoader.Load<Texture2D>(ApIconPath);

            // Initialize's deferred navigation pass already linked the removed icons.
            topBar.UpdateNavigation();
        }
    }
}
