using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using StS2AP.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Utils
{
    // StS 2 picks up these reflectively out of the mods without problem.
    public class APDevCommand : AbstractConsoleCmd
    {
        public override string CmdName => "ap";

        public override string Args => ""; // dunno what this is for

        public override string Description => "Sends an AP command (such as !hint) to the server";

        public override bool IsNetworked => false;

        public override CmdResult Process(Player? issuingPlayer, string[] args)
        {
            var sendMe = String.Join(" ", args);
            if(!sendMe.StartsWith("!"))
            {
                return new CmdResult(false, "AP Commands must start with '!'");
            }
            if(!ArchipelagoClient.IsConnected)
            {
                return new CmdResult(false, "Not connected to AP");
            }
            ArchipelagoClient.Session.Say(sendMe);
            return new CmdResult(true);
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
