using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rewards;
using System.Runtime.CompilerServices;
using StS2AP.UI;
using StS2AP.Utils;

namespace StS2AP.Patches
{
    /// <summary>
    /// Keeps MegaCrit's native reward controls while restoring the compact AP row spacing,
    /// two-line origin label, and Ancient tint used by the previous reward catalog.
    /// </summary>
    [HarmonyPatch(typeof(NRewardButton), nameof(NRewardButton._Ready))]
    public static class StyleNativeApRewardButton
    {
        private const int RewardFontSize = 24;

        // SelfModulate remains in effect while MegaCrit's native HSV material supplies the
        // focus/press brightness animation, keeping Ancient rows dark and visibly distinct.
        private static readonly Color AncientBackgroundTint = new(0.52f, 0.27f, 0.66f);

        [HarmonyPostfix]
        public static void Postfix(NRewardButton __instance)
        {
            if (__instance.Reward is not ApMirroredRewardDispatcher.IApNativeReward reward)
                return;

            if (reward.HasOriginText)
            {
                __instance.CustomMinimumSize = new Vector2(
                    __instance.CustomMinimumSize.X,
                    Mathf.Max(__instance.CustomMinimumSize.Y, 74f)
                );

                // Match the old AP catalog's deterministic text layout. Native auto-sizing can
                // choose a different size for each transient row rectangle; AP instead keeps a
                // fixed base size, wraps naturally, and lets FitContent increase the row height.
                if (__instance.GetNodeOrNull<MegaRichTextLabel>("%Label") is { } label)
                {
                    label.AutoSizeEnabled = false;
                    label.FitContent = true;
                    foreach (StringName fontSizeProperty in
                             ThemeConstants.RichTextLabel.AllFontSizes)
                    {
                        label.RemoveThemeFontSizeOverride(fontSizeProperty);
                        label.AddThemeFontSizeOverride(fontSizeProperty, RewardFontSize);
                    }
                }
            }

            if (reward.UseAncientStyle
                && __instance.GetNodeOrNull<TextureRect>("%Background") is { } background)
            {
                background.SelfModulate = AncientBackgroundTint;
            }
        }
    }

    /// <summary>Uses the previous AP catalog's compact gap on the native reward container.</summary>
    [HarmonyPatch(typeof(NRewardsScreen), nameof(NRewardsScreen._Ready))]
    public static class SpaceNativeApRewardRows
    {
        [HarmonyPostfix]
        public static void Postfix(NRewardsScreen __instance, RewardsSet ____rewardsSet)
        {
            if (!ArchipelagoRewardUI.IsApRewardSet(____rewardsSet))
                return;
            if (__instance.GetNodeOrNull<BoxContainer>("%RewardsContainer") is { } container)
                container.AddThemeConstantOverride("separation", 10);
        }
    }

    /// <summary>
    /// Rejects an AP-owned native row before MegaCrit emits RewardSelectedMessage. This keeps
    /// multiplayer combat and other expected claim gates as true non-selections.
    /// </summary>
    [HarmonyPatch(typeof(NRewardButton), "OnRelease")]
    public static class GateNativeApRewardSelection
    {
        [HarmonyPrefix]
        public static bool Prefix(NRewardButton __instance) =>
            ArchipelagoRewardUI.CanSelectNativeReward(__instance);
    }

    /// <summary>
    /// Vanilla removes an empty nonterminal reward screen immediately. AP intentionally exposes
    /// an empty persistent inventory, so retain only AP-owned screens that started empty.
    /// </summary>
    [HarmonyPatch(typeof(NRewardsScreen), "UpdateScreenState")]
    public static class KeepEmptyApRewardScreenOpen
    {
        [HarmonyPrefix]
        public static bool Prefix(RewardsSet ____rewardsSet) =>
            !ArchipelagoRewardUI.ShouldKeepEmptyScreenOpen(____rewardsSet);

        [HarmonyPostfix]
        public static void Postfix(NRewardsScreen __instance, RewardsSet ____rewardsSet)
        {
            if (ArchipelagoRewardUI.IsApRewardSet(____rewardsSet))
                ApLinkedRewardControllerFocus.RebuildFocusGraph(__instance);
        }
    }

