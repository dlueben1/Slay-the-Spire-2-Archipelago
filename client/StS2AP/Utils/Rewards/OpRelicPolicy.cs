using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Utils;

/// <summary>
/// Maps the slot's remove_op_relics categories to concrete relics. Each listed relic is much
/// stronger in Archipelago than in a normal run. The lists live in the client, so they can change
/// with balance findings without regenerating a seed.
/// </summary>
public static class OpRelicPolicy
{
    // An unseeded Anytime run can be restarted for free until floor 1 offers one of these.
    // Pael's Growth then doubles a strong Clone target at every rest site, and Glitter gives Glam
    // to every AP card reward. Start-of-act Ancients keep them: rerolling there costs a whole act.
    private static readonly Type[] AnytimeAncientRelics =
    [
        typeof(PaelsGrowth),
        typeof(Glitter),
    ];

    // Their extra card rewards are added after AP turns combat card rewards into checks, so they
    // return the card supply that the randomizer withholds.
    private static readonly Type[] RareRelics =
    [
        typeof(PrayerWheel),
        typeof(WhiteStar),
    ];

    /// <summary>Relics whose availability checks enforce the Rare category.</summary>
    public static IReadOnlyList<Type> RareRelicTypes => RareRelics;

    /// <summary>
    /// Whether an Anytime Ancient reward for this player must not offer the relic. Ancient choices
    /// belong to one player, so only that player's slot settings apply.
    /// </summary>
    public static bool IsRemovedFromAnytimeAncients(
        RelicModel relic,
        Player player,
        ArchipelagoSettings? settings)
    {
        if (!IsAnyOf(relic, AnytimeAncientRelics))
            return false;

        if (settings == null && ApPlayerContextResolver.TryGetRewardSettings(player, out var resolved))
            settings = resolved;
        return settings?.RemovedOpRelics.HasFlag(OpRelicCategories.Ancient) == true;
    }

    /// <summary>
    /// Whether the run must never generate the relic. Relic pools and RNG are shared by the whole
    /// run, so in multiplayer one Archipelago player enabling the category removes the relic for
    /// everyone. Every replica reads the same replicated slot settings, so all of them filter
    /// identically.
    /// </summary>
    public static bool IsRemovedFromRun(RelicModel relic, IRunState runState)
    {
        if (!IsAnyOf(relic, RareRelics))
            return false;

        foreach (Player player in runState.Players)
        {
            if (ApPlayerContextResolver.TryGetRewardSettings(player, out var settings)
                && settings.RemovedOpRelics.HasFlag(OpRelicCategories.Rare))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsAnyOf(RelicModel relic, IEnumerable<Type> types) =>
        types.Any(type => type.IsInstanceOfType(relic));
}
