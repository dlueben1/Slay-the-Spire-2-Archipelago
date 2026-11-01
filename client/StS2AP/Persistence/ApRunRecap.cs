namespace StS2AP.Persistence;

/// <summary>A floor's HP as the recap needs it, taken from the game's run history.</summary>
public readonly record struct ApRecapFloorHp(int Hp, int MaxHp);

/// <summary>One player whose items this run's checks held.</summary>
public sealed record ApRecapPlayer(
    string Name,
    string Game,
    bool IsSelf,
    int Checks,
    IReadOnlyDictionary<ApRunGraphItemType, int> ByType,
    ApRunGraphEvent BestFind);

/// <summary>A one-line fact about the run, shown as a tile: what it is, who or what, and a number.</summary>
public sealed record ApRecapHighlight(ApRecapHighlightKind Kind, string Title, string Subject, string Value, string Caption);

public enum ApRecapHighlightKind
{
    FirstKey,
    MostHelped,
    ClosestCall,
    Rewritten,
    HomeCooking,
    BestSupplier,
}

/// <summary>
/// The numbers behind the end-of-run recap, worked out from the AP log and the run's HP by floor.
/// Checks sent on a branch a reload undid still count: the server received them.
/// </summary>
public sealed class ApRunRecap
{
    public static readonly ApRunGraphItemType[] TypeOrder =
    {
        ApRunGraphItemType.Progression,
        ApRunGraphItemType.Useful,
        ApRunGraphItemType.Filler,
        ApRunGraphItemType.Trap,
    };

    public int ChecksSent { get; private init; }
    public int SentToSelf { get; private init; }
    public int SentToOthers => ChecksSent - SentToSelf;
    public int SentOnUndoneBranch { get; private init; }
    public IReadOnlyDictionary<ApRunGraphItemType, int> SentByType { get; private init; } = new Dictionary<ApRunGraphItemType, int>();
    public IReadOnlyList<ApRunGraphEvent> Claims { get; private init; } = Array.Empty<ApRunGraphEvent>();
    public int GoldClaimed { get; private init; }
    public int DeathLinksSent { get; private init; }
    public int DeathLinksReceived { get; private init; }
    /// <summary>Players whose items were found, most checks first.</summary>
    public IReadOnlyList<ApRecapPlayer> Players { get; private init; } = Array.Empty<ApRecapPlayer>();
    public IReadOnlyList<ApRecapHighlight> Highlights { get; private init; } = Array.Empty<ApRecapHighlight>();

    public static ApRunRecap Build(ApRunGraphLog log, IReadOnlyList<ApRecapFloorHp> floors)
    {
        var sent = log.Events.Where(e => e.Kind == ApRunGraphEventKind.CheckSent).ToList();
        // A reload hands back what was claimed after its checkpoint, so those claims did not happen.
        var claims = log.Events.Where(e => e.Kind == ApRunGraphEventKind.ItemClaimed && !e.Abandoned).ToList();
        var players = sent
            .GroupBy(e => (e.OtherPlayer, e.IsSelf))
            .Select(g => new ApRecapPlayer(
                g.Key.OtherPlayer,
                g.Select(e => e.OtherGame).FirstOrDefault(game => game.Length > 0) ?? "",
                g.Key.IsSelf,
                g.Count(),
                CountByType(g),
                BestFind(g)))
            .OrderByDescending(p => p.Checks)
            .ThenBy(p => p.IsSelf)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int sentToSelf = sent.Count(e => e.IsSelf);
        int sentOnUndoneBranch = sent.Count(e => e.Abandoned);
        return new ApRunRecap
        {
            ChecksSent = sent.Count,
            SentToSelf = sentToSelf,
            SentOnUndoneBranch = sentOnUndoneBranch,
            SentByType = CountByType(sent),
            Claims = claims,
            GoldClaimed = log.Events.Where(e => e.Kind == ApRunGraphEventKind.GoldClaimed && !e.Abandoned).Sum(e => e.Amount),
            DeathLinksSent = log.Events.Count(e => e.Kind == ApRunGraphEventKind.DeathLinkSent),
            DeathLinksReceived = log.Events.Count(e => e.Kind == ApRunGraphEventKind.DeathLinkReceived),
            Players = players,
            Highlights = BuildHighlights(log.Outcome, sent, claims, players, sentToSelf, sentOnUndoneBranch, floors),
        };
    }

