using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// The Hidden Intents challenge, like Runic Dome in the first game. Only presentation changes:
/// enemies still act on their moves, and cards that check intents still work.
/// </summary>
public static class Patches_HiddenIntents
{
    /// <summary>
    /// UpdateVisuals runs whenever an intent changes and on every combat state change, so it is
    /// the one place that keeps the icon and its damage label in step with Intent Sight.
    /// A hidden control also receives no hover, which keeps its tooltip away.
    /// </summary>
    [HarmonyPatch(typeof(NIntent), nameof(NIntent.UpdateVisuals))]
    public static class HideIntentIcon
    {
        [HarmonyPostfix]
        public static void Postfix(NIntent __instance)
        {
            IntentDetail detail = IntentSight.Detail;
            __instance.Visible = detail != IntentDetail.None;
            if (detail != IntentDetail.Full)
                __instance._valueLabel.Text = string.Empty;
        }
    }

    /// <summary>Intent tooltips keep their title but lose their numbers until full sight.</summary>
    [HarmonyPatch(typeof(AbstractIntent), nameof(AbstractIntent.GetHoverTip))]
    public static class HideIntentTooltipNumbers
    {
        [HarmonyPostfix]
        public static void Postfix(AbstractIntent __instance, ref HoverTip __result)
        {
            if (IntentSight.Detail != IntentDetail.Types)
                return;
            __result = new HoverTip(
                __instance.IntentTitle,
                new LocString("intents", "AP_INTENT_SIGHT_DETAILS_HIDDEN.description"),
                __result.Icon
            );
        }
    }

    /// <summary>
    /// Hovering an enemy lists its intent tips before its power tips. With no sight at all,
    /// drop those leading intent tips and keep the powers.
    /// </summary>
    [HarmonyPatch(typeof(Creature), nameof(Creature.HoverTips), MethodType.Getter)]
    public static class HideIntentsInCreatureTooltip
    {
        [HarmonyPostfix]
        public static void Postfix(Creature __instance, ref IEnumerable<IHoverTip> __result)
        {
            if (IntentSight.Detail != IntentDetail.None
                || !CombatManager.Instance.IsInProgress
                || !__instance.IsMonster
                || __instance.Monster is not { } monster)
                return;
            int intentTips = monster.NextMove.Intents.Count(intent => intent.HasIntentTip);
            __result = __result.Skip(intentTips).ToList();
        }
    }

    // INTENT_GRAPH: remidner for myself that this is a mod 'dependency' part
    /// <summary>
    /// The optional Intent Graph mod opens its overlay from an NCreature.OnFocus postfix. Skip its
    /// IntentGraphHost.Create until full sight; its Bestiary view is left alone.
    /// </summary>
    [HarmonyPatch(typeof(LocManager), nameof(LocManager.Initialize))]
    public static class BlockIntentGraphOverlay
    {
        private static bool _attempted;

        // Localization initializes after mod initializers on both supported game versions,
        // which also covers Intent Graph loading after Archipelago.
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (_attempted)
                return;
            _attempted = true;
            // Intent Graph is optional, so resolve its API by name without requiring its DLL.
            var host = AccessTools.TypeByName("IntentGraph2.Utils.IntentGraphHost");
            if (host is null)
                return;
            try
            {
                var create = AccessTools.Method(host, "Create", [typeof(NCreature)])
                    ?? throw new MissingMethodException(host.FullName, "Create");
                new Harmony("archipelago.patch.intentgraph.hiddenintents").Patch(create,
                    prefix: new HarmonyMethod(typeof(BlockIntentGraphOverlay), nameof(AllowOverlay)));
                LogUtility.Info("Hidden Intents can now hide the Intent Graph overlay.");
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Could not hide the optional Intent Graph overlay for Hidden Intents: {ex.Message}");
            }
        }

        private static bool AllowOverlay() => IntentSight.Detail == IntentDetail.Full;
    }
}
