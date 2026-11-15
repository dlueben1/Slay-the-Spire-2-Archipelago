using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;
using STS2RitsuLib.Combat.Rewards;

namespace StS2AP.Utils;

/// <summary>
/// Choose-one linked reward sets that the mod builds from rewards of any kind, including base-game
/// rewards such as card removal. The reward screen's linked-set fix
/// (<c>FixApLinkedRewardClaimCallback</c>) rewires every child of these sets, not only the AP
/// mirrored rewards it recognizes by type.
/// </summary>
internal static class ApLinkedRewardSets
{
    private static readonly ConditionalWeakTable<LinkedRewardSet, object> Owned = new();
    private static readonly object Marker = new();

    internal static LinkedRewardSet CreateChooseOne(List<Reward> rewards, Player player)
    {
        LinkedRewardSet set = LinkedRewardSets.Create(rewards, player, LinkedRewardSelectionMode.ChooseOne);
        Owned.AddOrUpdate(set, Marker);
        return set;
    }

    internal static bool IsOwned(LinkedRewardSet set) => Owned.TryGetValue(set, out _);
}
