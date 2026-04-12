using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Utils
{
    public class APDevCommand : AbstractConsoleCmd
    {
        public override string CmdName => "ap";

        public override string Args => "";

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
                return new CmdResult(false, "Not connected");
            }
            ArchipelagoClient.Session.Say(sendMe);
            return new CmdResult(true);
        }
    }
}
