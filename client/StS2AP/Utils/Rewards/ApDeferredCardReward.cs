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

    protected abstract Task PrepareCards();

    // Multiplayer may bind an already-reserved first choice before entering the native picker.
    protected virtual Task<bool> SelectCards() => base.OnSelect();

    protected override async Task<bool> OnSelect()
    {
        await PrepareCards();
        _canPopulate = true;
        return await SelectCards();
    }
}
