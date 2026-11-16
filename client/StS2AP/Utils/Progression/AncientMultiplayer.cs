using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Extensions;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Utils;

/// <summary>
/// Supplies each player's Progressive Ancient count when MegaCrit constructs one mutable Ancient
/// event per player on every replica. MegaCrit continues to own option-index and relic
/// synchronization; this class only makes the deterministic option transform owner-correct.
/// </summary>
public static class AncientMultiplayer
{
    // Each player's Progressive Ancients for their run's character, as the current event began.
    private static readonly Dictionary<ulong, int> FrozenByPlayer = new();
    private static bool _encounterFrozen;

    public static void EndRun()
    {
        _encounterFrozen = false;
        FrozenByPlayer.Clear();
    }

    /// <summary>
    /// Freezes every player's Progressive Ancient count from the receipt ledger as the event
    /// begins, inside the move action, so the count is the same on every peer. The options are
    /// built after an await, when a later receipts action may already have run on one peer only,
    /// so they read the frozen count; a receipt recorded later applies to the next Ancient.
    /// </summary>
    public static void BeginEncounter(RunState runState, bool isAncient)
    {
        FrozenByPlayer.Clear();
        _encounterFrozen = false;
        if (!isAncient
            || !MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.ShouldRunReplicatedConstruction(
                MultiplayerFeature.Ancients
            ))
        {
            return;
        }

        foreach (Player player in runState.Players)
        {
            if (!ApReceiptLedger.TryGetState(player, out ApPlayerRunState state)
                || player.GetAPCharacterNumber() is not long characterOffset)
            {
                continue;
            }

            // The run's first Ancient begins at launch, before the owner's run-start receipts
            // action can run; the lobby's snapshot of the owner's receipts stands in until then.
            int recorded = ApReceiptLedger.CountCharacterReceipts(
                state,
                APItem.ProgressiveAncient,
                characterOffset
            );
            state.InitialProgressiveAncientsByCharacter.TryGetValue(characterOffset, out int initial);
            FrozenByPlayer[player.NetId] = Math.Max(recorded, initial);
        }
        _encounterFrozen = true;
    }

    public static bool TryGetFrozenContext(
        Player player,
        out ArchipelagoSettings settings,
        out int receivedCount,
        out long characterOffset,
        out string reason)
    {
        settings = null!;
        receivedCount = 0;
        characterOffset = -1;
        reason = string.Empty;
        if (!ApPlayerContextResolver.TryGetRewardSettings(player, out settings))
        {
            reason = $"No frozen AP Ancient settings exist for player {player.NetId}.";
            return false;
        }
        if (!_encounterFrozen || !FrozenByPlayer.TryGetValue(player.NetId, out receivedCount))
        {
            reason = $"No Progressive Ancient count was frozen for player {player.NetId}.";
            return false;
        }
        if (!settings.TryGetCharacterConfig(
                player.Character.Id.Entry,
                out CharacterConfig? config
            ))
        {
            reason = $"No AP character mapping exists for {player.Character.Id.Entry}.";
            return false;
        }

        characterOffset = config.CharOffset;
        if (characterOffset > 0)
            return true;

        reason = $"Character {player.Character.Id.Entry} has an invalid AP offset.";
        return false;
    }

}
