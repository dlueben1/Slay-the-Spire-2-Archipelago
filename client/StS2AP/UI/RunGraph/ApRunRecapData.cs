using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace StS2AP.UI.RunGraph;

/// <summary>A deck entry as the recap lists it: copies of one card at one upgrade level.</summary>
internal sealed record RecapCard(string Name, CardType Type, bool Upgraded, int Count);

internal sealed record RecapRelic(string Name, Texture2D? Icon, bool FromArchipelago);

/// <summary>
/// Everything the recap draws for one run: the AP log, the numbers worked out from it, and what
/// the game saved about the run. The game-over screen has only the live run's save, while Run
/// History and Past AP Runs have the saved history, so both can build it.
/// </summary>
internal sealed class ApRunRecapData
{
    private const string ArchipelagoRelicPrefix = "ARCHIPELAGO_";

    internal required ApRunGraphLog Log { get; init; }
    internal required ApRunRecap Recap { get; init; }
    internal required IReadOnlyList<RunGraphFloor> Floors { get; init; }
    internal CharacterModel? Character { get; init; }
    internal required IReadOnlyList<RecapCard> Deck { get; init; }
    internal required IReadOnlyList<RecapRelic> Relics { get; init; }
    internal int Ascension { get; init; }
    internal long RunSeconds { get; init; }
    internal string KilledBy { get; init; } = "";

    internal DateTimeOffset Date => DateTimeOffset.FromUnixTimeSeconds(Log.StartTime).ToLocalTime();

    internal static ApRunRecapData FromHistory(RunHistory history, ApRunGraphLog log)
    {
        var player = history.Players.FirstOrDefault(p => p.Id == log.LocalNetId) ?? history.Players.FirstOrDefault();
        return Create(log, history.MapPointHistory, player?.Character, player?.Deck, player?.Relics,
            history.Ascension, (long)history.RunTime, KilledByName(history));
    }

    /// <summary>The encounter or event that ended a lost run, or "" for a win.</summary>
    internal static string KilledByName(RunHistory history) =>
        history.KilledByEncounter != ModelId.none
            ? SaveUtil.EncounterOrDeprecated(history.KilledByEncounter).Title.GetFormattedText()
            : history.KilledByEvent != ModelId.none
                ? SaveUtil.EventOrDeprecated(history.KilledByEvent).Title.GetFormattedText()
                : "";

    /// <summary>The live run at game over, before or without a saved history.</summary>
    internal static ApRunRecapData FromRun(SerializableRun run, ApRunGraphLog log)
    {
        var player = run.Players.FirstOrDefault(p => p.NetId == log.LocalNetId) ?? run.Players.FirstOrDefault();
        long seconds = run.WinTime > 0 ? run.WinTime : run.RunTime;
        return Create(log, run.MapPointHistory, player?.CharacterId, player?.Deck, player?.Relics,
            run.Ascension, seconds, killedBy: "");
    }

    private static ApRunRecapData Create(
        ApRunGraphLog log,
        IReadOnlyList<IReadOnlyList<MapPointHistoryEntry>> mapHistory,
        ModelId? character,
        IEnumerable<SerializableCard>? deck,
        IEnumerable<SerializableRelic>? relics,
        int ascension,
        long runSeconds,
        string killedBy)
    {
        var floors = ApRunGraphCanvas.BuildFloors(mapHistory, log.LocalNetId);
        return new ApRunRecapData
        {
            Log = log,
            Recap = ApRunRecap.Build(log, floors.Select(f => new ApRecapFloorHp(f.Hp, f.MaxHp)).ToList()),
            Floors = floors,
            Character = character == null || character == ModelId.none ? null : SaveUtil.CharacterOrDeprecated(character),
            Deck = GroupDeck(deck ?? Enumerable.Empty<SerializableCard>()),
            Relics = (relics ?? Enumerable.Empty<SerializableRelic>()).Select(ToRelic).ToList(),
            Ascension = ascension,
            RunSeconds = runSeconds,
            KilledBy = killedBy,
        };
    }

    private static List<RecapCard> GroupDeck(IEnumerable<SerializableCard> deck) => deck
        .Where(card => card.Id != null)
        .GroupBy(card => (card.Id!, Upgraded: card.CurrentUpgradeLevel > 0))
        .Select(group =>
        {
            var model = SaveUtil.CardOrDeprecated(group.Key.Item1);
            return new RecapCard(model.Title, model.Type, group.Key.Upgraded, group.Count());
        })
        .OrderBy(card => card.Name, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(card => card.Upgraded)
        .ToList();

    internal static RecapRelic ToRelic(SerializableRelic relic)
    {
        var model = relic.Id == null ? null : SaveUtil.RelicOrDeprecated(relic.Id);
        if (model == null)
            return new RecapRelic("Unknown relic", null, false);
        Texture2D? icon = ResourceLoader.Exists(model.IconPath) ? model.Icon : null;
        return new RecapRelic(model.Title.GetFormattedText(), icon,
            model.Id.Entry.StartsWith(ArchipelagoRelicPrefix, StringComparison.Ordinal));
    }
}
