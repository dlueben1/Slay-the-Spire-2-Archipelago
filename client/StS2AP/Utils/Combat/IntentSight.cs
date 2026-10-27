using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using StS2AP.Data;

namespace StS2AP.Utils;

/// <summary>How much of an enemy's intent the local player may see.</summary>
internal enum IntentDetail
{
    // No intent icons, intent tooltips or Intent Graph overlay.
    None = 0,
    // Intent icons and tooltip titles, without damage or other numbers.
    Types = 1,
    // Everything the game shows normally.
    Full = 2,
}

/// <summary>
/// The Hidden Intents challenge: intents stay hidden until the slot receives Progressive Intent
/// Sight items, one step per copy.
/// </summary>
internal static class IntentSight
{
    internal static IntentDetail Detail =>
        ChallengeSightItems.Applies(ArchipelagoClient.Settings?.HiddenIntents == true)
            ? (IntentDetail)Math.Min(
                ChallengeSightItems.Received(ItemTable.APItem.ProgressiveIntentSight),
                (int)IntentDetail.Full)
            : IntentDetail.Full;

    /// <summary>Applies a newly received Intent Sight to the intents already on screen.</summary>
    internal static void RefreshVisibleIntents()
    {
        if (NCombatRoom.Instance is not { } room)
            return;
        foreach (NCreature creature in room.CreatureNodes)
        {
            foreach (NIntent intent in creature.IntentContainer.GetChildren().OfType<NIntent>())
            {
                if (intent._intent != null)
                    intent.UpdateVisuals();
            }
        }
    }
}
