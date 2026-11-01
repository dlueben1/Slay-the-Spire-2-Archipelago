using System.Text.Json;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using StS2AP.Patches;
using StS2AP.Persistence;
using StS2AP.UI.RunGraph;
using StS2AP.Utils;

namespace StS2AP.UI;

/// <summary>One player as a save card shows them.</summary>
internal sealed record ApSavePlayer(
    ulong NetId,
    CharacterModel? Character,
    int Hp,
    int MaxHp,
    int Gold,
    int DeckSize,
    IReadOnlyList<RecapRelic> Relics);

/// <summary>
/// What the save pickers show about a saved run: each player's HP, gold, deck and relics, and the
/// floors so far. It is read from the saved run itself, so it needs nothing from the AP server.
/// </summary>
internal sealed class ApSaveSummary
{
    internal required IReadOnlyList<ApSavePlayer> Players { get; init; }
    /// <summary>The first player's floors, for the room icon at the point the run was saved.</summary>
    internal required IReadOnlyList<RunGraphFloor> Floors { get; init; }
    internal int Act { get; init; }
    internal long RunSeconds { get; init; }

    /// <summary>A singleplayer checkpoint, stored as the compressed AP envelope around the game's run.</summary>
    internal static ApSaveSummary? FromCheckpoint(string compressedPayload)
    {
        try
        {
            string json = Patches_RunSaveManager.SaveRun.Unzip(compressedPayload);
            var envelope = JsonSerializer.Deserialize<SerializableAP>(json, SerializationUtility.CombinedOptions);
            return envelope?.SaveData is { } run ? FromRunJson(run.GetRawText()) : null;
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Could not read an AP checkpoint for its card: {ex.Message}");
            return null;
        }
    }

    /// <summary>A game run save as JSON, as the multiplayer campaigns store it.</summary>
    internal static ApSaveSummary? FromRunJson(string json)
    {
        try
        {
            ReadSaveResult<SerializableRun> read = JsonSerializationUtility.FromJson<SerializableRun>(json);
            return read.Success && read.SaveData != null ? FromRun(read.SaveData) : null;
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Could not read a saved run for its card: {ex.Message}");
            return null;
        }
    }

    private static ApSaveSummary FromRun(SerializableRun run)
    {
        var players = (run.Players ?? new List<SerializablePlayer>())
            .Select(player => new ApSavePlayer(
                player.NetId,
                player.CharacterId == null || player.CharacterId == ModelId.none
                    ? null
                    : SaveUtil.CharacterOrDeprecated(player.CharacterId),
                player.CurrentHp,
                player.MaxHp,
                player.Gold,
                player.Deck.Count,
                player.Relics.Select(ApRunRecapData.ToRelic).ToList()))
            .ToList();
        ulong firstPlayer = players.FirstOrDefault()?.NetId ?? 0;
        return new ApSaveSummary
        {
            Players = players,
            Floors = ApRunGraphCanvas.BuildFloors(run.MapPointHistory, firstPlayer),
            Act = run.CurrentActIndex + 1,
            RunSeconds = run.RunTime,
        };
    }
}
