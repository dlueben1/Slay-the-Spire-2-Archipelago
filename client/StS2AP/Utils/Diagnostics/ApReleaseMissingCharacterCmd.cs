using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using StS2AP.Data;
using StS2AP.Models;

namespace StS2AP.Utils.Diagnostics;

/// <summary>
/// Dev-console escape hatch that sends every remaining location of a slot character whose mod is
/// not loaded, for when no playable character can reasonably finish that character's checks. It only targets
/// missing characters, so it cannot release a playable character's checks, and it refuses to run
/// in race rooms or before the room's race mode is known.
/// </summary>
public sealed class ApReleaseMissingCharacterCmd : AbstractConsoleCmd
{
    private const string ConfirmArgument = "confirm";

    public override string CmdName => "ap_release_missing";

    public override string Args => "<character> [confirm]";

    public override string Description =>
        "Sends all remaining AP checks of a character whose mod is not loaded. Disabled in race rooms.";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        ArchipelagoSettings? settings = ArchipelagoClient.Settings;
        if (!ArchipelagoClient.IsConnected || settings == null)
            return new CmdResult(false, "Connect to an Archipelago slot first.");
        if (ArchipelagoClient.IsRaceMode != false)
        {
            return new CmdResult(false, ArchipelagoClient.IsRaceMode == true
                ? "This command is disabled in race rooms."
                : "The room's race mode is unknown, so this command stays disabled.");
        }

        MissingCharacterFallbackPlan plan = MissingCharacterFallback.GetPlan(settings);
        if (!plan.IsActive)
            return new CmdResult(false, "Every character in this slot is loaded; there is nothing to release.");

        if (args.Length == 0)
            return new CmdResult(true, DescribeMissingCharacters(settings, plan));

        CharacterConfig? target = FindMissingCharacter(settings, plan, args[0]);
        if (target == null)
        {
            return new CmdResult(false,
                $"'{args[0]}' is not a missing character in this slot.\n{DescribeMissingCharacters(settings, plan)}");
        }

        long[] remaining = RemainingLocations(target.CharOffset);
        if (remaining.Length == 0)
            return new CmdResult(true, $"{target.Name} has no remaining checks.");

        bool confirmed = args.Length > 1
            && string.Equals(args[1], ConfirmArgument, StringComparison.OrdinalIgnoreCase);
        if (!confirmed)
        {
            return new CmdResult(true,
                $"This would send {remaining.Length} remaining checks for {target.Name}. "
                    + $"Run '{CmdName} {args[0]} {ConfirmArgument}' to send them.");
        }

        LogUtility.Warn($"{CmdName}: releasing {remaining.Length} checks for missing character {target.Name}");
        LocationCheckSendResult result = GameUtility.QueueExactChecks(remaining);
        return result.WasRecorded
            ? new CmdResult(true, $"Sent {result.AcceptedCount} checks for {target.Name}.")
            : new CmdResult(false, $"No checks were sent for {target.Name} ({result.Dispatch}).");
    }

    public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
    {
        ArchipelagoSettings? settings = ArchipelagoClient.Settings;
        if (settings == null || args.Length > 1)
            return base.GetArgumentCompletions(player, args);

        IEnumerable<string> candidates = MissingCharacterConfigs(settings, MissingCharacterFallback.GetPlan(settings))
            .Select(config => config.OptionName)
            .Where(name => !string.IsNullOrWhiteSpace(name));
        return CompleteArgument(candidates, Array.Empty<string>(), args.Length == 1 ? args[0] : string.Empty);
    }

    private static string DescribeMissingCharacters(ArchipelagoSettings settings, MissingCharacterFallbackPlan plan) =>
        "Missing characters (remaining checks): "
            + string.Join(", ", MissingCharacterConfigs(settings, plan).Select(config =>
                $"{config.OptionName} ({RemainingLocations(config.CharOffset).Length})"));

    private static CharacterConfig? FindMissingCharacter(
        ArchipelagoSettings settings,
        MissingCharacterFallbackPlan plan,
        string name) =>
        MissingCharacterConfigs(settings, plan).FirstOrDefault(config =>
            string.Equals(config.OptionName, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(config.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(config.OfficialName, name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<CharacterConfig> MissingCharacterConfigs(
        ArchipelagoSettings settings,
        MissingCharacterFallbackPlan plan) =>
        settings.Characters.Values
            .Where(config => plan.IsMissing(config.CharOffset))
            .OrderBy(config => config.CharOffset);

    private static long[] RemainingLocations(long characterNumber)
    {
        var checkedLocations = ArchipelagoClient.CheckedLocations.ToHashSet();
        return ArchipelagoClient.SlotLocationIds
            .Where(id => !ArchipelagoIdCodec.IsArchipelagoReservedId(id)
                && ArchipelagoIdCodec.GetAPCharacterNumberFromLocationId(id) == characterNumber
                && !checkedLocations.Contains(id))
            .Order()
            .ToArray();
    }
}