    private static Dictionary<ApRunGraphItemType, int> CountByType(IEnumerable<ApRunGraphEvent> events)
    {
        var counts = TypeOrder.ToDictionary(type => type, _ => 0);
        foreach (var e in events)
            counts[e.Classification]++;
        return counts;
    }

    /// <summary>The most important item found for a player, the earliest of its kind.</summary>
    private static ApRunGraphEvent BestFind(IEnumerable<ApRunGraphEvent> events) => events
        .OrderBy(e => Array.IndexOf(TypeOrder, e.Classification))
        .ThenBy(e => e.Floor)
        .First();

    /// <summary>Only the highlights this run has something to say about, in a fixed order.</summary>
    private static List<ApRecapHighlight> BuildHighlights(
        ApRunOutcome? outcome,
        List<ApRunGraphEvent> sent,
        List<ApRunGraphEvent> claims,
        List<ApRecapPlayer> players,
        int sentToSelf,
        int sentOnUndoneBranch,
        IReadOnlyList<ApRecapFloorHp> floors)
    {
        var highlights = new List<ApRecapHighlight>();

        var firstKey = sent
            .Where(e => !e.IsSelf && e.Classification == ApRunGraphItemType.Progression)
            .OrderBy(e => e.Floor)
            .FirstOrDefault();
        if (firstKey != null)
        {
            highlights.Add(new ApRecapHighlight(ApRecapHighlightKind.FirstKey, "First key",
                $"{firstKey.ItemName} for {firstKey.OtherPlayer}", $"F{firstKey.Floor}",
                "first progression item sent to someone else"));
        }

        var mostHelped = players.FirstOrDefault(p => !p.IsSelf);
        if (mostHelped != null)
        {
            highlights.Add(new ApRecapHighlight(ApRecapHighlightKind.MostHelped, "Most helped",
                mostHelped.Name, $"{mostHelped.Checks}", $"{Checks(mostHelped.Checks)} that held their items"));
        }

        // The floor a lost run ended on is 0 HP by definition, so it is no close call.
        int considered = outcome == ApRunOutcome.Lost ? floors.Count - 1 : floors.Count;
        var lowest = floors.Take(considered)
            .Select((floor, index) => (floor, index))
            .Where(f => f.floor.MaxHp > 0 && f.floor.Hp > 0)
            .OrderBy(f => (double)f.floor.Hp / f.floor.MaxHp)
            .ThenBy(f => f.index)
            .Select(f => ((ApRecapFloorHp Floor, int Index)?)f)
            .FirstOrDefault();
        if (lowest is { } low)
        {
            highlights.Add(new ApRecapHighlight(ApRecapHighlightKind.ClosestCall, "Closest call",
                $"{low.Floor.Hp} HP of {low.Floor.MaxHp}, floor {low.Index + 1}", $"{low.Floor.Hp} HP",
                "the lowest the run got"));
        }

        if (sentOnUndoneBranch > 0)
        {
            int firstUndone = sent.Where(e => e.Abandoned).Min(e => e.Floor);
            highlights.Add(new ApRecapHighlight(ApRecapHighlightKind.Rewritten, "Rewritten",
                $"A reload undid floor {firstUndone} onward", $"{sentOnUndoneBranch}",
                $"{Checks(sentOnUndoneBranch)} sent on the undone branch"));
        }

        if (sentToSelf > 0)
        {
            highlights.Add(new ApRecapHighlight(ApRecapHighlightKind.HomeCooking, "Home cooking",
                "Your own items found", $"{sentToSelf}",
                $"{Checks(sentToSelf)} that held something for you"));
        }

        var supplier = claims
            .Where(e => !e.IsSelf && e.OtherPlayer.Length > 0)
            .GroupBy(e => e.OtherPlayer)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (supplier != null)
        {
            highlights.Add(new ApRecapHighlight(ApRecapHighlightKind.BestSupplier, "Best supplier",
                supplier.Key, $"{supplier.Count()}",
                supplier.Count() == 1 ? "item they found that you claimed" : "items they found that you claimed"));
        }

        return highlights;
    }

    private static string Checks(int count) => count == 1 ? "check" : "checks";
}