    /// <summary>
    /// NLinkedRewardSet connects the one-argument NRewardButton.RewardClaimed signal to a
    /// zero-argument callback. Replace that callback only for AP-owned linked rewards with an
    /// argument-compatible equivalent.
    /// </summary>
    [HarmonyPatch(typeof(NLinkedRewardSet), nameof(NLinkedRewardSet._Ready))]
    public static class FixApLinkedRewardClaimCallback
    {
        [HarmonyPostfix]
        public static void Postfix(NLinkedRewardSet __instance) =>
            ApLinkedRewardControllerFocus.ReplaceClaimCallback(__instance);
    }

    /// <summary>
    /// MegaCrit puts an NLinkedRewardSet container in the reward screen's focus list even though
    /// its selectable controls are nested NRewardButtons. Return a real child button whenever the
    /// native default would otherwise point at that non-focusable container.
    /// </summary>
    [HarmonyPatch(
        typeof(NRewardsScreen),
        nameof(NRewardsScreen.DefaultFocusedControl),
        MethodType.Getter
    )]
    public static class FocusApLinkedRewardChildByDefault
    {
        [HarmonyPostfix]
        public static void Postfix(
            NRewardsScreen __instance,
            RewardsSet ____rewardsSet,
            ref Control __result)
        {
            if (ArchipelagoRewardUI.IsApRewardSet(____rewardsSet))
                __result = ApLinkedRewardControllerFocus.ResolvePreferredFocus(__instance, __result);
        }
    }

    /// <summary>Restores the last Ancient choice when controller focus returns from the top bar.</summary>
    [HarmonyPatch(
        typeof(NRewardsScreen),
        nameof(NRewardsScreen.FocusedControlFromTopBar),
        MethodType.Getter
    )]
    public static class FocusApLinkedRewardChildFromTopBar
    {
        [HarmonyPostfix]
        public static void Postfix(
            NRewardsScreen __instance,
            RewardsSet ____rewardsSet,
            ref Control __result)
        {
            if (ArchipelagoRewardUI.IsApRewardSet(____rewardsSet))
                __result = ApLinkedRewardControllerFocus.ResolvePreferredFocus(__instance, __result);
        }
    }

    /// <summary>
    /// Native scrolling only recognizes controls stored directly in NRewardsScreen._rewardButtons.
    /// Linked Ancient choices are grandchildren, so extend focus-following behavior to those
    /// AP-owned child buttons.
    /// </summary>
    [HarmonyPatch(typeof(NRewardsScreen), "ProcessGuiFocus")]
    public static class ScrollToFocusedApLinkedRewardChild
    {
        [HarmonyPostfix]
        public static void Postfix(
            NRewardsScreen __instance,
            Control focusedControl,
            RewardsSet ____rewardsSet,
            Control ____rewardContainerMask,
            Control ____rewardsContainer,
            ref Vector2 ____targetDragPos)
        {
            if (!ArchipelagoRewardUI.IsApRewardSet(____rewardsSet))
                return;

            ApLinkedRewardControllerFocus.ProcessFocus(
                __instance,
                focusedControl,
                ____rewardContainerMask,
                ____rewardsContainer,
                ref ____targetDragPos
            );
        }
    }

    /// <summary>
    /// Adds the selectable children of AP Ancient linked rewards to MegaCrit's vertical focus
    /// graph without replacing Godot's directional-navigation input handling.
    /// </summary>
    internal static class ApLinkedRewardControllerFocus
    {
        private sealed class FocusState
        {
            public WeakReference<Control>? LastLinkedChild { get; set; }
        }

        private static readonly ConditionalWeakTable<NRewardsScreen, FocusState> States = new();

        internal static void ReplaceClaimCallback(NLinkedRewardSet linkedSet)
        {
            List<NRewardButton> children = GetApLinkedChildren(linkedSet);
            if (children.Count == 0)
                return;

            foreach (NRewardButton child in children)
            {
                // This list is a snapshot, so disconnecting while iterating it is safe.
                foreach (Godot.Collections.Dictionary connection in
                         child.GetSignalConnectionList(NRewardButton.SignalName.RewardClaimed))
                {
                    Callable callback = connection["callable"].AsCallable();
                    // Godot also reports the generated C# signal-event bridge here even though it
                    // is not a disconnectable native connection. Only remove callbacks which the
                    // signal registry can resolve; otherwise Disconnect logs an engine error.
                    if (child.IsConnected(NRewardButton.SignalName.RewardClaimed, callback))
                        child.Disconnect(NRewardButton.SignalName.RewardClaimed, callback);
                }

                child.Connect(
                    NRewardButton.SignalName.RewardClaimed,
                    Callable.From<NRewardButton>(_ => CollectLinkedReward(linkedSet))
                );
            }
        }

