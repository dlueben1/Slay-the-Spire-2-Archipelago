using Archipelago.MultiClient.Net;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using StS2AP.UI;

namespace StS2AP.Utils
{
    // StS 2 picks up these reflectively out of the mods without problem.
    public class APDevCommand : AbstractConsoleCmd
    {
        public override string CmdName => "ap";

        public override string Args =>
            "report | !command";

        public override string Description =>
            "ap report exports the newest divergence ZIP and game logs; also supports AP server commands";

        public override bool IsNetworked => false;

        public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
        {
            if (args.Length <= 1)
                return CompleteArgument(["report"], [], args.FirstOrDefault() ?? "", CompletionType.Subcommand);
            return base.GetArgumentCompletions(player, args);
        }

        public override CmdResult Process(Player? issuingPlayer, string[] args)
        {
            if (args.Length == 0)
            {
                return new CmdResult(false, "Usage: ap report | ap !command");
            }

            if (args[0].Equals("report", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length != 1)
                    return new CmdResult(false, "Usage: ap report (exports the newest divergence report and opens its folder)");
                bool started = ApBugReport.TryStart(out string message);
                return new CmdResult(started, message);
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

            return new CmdResult(false, "Unknown AP command. Use ap report or ap !command.");
        }
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
