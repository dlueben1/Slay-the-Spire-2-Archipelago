using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;
using StS2AP.Utils;
using STS2RitsuLib.Interop.AutoRegistration;

namespace StS2AP.Models;

/// <summary>
/// The Map Sight bonus's free move: once per act, travel to any room in the next row, ignoring the
/// paths, so the player can reach what the Scouting list shows. Works like Winged Boots and shares
/// free moves with it and the Wayfinder Boon (<see cref="FreeTravelUses"/>). Drawn with the game's
/// unused "diagram" sprite. Singleplayer only, as Winged Boots is, because the map is shared in
/// multiplayer.
/// </summary>
[RegisterRelic(typeof(SharedRelicPool))]
public sealed class ApRouteDiagram : ApGameArtRelic
{
    private bool _usedThisAct;

    protected override string ArtName => "diagram";

    public override bool IsUsedUp => UsedThisAct;

    [SavedProperty]
    public bool UsedThisAct
    {
        get => _usedThisAct;
        set
        {
            AssertMutable();
            _usedThisAct = value;
            // Unlike Winged Boots, a spent Route Diagram comes back, so the status goes both ways.
            Status = value ? RelicStatus.Disabled : RelicStatus.Normal;
        }
    }

    public override bool ShouldAllowFreeTravel() => !IsUsedUp;

    public override Task AfterActEntered()
    {
        if (UsedThisAct)
            UsedThisAct = false;
        return Task.CompletedTask;
    }

    public override Task AfterRoomEntered(AbstractRoom room)
    {
        if (FreeTravelUses.ShouldSpend(this))
        {
            Flash();
            UsedThisAct = true;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds the relic when the player has the Map Sight bonus and lacks it. Singleplayer only: the
    /// Scouting list also works in multiplayer, but there the map is shared.
    /// </summary>
    public static void EnsureOwnedIfEarned(Player? player, bool silent = false)
    {
        if (player == null || MultiplayerSupport.IsMultiplayerScope || !MapScouting.IsActive)
            return;
        SightBonusRelic.EnsureOwned<ApRouteDiagram>(player, silent);
    }
}