        internal static void RebuildFocusGraph(NRewardsScreen screen)
        {
            List<Control> controls = GetFlattenedControls(screen);
            for (int i = 0; i < controls.Count; i++)
            {
                Control control = controls[i];
                NodePath ownPath = control.GetPath();
                control.FocusNeighborLeft = ownPath;
                control.FocusNeighborRight = ownPath;
                control.FocusNeighborTop = i > 0 ? controls[i - 1].GetPath() : ownPath;
                control.FocusNeighborBottom = i < controls.Count - 1
                    ? controls[i + 1].GetPath()
                    : ownPath;
            }
        }

        internal static Control ResolvePreferredFocus(NRewardsScreen screen, Control nativeFocus)
        {
            FocusState state = States.GetOrCreateValue(screen);
            if (state.LastLinkedChild?.TryGetTarget(out Control? previous) == true
                && IsUsable(previous)
                && IsNestedApRewardButton(previous))
            {
                return previous;
            }

            if (nativeFocus is not NLinkedRewardSet linkedSet)
                return nativeFocus;

            return GetApLinkedChildren(linkedSet).FirstOrDefault() ?? nativeFocus;
        }

        internal static void ProcessFocus(
            NRewardsScreen screen,
            Control focusedControl,
            Control rewardContainerMask,
            Control rewardsContainer,
            ref Vector2 targetDragPos)
        {
            FocusState state = States.GetOrCreateValue(screen);
            if (!IsNestedApRewardButton(focusedControl))
            {
                if (focusedControl is NRewardButton directButton
                    && directButton.Reward is ApMirroredRewardDispatcher.IApNativeReward
                    && ReferenceEquals(directButton.GetParent(), rewardsContainer))
                {
                    state.LastLinkedChild = null;
                }
                return;
            }

            state.LastLinkedChild = new WeakReference<Control>(focusedControl);

            const float topLimit = 35f;
            const float visibleRewardHeight = 400f;
            if (!screen.IsVisibleInTree() || rewardsContainer.Size.Y < visibleRewardHeight)
                return;

            float positionInRewards = focusedControl.GlobalPosition.Y - rewardsContainer.GlobalPosition.Y;
            float bottomLimit = topLimit - rewardsContainer.Size.Y + visibleRewardHeight;
            float targetY = -positionInRewards + rewardContainerMask.Size.Y * 0.5f;
            targetDragPos.Y = Mathf.Clamp(targetY, bottomLimit, topLimit);
        }

        private static List<Control> GetFlattenedControls(NRewardsScreen screen)
        {
            var controls = new List<Control>();
            Control? container = screen.GetNodeOrNull<Control>("%RewardsContainer");
            if (container == null)
                return controls;

            foreach (Control control in container.GetChildren().OfType<Control>())
            {
                if (control is NLinkedRewardSet linkedSet)
                {
                    List<NRewardButton> children = GetApLinkedChildren(linkedSet);
                    if (children.Count > 0)
                    {
                        linkedSet.FocusMode = Control.FocusModeEnum.None;
                        controls.AddRange(children);
                        continue;
                    }
                }

                controls.Add(control);
            }

            return controls;
        }

        private static bool IsNestedApRewardButton(Control control) =>
            control is NRewardButton button
            && button.Reward is ApMirroredRewardDispatcher.IApNativeReward
            && FindLinkedParent(button) is { } linkedSet
            && GetApLinkedChildren(linkedSet).Contains(button);

        private static List<NRewardButton> GetApLinkedChildren(NLinkedRewardSet linkedSet)
        {
            Control? container = linkedSet.GetNodeOrNull<Control>("%RewardContainer");
            if (container == null)
                return new List<NRewardButton>();

            return container.GetChildren()
                .OfType<NRewardButton>()
                .Where(button => button.Reward is ApMirroredRewardDispatcher.IApNativeReward)
                .ToList();
        }

