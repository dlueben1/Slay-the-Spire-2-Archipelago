using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Relics;
using StS2AP.Utils;

namespace StS2AP.Models;

/// <summary>
/// Grants the relics that show a Sight bonus is active, such as Aged Cheese and Frozen Eye. AP adds
/// them when a run starts or loads, or when the Sight's second copy arrives mid-run.
/// </summary>
internal static class SightBonusRelic
{
    /// <summary>
    /// Adds the relic when the player lacks it. Direct inventory insertion, as Relic Coupons uses,
    /// keeps it out of the run's reward history.
    /// </summary>
    public static void EnsureOwned<TRelic>(Player player, bool silent) where TRelic : RelicModel
    {
        if (player.GetRelic<TRelic>() != null)
            return;
        try
        {
            RelicModel relic = ModelDb.Relic<TRelic>().ToMutable();
            relic.FloorAddedToDeck = Math.Max(1, player.RunState.TotalFloor);
            player.AddRelicInternal(relic, silent: silent);
            if (!silent)
                RevealInRelicBar(relic);
            LogUtility.Info($"Added {typeof(TRelic).Name} for its Sight bonus");
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Could not add {typeof(TRelic).Name} for its Sight bonus: {ex.Message}");
        }
    }

    /// <summary>
    /// The relic bar adds a newly obtained relic hidden, and RelicCmd.Obtain then plays the pickup
    /// animation that shows it. A mid-run grant has to do that step itself, or the relic stays
    /// hidden until the bar is rebuilt. Before the bar exists, as at run start, there is nothing to do.
    /// </summary>
    private static void RevealInRelicBar(RelicModel relic)
    {
        NRelicInventory? inventory = NRun.Instance?.GlobalUi.RelicInventory;
        if (inventory != null && inventory._relicNodes.Any(node => node.Relic.Model == relic))
            inventory.AnimateRelic(relic);
    }
}
