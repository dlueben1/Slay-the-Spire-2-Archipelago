using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>Adapts the native rest-site layout for the additional AP options.</summary>
public static class Patches_RestSiteRoom
{
    [HarmonyPatch(typeof(NRestSiteRoom), nameof(NRestSiteRoom._Ready))]
    private static class Ready
    {
        [HarmonyPrefix]
        private static void AddScrollBar(NRestSiteRoom __instance)
        {
            if (!MultiplayerSupport.ShouldRunReplicatedConstruction(
                    MultiplayerFeature.RestSites
                )
                || GameUtility.CurrentPlayer is not Player localPlayer
                || !MultiplayerLocationChecks.TryGetCheckSettings(
                    localPlayer,
                    out ArchipelagoSettings settings
                )
                || !settings.CampfireSanity)
            {
                return;
            }

            HBoxContainer choicesContainer =
                __instance.GetNode<HBoxContainer>("%ChoicesContainer");
            Control choicesScreen = __instance.GetNode<Control>("%ChoicesScreen");

            var wrapper = new ScrollContainer();
            wrapper.SetAnchorsPreset(Control.LayoutPreset.VcenterWide);
            wrapper.SetAnchorAndOffset(Side.Left, 0.5f, -__instance.Size.X / 2 + 50.0f);
            wrapper.SetAnchorAndOffset(Side.Top, 0.5f, -285.0f);
            wrapper.SetAnchorAndOffset(Side.Right, 0.5f, __instance.Size.X / 2 - 50.0f);
            wrapper.SetAnchorAndOffset(Side.Bottom, 0.5f, -50.0f);
            wrapper.GrowHorizontal = Control.GrowDirection.Both;
            wrapper.GrowVertical = Control.GrowDirection.Both;
            wrapper.MouseFilter = Control.MouseFilterEnum.Ignore;
            wrapper.CustomMinimumSize = Vector2.Zero;

            choicesContainer.SizeFlagsHorizontal =
                Control.SizeFlags.Expand | Control.SizeFlags.ShrinkCenter;
            choicesScreen.AddChild(wrapper);
            choicesContainer.Reparent(wrapper);
        }
    }
}
