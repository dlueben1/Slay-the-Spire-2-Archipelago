using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace StS2AP.Models;

/// <summary>
/// Permanent AP-run indicator for earned natural relic rewards that have not yet been paired
/// with a received AP Relic item. The authoritative value remains on ArchipelagoProgress.
/// NOTE: since this is a RitsuLib relic: the appropriate relics.json localization must stay
/// as ARCHIPELAGO to match the current ModId (even if it goes against the other localization names)
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class RelicCoupons : ModRelicTemplate
{
    private const string CouponIconPath = "res://images/APIcon.png";
    private int? _activationDisplayAmount;
    private int _activationSequence;

    // This is AP run-state presentation, not a character starter relic. Touch of Orobas
    // selects the first owned Starter-rarity relic, so classifying the coupon as Starter
    // can make Touch replace it with Circlet instead of upgrading the real starter relic.
    public override RelicRarity Rarity => RelicRarity.None;

    public override bool ShowCounter => true;

    public override int DisplayAmount =>
        _activationDisplayAmount ?? ArchipelagoClient.Progress.BankedRelicRewards;

    public override bool ShouldReceiveCombatHooks => false;

    public override RelicAssetProfile AssetProfile { get; } = new(
        IconPath: CouponIconPath,
        IconOutlinePath: CouponIconPath,
        BigIconPath: CouponIconPath
    );

    // This relic is granted explicitly to AP players and must never enter a natural reward pool.
    public override bool IsAllowed(IRunState runState) => false;

    /// <summary>
    /// Adds the counter as starting-inventory state. Direct inventory insertion is intentional at
    /// this lifecycle boundary: RelicCmd.Obtain expects a fully initialized run and would create a
    /// reward-history entry and acquisition animation for what is effectively a starting relic.
    /// </summary>
    public static void EnsureOwnedBy(Player player, bool silent = false)
    {
        if (!ArchipelagoClient.IsConnected || player.GetRelic<RelicCoupons>() != null)
            return;

        try
        {
            var relic = ModelDb.Relic<RelicCoupons>().ToMutable();
            relic.FloorAddedToDeck = 1;
            player.AddRelicInternal(relic, silent: silent);
            LogUtility.Info("Added the Relic Coupons counter to the AP run");
        }
        catch (Exception ex)
        {
            // The counter is presentation-only; coupon earning and redemption must remain usable.
            LogUtility.Warn($"Could not add the Relic Coupons counter: {ex.Message}");
        }
    }

    /// <summary>Notifies the native relic inventory after the authoritative coupon value changes.</summary>
    public static void RefreshCounter(Player? player = null)
    {
        try
        {
            (player ?? GameUtility.CurrentPlayer)
                ?.GetRelic<RelicCoupons>()
                ?.InvokeCouponCountChanged();
        }
        catch (Exception ex)
        {
            // Updating the number is best-effort and must not affect coupon ownership semantics.
            LogUtility.Warn($"Could not refresh the Relic Coupons counter: {ex.Message}");
        }
    }

    /// <summary>
    /// Uses the same native relic flash event as counting relics such as Lasting Candy. This is
    /// shown when an earned coupon is immediately paired, even if its displayed balance stays at 0.
    /// </summary>
    public static void Activate(Player? player = null)
    {
        try
        {
            var relic = (player ?? GameUtility.CurrentPlayer)?.GetRelic<RelicCoupons>();
            if (relic == null)
                return;

            TaskHelper.RunSafely(relic.DoActivateVisuals());
        }
        catch (Exception ex)
        {
            // Activation is presentation-only and must not affect coupon pairing.
            LogUtility.Warn($"Could not activate the Relic Coupons counter: {ex.Message}");
        }
    }

    private async Task DoActivateVisuals()
    {
        AssertMutable();
        var activationSequence = ++_activationSequence;

        // Pairing has already decremented the authoritative balance. Briefly show its previous
        // value so an immediate earn-and-spend visibly ticks from 1 to 0 instead of appearing idle.
        _activationDisplayAmount = ArchipelagoClient.Progress.BankedRelicRewards + 1;
        InvokeCouponCountChanged();
        Flash();

        await Cmd.Wait(1f);
        if (activationSequence != _activationSequence)
            return;

        _activationDisplayAmount = null;
        InvokeCouponCountChanged();
    }

    private void InvokeCouponCountChanged() => InvokeDisplayAmountChanged();
}
