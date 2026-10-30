using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Models;

/// <summary>
/// ? rooms lean towards fights. Combat starts at 20% and rises 15% each time a ? room is not a
/// fight, elites start at 0% and rise 4%, and chests start and rise at 1%. Shops keep the base
/// game's 3%. The base game resets every chance at the start of each act and has no cap.
/// </summary>
public sealed class ApDangerousUnknowns : ApRunModifier
{
    private const float MonsterStart = 0.20f;
    private const float MonsterIncrease = 0.15f;
    private const float EliteStart = 0f;
    private const float EliteIncrease = 0.04f;
    private const float TreasureStart = 0.01f;

    protected override string IconModifierEntry => "deadly_events";

    protected override void AfterRunCreated(RunState runState)
    {
        UnknownMapPointOdds odds = runState.Odds.UnknownMapPoint;
        SetStartingOdds(odds);
        odds.MonsterOdds = MonsterStart;
        odds.EliteOdds = EliteStart;
        odds.TreasureOdds = TreasureStart;
    }

    // A run save keeps the current chances but not the starting ones, which each roll and act
    // reset return to.
    protected override void AfterRunLoaded(RunState runState) =>
        SetStartingOdds(runState.Odds.UnknownMapPoint);

    // The game raises every type a ? room did not roll by that type's starting chance. Combat and
    // elites rise by their own amounts instead; chests keep rising by their 1% start.
    public override float ModifyOddsIncreaseForUnrolledRoomType(RoomType roomType, float oddsIncrease) =>
        roomType switch
        {
            RoomType.Monster => MonsterIncrease,
            RoomType.Elite => EliteIncrease,
            _ => oddsIncrease,
        };

    private static void SetStartingOdds(UnknownMapPointOdds odds)
    {
        odds.SetBaseOdds(RoomType.Monster, MonsterStart);
        odds.SetBaseOdds(RoomType.Elite, EliteStart);
        odds.SetBaseOdds(RoomType.Treasure, TreasureStart);
    }
}
