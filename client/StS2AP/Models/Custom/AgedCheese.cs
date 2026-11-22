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
/// The Health Sight bonus, a variant of Chosen Cheese: after each Elite or boss fight, raise max HP by 3.
/// AP grants it when a run starts or loads, or when the second Health Sight arrives mid-run. As
/// with Relic Coupons, the relic localization keys must stay ARCHIPELAGO to match the ModId.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class AgedCheese : ModRelicTemplate
{
    private const decimal MaxHpPerFight = 3m;
    // Chosen Cheese's own art. Its small icons live in an atlas whose .tres paths fail RitsuLib's
    // "does this file exist" check, which then falls back to an empty icon, so all three use the
    // large picture, as Relic Coupons does with its own art.
    private static readonly string ChosenCheeseIconPath = ImageHelper.GetImagePath("relics/chosen_cheese.png");

    // Granted by AP, never a reward, so it has no rarity Touch of Orobas could pick up.
    public override RelicRarity Rarity => RelicRarity.None;

    // Keeps relic-trading events from taking it, as Relic Coupons does. A traded-away Aged Cheese
    // would come back on the next load, so the trade would be free.
    public override bool HasUponPickupEffect => true;

    public override RelicAssetProfile AssetProfile { get; } = new(
        IconPath: ChosenCheeseIconPath,
        IconOutlinePath: ChosenCheeseIconPath,
        BigIconPath: ChosenCheeseIconPath
    );

    // AP grants this relic explicitly; it must never enter a natural reward pool.
#if STS2_AT_LEAST_0_113_0
    protected override bool IsAllowedInternal(IRunState runState) => false;
#else
    public override bool IsAllowed(IRunState runState) => false;
#endif

    // As Chosen Cheese, but only for Elites (including those a ? room turns into) and bosses.
    public override async Task AfterCombatEnd(CombatRoom room)
    {
        if (room.RoomType is not (RoomType.Elite or RoomType.Boss))
            return;
        Flash();
        await CreatureCmd.GainMaxHp(Owner.Creature, MaxHpPerFight);
    }

    /// <summary>Adds the relic when the player has the Health Sight bonus and lacks it.</summary>
    public static void EnsureOwnedIfEarned(Player? player, bool silent = false)
    {
        if (player == null || !HealthSight.HasBonus)
            return;
        SightBonusRelic.EnsureOwned<AgedCheese>(player, silent);
    }
}
