using MegaCrit.Sts2.Core.Entities.Players;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Extensions
{
    public static class PlayerExtensions
    {
        public static string APName(this Player player)
        {
            return player.Character.Title.GetFormattedText().Split().Last();
        }
    }
}
