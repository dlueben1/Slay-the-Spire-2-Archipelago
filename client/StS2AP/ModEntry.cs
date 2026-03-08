using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP
{
    [ModInitializer("Initialize")]
    public class ModEntry
    {
        public static void Initialize()
        {
            var harmony = new Harmony("archipelago.patch");
            harmony.PatchAll();
        }
    }
}