        private static NLinkedRewardSet? FindLinkedParent(Node node)
        {
            Node? current = node.GetParent();
            while (current != null)
            {
                if (current is NLinkedRewardSet linkedSet)
                    return linkedSet;
                current = current.GetParent();
            }

            return null;
        }

        private static void CollectLinkedReward(NLinkedRewardSet linkedSet)
        {
            if (!GodotObject.IsInstanceValid(linkedSet)
                || linkedSet.IsQueuedForDeletion()
                || FindRewardsScreen(linkedSet) is not { } screen)
            {
                return;
            }

            linkedSet.LinkedRewardSet.OnSkipped();
            // NRewardsScreen already listens to this signal and removes the linked row. Calling
            // RewardCollectedFrom directly as well removes it twice and leaves the second call
            // dereferencing a null parent in NRewardsScreen.RemoveButton.
            linkedSet.EmitSignal(NLinkedRewardSet.SignalName.RewardClaimed, linkedSet);
            linkedSet.QueueFreeSafely();

            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(screen)
                    && screen.IsInsideTree()
                    && !screen.IsQueuedForDeletion())
                {
                    screen.DefaultFocusedControl.GrabFocus();
                }
            }).CallDeferred();
        }

        private static NRewardsScreen? FindRewardsScreen(Node node)
        {
            Node? current = node.GetParent();
            while (current != null)
            {
                if (current is NRewardsScreen screen)
                    return screen;
                current = current.GetParent();
            }

            return null;
        }

        private static bool IsUsable(Control control) =>
            GodotObject.IsInstanceValid(control)
            && control.IsInsideTree()
            && control.IsVisibleInTree()
            && !control.IsQueuedForDeletion();
    }

    /// <summary>
    /// Empty and vanilla-guest AP screens have no live synchronized set to skip. Their native
    /// proceed button is presentation-only and simply closes the overlay.
    /// </summary>
    [HarmonyPatch(typeof(NRewardsScreen), "OnProceedButtonPressed")]
    public static class CloseUnsynchronizedApRewardScreen
    {
        [HarmonyPrefix]
        public static bool Prefix(NRewardsScreen __instance, RewardsSet ____rewardsSet)
        {
            if (!ArchipelagoRewardUI.ShouldHandleProceedWithoutNativeSkip(____rewardsSet))
                return true;
            ArchipelagoRewardUI.CloseWithoutNativeSkip(__instance);
            return false;
        }
    }

    /// <summary>Releases AP lifecycle state whenever the native overlay finishes closing.</summary>
    [HarmonyPatch(typeof(NRewardsScreen), nameof(NRewardsScreen.AfterOverlayClosed))]
    public static class FinishNativeApRewardScreen
    {
        [HarmonyPostfix]
        public static void Postfix(NRewardsScreen __instance, RewardsSet ____rewardsSet) =>
            ArchipelagoRewardUI.NotifyNativeScreenClosed(__instance, ____rewardsSet);
    }

    /// <summary>
    /// AP registers its own map hotkey while the native reward screen is active. A direct top-bar
    /// click calls Open with isOpenedFromTopBar=true, so defer that path until AP has closed.
    /// </summary>
    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
    public static class OpenMapAfterClosingAPRewards
    {
        [HarmonyPrefix]
        public static bool Prefix(
            NMapScreen __instance,
            bool isOpenedFromTopBar,
            ref NMapScreen __result)
        {
            if (!isOpenedFromTopBar || !ArchipelagoRewardUI.IsActive)
            {
                return true;
            }

            ArchipelagoRewardUI.CloseToMap();
            __result = __instance;
            return false;
        }
    }

    /// <summary>
    /// Apply the symmetric behaviour to deck requests. AP's blocker handles the
    /// equivalent hotkey while AP owns input; this catches the direct button path.
    /// </summary>
    [HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen.ShowScreen))]
    public static class OpenDeckAfterClosingAPRewards
    {
        [HarmonyPrefix]
        public static bool Prefix(ref NDeckViewScreen? __result)
        {
            if (!ArchipelagoRewardUI.IsActive)
            {
                return true;
            }

            ArchipelagoRewardUI.CloseToDeck();
            __result = null;
            return false;
        }
    }
}
