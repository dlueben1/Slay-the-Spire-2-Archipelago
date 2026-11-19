using MegaCrit.Sts2.Core.Helpers;

namespace StS2AP.Utils;

/// <summary>
/// The seed the <c>ap_seed</c> console command asked the next new run to use. It is used once:
/// the run that takes it clears it, so later runs roll their own seed again.
/// </summary>
internal static class RunSeedOverride
{
    /// <summary>The seed waiting for the next new run, or null.</summary>
    public static string? Pending { get; private set; }

    /// <summary>The seed the run now starting took from this override, or null.</summary>
    private static string? _applied;

    /// <summary>Asks the next new run to use this seed, written the way the game writes seeds.</summary>
    public static string Set(string seed)
    {
        Pending = SeedHelper.CanonicalizeSeed(seed);
        return Pending;
    }

    public static void Clear() => Pending = null;

    /// <summary>
    /// Hands the pending seed to a run that is about to start. The run's own setup may ask
    /// <see cref="TakeApplied"/> before the launch call returns, so the seed counts as applied
    /// from here on; <see cref="FinishLaunch"/> settles whether the launch went through.
    /// </summary>
    public static string? BeginLaunch()
    {
        _applied = Pending;
        return Pending;
    }

    /// <summary>Uses up the pending seed after a launch, or keeps it for the next try.</summary>
    public static void FinishLaunch(bool launched)
    {
        if (launched)
            Pending = null;
        else
            _applied = null;
    }

    /// <summary>
    /// Whether the run now starting got this seed from the override, in which case a slot's
    /// Seeded Run seed must not replace it. Asking forgets the answer, so it holds for one run.
    /// </summary>
    public static bool TakeApplied(string seed)
    {
        bool applied = _applied != null && _applied == seed;
        _applied = null;
        return applied;
    }
}
