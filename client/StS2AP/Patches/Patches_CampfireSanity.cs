
using HarmonyLib;
using Archipelago.MultiClient.Net.Models;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using StS2AP.Utils;
using StS2AP.Extensions;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes;


namespace StS2AP.Patches
{
    public static class Patches_RestSiteOption
    {
        [HarmonyPatch(typeof(RestSiteOption), "Generate")]
        public static class Generate
        {
            [HarmonyPostfix]
            static void AddOptions(Player player, ref List<RestSiteOption> __result)
            {
                if(!ArchipelagoClient.Settings.CampfireSanity)
                {
                    return;
                }
                var progress = ArchipelagoClient.Progress;
                LogUtility.Info($"Adding Campfire Locations for act {player.RunState.CurrentActIndex}");
                for (int i = 1; i <= player.RunState.CurrentActIndex + 1; i++)
                {
                    for (int j = 1; j <= 2; j++)
                    {
                        {
                            var checkName = $"{player.APName()} Act {i} Campfire {j}";
                            bool isChecked = false;
                            progress.CampfiresChecked.TryGetValue(checkName, out isChecked);
                            if (!isChecked)
                            {
                                
                                var locationId = ArchipelagoClient.Session.Locations.GetLocationIdFromName("Slay the Spire II", checkName);
                                LogUtility.Info($"Adding campfire location {locationId} " + checkName);
                                var description = checkName;
                                ScoutedItemInfo info;
                                if (ArchipelagoClient.ScoutedLocations.TryGetValue(locationId, out info))
                                {
                                    description = info.Player.Alias + "'s " + info.ItemName;
                                }
                                __result.Add(new APRestOption(player, locationId, info, description));
                            }
                        }

                    }
                }

                bool canRest = ArchipelagoClient.Session.Items.AllItemsReceived.Where(i => $"{player.APName()} Progressive Rest".Equals(i.ItemName))
                    .Count() >= Math.Min(player.RunState.CurrentActIndex + 1, 3);
                bool canSmith = ArchipelagoClient.Session.Items.AllItemsReceived.Where(i => $"{player.APName()} Progressive Smith".Equals(i.ItemName))
                   .Count() >= Math.Min(player.RunState.CurrentActIndex + 1, 3);
                bool anyEnabled = canRest || canSmith;
                // Removing the heal option (potentially) in favor of the fake heal option
                if(!canRest)
                {
                    __result.RemoveAll(n => "HEAL".Equals(n.OptionId));
                }

                foreach (var option in __result)
                {
                    if (option.OptionId == "SMITH")
                    {
                        option.IsEnabled = canSmith;
                    } else
                    {
                        anyEnabled |= option.IsEnabled;
                    }
                }

                if (!anyEnabled)
                {
                    // Being unable to do anything results in a softlock, so we give something to do.
                    // TODO: I wonder how this interacts with the potion when healing relic
                    __result.Insert(0, new FakeRestOption(player));
                }

            }
        }

        public class APRestOption : RestSiteOption
        {
            private readonly long locationId;
            private readonly string description;
            private readonly ScoutedItemInfo? info;
            public APRestOption(Player owner, long locationId, ScoutedItemInfo? info, string description) : base(owner)
            {
                this.locationId = locationId;
                this.description = description;
                this.info = info;
            }

            public override IEnumerable<string> AssetPaths
            {
                get
                {
                    List<string> list = new List<string>();
                    list.AddRange(base.AssetPaths);
                    list.AddRange(NRestSmokeVfx.AssetPaths);
                    list.AddRange(NDesaturateTransitionVfx.AssetPaths);
                    return list;
                }
            }

            public override LocString Description
            {
                get
                {
                    LocString description = new LocString("rest_site_ui", "OPTION_CHECK.description");
                    description.Add("description", this.description);
                    return description;
                }
            }

            public override string OptionId
            {
                get
                {
                    // This gets used in a few places internally in the code:
                    // 1: For the title of the rest site option
                    // 2: For the png lookup of the option
                    // 3: For the description of the option
                    // 4: For the description of the option when disabled
                    // For (3), we can override and replace with what we need.
                    // For the rest, we have to create localization files/pngs.
                    // Also, previously I tried importing the ItemFlags from Multiclient, but that broke patching
                    // for some reason.
                    if (info?.Advancement() ?? false)
                        return "PROGRESSION";
                    if (info?.Trap() ?? false)
                        return "TRAP";
                    if (info?.Useful() ?? false)
                        return "USEFUL";
                    return "FILLER";
                }
            }

            public override Task<bool> OnSelect()
            {
                // Supposed to return true if selecting this option succeeded.
                return SendCampfireCheck(locationId);
            }

            public static async Task<bool> SendCampfireCheck(long locationId)
            {
                GameUtility.SendCheck(locationId);
                return true;
            }
        }

        public class FakeRestOption : RestSiteOption
        {
            public FakeRestOption(Player owner) : base(owner)
            {
            }

            public override string OptionId => "HEAL";

            public override LocString Description
            {
                get
                {
                    LocString description = new LocString("rest_site_ui", "OPTION_HEAL.descriptionDisabled");
                    return description;
                }
            }

            public override Task<bool> OnSelect()
            {
                return DoNothing();
            }
            public static async Task<bool> DoNothing()
            {
                return true;
            }
        }
    }
}
