using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Utils;

/// <summary>
/// A native card-reward row that prepares its own cards when its picker opens.
/// The derived reward owns generation, synchronization, and receipt storage.
/// </summary>
internal abstract class ApDeferredCardReward : CardReward
{
    private bool _canPopulate;

    protected ApDeferredCardReward(CardCreationOptions options, Player player)
        : base(options, 3, player)
    {
        ApCardRewardLifecycle.Freeze(this);
    }

    // A ready menu row does not imply that its hidden card choices have been generated.
    public override bool IsPopulated => true;

    public override void Populate()
    {
        // Keep native rerolls available after this picker has opened.
        if (_canPopulate)
            base.Populate();
    }

    /// <summary>
    /// For a reward that is not a menu row, such as a queued card claim's offer, whose native
    /// rerolls must regenerate from the start.
    /// </summary>
    internal void AllowPopulate() => _canPopulate = true;

    protected abstract Task PrepareCards();

    protected override async Task<bool> OnSelect()
    {
        await PrepareCards();
        _canPopulate = true;
        return await base.OnSelect();
    }
}
