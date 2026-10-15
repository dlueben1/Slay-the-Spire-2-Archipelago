using Archipelago.MultiClient.Net;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.UI;

namespace StS2AP.Utils
{
    // StS 2 picks up these reflectively out of the mods without problem.
    public class APDevCommand : AbstractConsoleCmd
    {
        public override string CmdName => "ap";

        public override string Args =>
            "report | deathlink | !command";

        public override string Description =>
            "AP report, DeathLink status, and AP server commands";

        public override bool IsNetworked => false;

        public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
        {
            if (args.Length <= 1)
                return CompleteArgument(["report", "deathlink"], [], args.FirstOrDefault() ?? "", CompletionType.Subcommand);
            return base.GetArgumentCompletions(player, args);
        }

        public override CmdResult Process(Player? issuingPlayer, string[] args)
        {
            if (args.Length == 0)
            {
                return new CmdResult(false, "Usage: ap report | ap deathlink | ap !command");
            }

            if (args[0].Equals("report", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length != 1)
                    return new CmdResult(false, "Usage: ap report (exports the newest divergence report and opens its folder)");
                bool started = ApBugReport.TryStart(out string message);
                return new CmdResult(started, message);
            }

            if (args[0].Equals("deathlink", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length != 1)
                    return new CmdResult(false, "Usage: ap deathlink");
                return new CmdResult(true, GetDeathLinkStatus(issuingPlayer));
            }

            if (args[0].StartsWith("!", StringComparison.Ordinal))
            {
                string sendMe = string.Join(" ", args);
                ArchipelagoSession? session = ArchipelagoClient.Session;
                if (!ArchipelagoClient.IsConnected || session == null)
                    return new CmdResult(false, "Not connected to AP");
                session.Say(sendMe);
                return new CmdResult(true);
            }

            return new CmdResult(false, "Unknown AP command. Use ap report, ap deathlink, or ap !command.");
        }

        private static string GetDeathLinkStatus(Player? issuingPlayer)
        {
            ClientSettings local = ArchipelagoClient.LocalSettings.Value;
            ArchipelagoSettings? settings = ArchipelagoClient.Settings;
            bool connected = ArchipelagoClient.IsConnected;
            bool multiplayerRun = MultiplayerSupport.IsRealMultiplayerRun;
            ArchipelagoSettings? runSettings = null;
            if (multiplayerRun)
            {
                Player? localPlayer = GameUtility.CurrentPlayer ?? issuingPlayer;
                if (localPlayer?.RunState is RunState runState
                    && ApRunData.TryGetPlayerState(runState, localPlayer.NetId, out ApPlayerRunState playerState)
                    && playerState.Participation == ApParticipationKind.OwnApSlot)
                {
                    runSettings = playerState.SlotSettings;
                }
            }

            bool serviceReady = ArchipelagoClient.DeathLinkController != null;
            bool optionEnabled = DeathLinkUtility.IsDeathLinkEnabled;
            bool processingEnabled = GameUtility.IsInRun && connected && serviceReady && optionEnabled
                && (!multiplayerRun
                    || (MultiplayerSupport.IsLocalOwnApSlot && runSettings?.IsDeathLinkEnabled == true));
            string settingsSource = MultiplayerSupport.UsesFrozenHostSettings
                ? "frozen host run"
                : local.OverrideDeathLinkOptions ? "local override" : "AP slot";
            int? appliedDamagePercent = multiplayerRun
                ? runSettings?.DeathLinkDamagePercent
                : DeathLinkUtility.DeathLinkDamagePercent;

            var lines = new List<string>
            {
                $"DeathLink local processing={OnOff(processingEnabled)}; AP connected={YesNo(connected)}; service={(serviceReady ? "available" : "unavailable")}; settings source={settingsSource}.",
                $"Current option={OnOff(optionEnabled)}; applied damage={SettingPercent(appliedDamagePercent)}; death fragments={OnOff(DeathLinkUtility.AreDeathFragmentsEnabled)}.",
                $"Active settings: enabled={SettingValue(settings?.IsDeathLinkEnabled)}, damage={SettingPercent(settings?.DeathLinkDamagePercent)}, fragments={SettingValue(settings?.EnableDeathFragments)}.",
                $"Local override={OnOff(local.OverrideDeathLinkOptions)}; local opt-in={OnOff(local.EnableDeathLink)}, damage={local.DeathLinkPercentDamage}%, fragments={OnOff(local.EnableDeathFragments)}.",
            };

            if (multiplayerRun)
            {
                if (runSettings != null)
                {
                    lines.Add($"This run: enabled={OnOff(runSettings.IsDeathLinkEnabled)}, damage={runSettings.DeathLinkDamagePercent}%, fragments={OnOff(runSettings.EnableDeathFragments)} (frozen at lobby launch). Changes to local settings require a new run.");
                }
                else
                {
                    lines.Add("This run: no local AP-owned settings snapshot; DeathLink cannot affect this player.");
                }
            }

            string result = string.Join("\n", lines);
            LogUtility.Info(result);
            return result;
        }

        private static string OnOff(bool value) => value ? "on" : "off";

        private static string YesNo(bool value) => value ? "yes" : "no";

        private static string SettingValue(bool? value) => value.HasValue ? OnOff(value.Value) : "unavailable";

        private static string SettingPercent(int? value) => value.HasValue ? $"{value.Value}%" : "unavailable";
    }

    /// <summary>
    /// Toggles the live counters used to debug progressive Relic receipt/bank behavior.
    /// </summary>
    public class APRelicDebugCommand : AbstractConsoleCmd
    {
        public override string CmdName => "aprelicdebug";

        public override string Args => "[on|off]";

        public override string Description => "Toggles the AP Relic receipt/bank debug overlay";

        public override bool IsNetworked => false;

        public override CmdResult Process(Player? issuingPlayer, string[] args)
        {
            bool shouldShow;
            if (args.Length == 0)
            {
                shouldShow = !RelicRewardDebugUI.IsVisible;
            }
            else if (args.Length == 1 && args[0].Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                shouldShow = true;
            }
            else if (args.Length == 1 && args[0].Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                shouldShow = false;
            }
            else
            {
                return new CmdResult(false, "Usage: aprelicdebug [on|off]");
            }

            if (shouldShow)
                RelicRewardDebugUI.Show();
            else
                RelicRewardDebugUI.Hide();

            if (shouldShow && !RelicRewardDebugUI.IsVisible)
                return new CmdResult(false, "Could not create the AP Relic debug overlay; check the log.");

            return new CmdResult(
                true,
                $"AP Relic debug overlay {(RelicRewardDebugUI.IsVisible ? "enabled" : "disabled")}."
            );
        }
    }
}
