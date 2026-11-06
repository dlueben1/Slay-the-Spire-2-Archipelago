namespace StS2AP.Persistence;

/// <summary>Where a floor's gold came from or went, as the Economy tab splits it.</summary>
public enum ApEconomyRoom
{
    Combat,
    Shop,
    Other,
}

/// <summary>A floor's gold as the game's run history recorded it for the local player.</summary>
public readonly record struct ApEconomyFloor(
    ApEconomyRoom Room,
    int GoldGained,
    int GoldSpent,
    int GoldLost,
    int GoldStolen,
    int CurrentGold);

/// <summary>
/// Where a run's gold came from and where it went. The game counts AP gold in a floor's gold
/// gained, so the AP log's gold claims on that floor are taken back out of it. Claims a checkpoint
/// reload undid never reached the surviving run, so they are left out.
/// </summary>
public sealed class ApRunEconomy
{
    public int StartGold { get; private init; }
    public int EndGold { get; private init; }
    public int PeakGold { get; private init; }
    /// <summary>The floor index (from 0) where gold was highest, or -1 for a run without floors.</summary>
    public int PeakFloor { get; private init; } = -1;
    public int CombatGold { get; private init; }
    public int OtherGold { get; private init; }
    public int ApGold { get; private init; }
    public int ShopSpent { get; private init; }
    public int OtherSpent { get; private init; }
    public int Lost { get; private init; }
    public int Stolen { get; private init; }
    /// <summary>AP gold claimed on each floor, by floor index.</summary>
    public IReadOnlyList<int> ApGoldByFloor { get; private init; } = Array.Empty<int>();

    public int Gained => CombatGold + OtherGold + ApGold;
    public int Spent => ShopSpent + OtherSpent;

    public static ApRunEconomy Build(IReadOnlyList<ApEconomyFloor> floors, ApRunGraphLog log)
    {
        if (floors.Count == 0)
            return new ApRunEconomy();

        var apByFloor = new int[floors.Count];
        foreach (var e in log.Events)
        {
            if (e.Kind != ApRunGraphEventKind.GoldClaimed || e.Abandoned)
                continue;
            // The log stores the game's TotalFloor, which counts from 1.
            apByFloor[Math.Clamp(e.Floor - 1, 0, floors.Count - 1)] += e.Amount;
        }

        int combat = 0, other = 0, shopSpent = 0, otherSpent = 0, lost = 0, stolen = 0;
        int peak = int.MinValue, peakFloor = -1;
        for (int i = 0; i < floors.Count; i++)
        {
            var floor = floors[i];
            // A claim the history missed (made with no floor open) must not push game gold below zero.
            int gameGold = Math.Max(0, floor.GoldGained - apByFloor[i]);
            if (floor.Room == ApEconomyRoom.Combat)
                combat += gameGold;
            else
                other += gameGold;
            if (floor.Room == ApEconomyRoom.Shop)
                shopSpent += floor.GoldSpent;
            else
                otherSpent += floor.GoldSpent;
            lost += floor.GoldLost;
            // Gold taken back from a thief is recorded as negative stolen gold.
            stolen += floor.GoldStolen;
            if (floor.CurrentGold > peak)
            {
                peak = floor.CurrentGold;
                peakFloor = i;
            }
        }

        var first = floors[0];
        int start = first.CurrentGold - first.GoldGained + first.GoldSpent + first.GoldLost + first.GoldStolen;
        return new ApRunEconomy
        {
            StartGold = Math.Max(0, start),
            EndGold = floors[^1].CurrentGold,
            PeakGold = peak,
            PeakFloor = peakFloor,
            CombatGold = combat,
            OtherGold = other,
            ApGold = apByFloor.Sum(),
            ShopSpent = shopSpent,
            OtherSpent = otherSpent,
            Lost = lost,
            Stolen = Math.Max(0, stolen),
            ApGoldByFloor = apByFloor,
        };
    }
}
