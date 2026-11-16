using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;
using StS2AP.Utils.Combat;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace StS2AP.Models;

/// <summary>
/// The Health Sight bonus, a variant of Chosen Cheese: after each Elite fight, raise max HP by 2.
/// AP grants it when a run starts or loads, or when the second Health Sight arrives mid-run. As
/// with Relic Coupons, the relic localization keys must stay ARCHIPELAGO to match the ModId.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class AgedCheese : ModRelicTemplate
{
    private const decimal MaxHpPerElite = 2m;
    // Chosen Cheese's own art, at the paths RelicModel builds from its icon name.
    private const string ChosenCheeseIconName = "chosen_cheese";

    // Granted by AP, never a reward, so it has no rarity Touch of Orobas could pick up.
    public override RelicRarity Rarity => RelicRarity.None;

    public override RelicAssetProfile AssetProfile { get; } = new(
        IconPath: ImageHelper.GetImagePath($"atlases/relic_atlas.sprites/{ChosenCheeseIconName}.tres"),
        IconOutlinePath: ImageHelper.GetImagePath($"atlases/relic_outline_atlas.sprites/{ChosenCheeseIconName}.tres"),
        BigIconPath: ImageHelper.GetImagePath($"relics/{ChosenCheeseIconName}.png")
    );

    // AP grants this relic explicitly; it must never enter a natural reward pool.
#if STS2_AT_LEAST_0_113_0
    protected override bool IsAllowedInternal(IRunState runState) => false;
#else
    public override bool IsAllowed(IRunState runState) => false;
#endif

    // As Chosen Cheese, but only for Elites, including those a ? room turns into.
    public override async Task AfterCombatEnd(CombatRoom room)
    {
        if (room.RoomType != RoomType.Elite)
            return;
        Flash();
        await CreatureCmd.GainMaxHp(Owner.Creature, MaxHpPerElite);
    }

    /// <summary>
    /// Adds the relic when the player has the Health Sight bonus and lacks it. Direct inventory
    /// insertion, as Relic Coupons uses, keeps it out of the run's reward history.
    /// </summary>
    public static void EnsureOwnedIfEarned(Player? player, bool silent = false)
    {
        if (player == null || !HealthSight.HasBonus || player.GetRelic<AgedCheese>() != null)
            return;
        try
        {
            var relic = ModelDb.Relic<AgedCheese>().ToMutable();
            relic.FloorAddedToDeck = Math.Max(1, player.RunState.TotalFloor);
            player.AddRelicInternal(relic, silent: silent);
            LogUtility.Info("Added Aged Cheese for the Health Sight bonus");
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Could not add Aged Cheese for the Health Sight bonus: {ex.Message}");
        }
    }
}
