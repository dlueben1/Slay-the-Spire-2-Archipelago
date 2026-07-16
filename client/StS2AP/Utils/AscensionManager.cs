using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Models;
using StS2AP.Patches;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Utils
{
    public class AscensionManager
    {
        /// <summary>
        /// The initial ascension configuration for the current character.
        /// </summary>
        public ISet<AscensionLevel> ConfiguredAscension { get; set; } = new HashSet<AscensionLevel>();

        /// <summary>
        /// The current ascension "level" for the current character, based on the initial configuration
        /// plus any ascension downs that the player has received for the current character.
        /// </summary>
        public ISet<AscensionLevel> CurrentAscension { get; set; } = new HashSet<AscensionLevel>();

        private IHoverTip? _hoverTip;

        /// <summary>
        /// The hover tip for the ascension icon during a run.  Shows which ascensions are currently enabled.
        /// </summary>
        public IHoverTip HoverTip { 
            get {
                if(_hoverTip == null)
                {
                    UpdateHoverTip();
                }
                return _hoverTip!;
            }
        }

        public int AscensionLevelAsInt
        {
            get
            {
                if(CurrentAscension.Count == 0)
                {
                    return 0;
                }
                return CurrentAscension.Select(a => (int)a).Max();
            }
        }

        /// <summary>
        /// Whether the provided ascension level is set or not.
        /// </summary>
        public bool HasLevel(AscensionLevel level)
        {
            return CurrentAscension.Contains(level);
        }

        public void Reset()
        {
            ConfiguredAscension.Clear();
            CurrentAscension.Clear();
        }

        private void UpdateHoverTip()
        {

            if(GameUtility.CurrentPlayer == null)
            {
                return;
            }

            LocString title = new LocString("ascension", "PORTRAIT_TITLE");
            title.Add("character", GameUtility.CurrentPlayer.Character.Title);
            title.Add("ascension", CurrentAscension.Count);

            LocString description = new LocString("ascension", "PORTRAIT_DESCRIPTION");
            List<string> ascensions = new List<string>();
            for(int i = 1; i <= 10; i++)
            {
                if(CurrentAscension.Contains((AscensionLevel) i))
                {
                    ascensions.Add(AscensionHelper.GetTitle(i).GetFormattedText());
                }
            }
            description.Add("ascensions", ascensions);
            _hoverTip = new HoverTip(title, description);
            Patches_AscensionOverride.ChangeAscensionLabel(CurrentAscension.Count.ToString());
        }


        /// <summary>
        /// 
        /// </summary>
        public void Initialize(CharacterConfig currentConfig, ISet<AscensionLevel>? currentLevels = null)
        {
            // Initialize on new run, save, and update in game.
            Reset();
            
            foreach(var asc in currentConfig.Ascension)
            {
                var check = GetLevel(asc);
                if(check != null)
                {
                    AscensionLevel level = (AscensionLevel) check;
                    ConfiguredAscension.Add(level);
                    if (currentLevels == null)
                    {
                        CurrentAscension.Add(level);
                    }
                }
            }

            if (currentLevels != null)
            {
                // save load flow
                CurrentAscension.UnionWith(currentLevels);
            }
            else
            {
                // new run flow
                foreach (var item in ArchipelagoClient.Progress.AllReceivedItems)
                {
                    var id = item.Item.GetRawItemID();
                    LogUtility.Info($"Checking if item is an ascension {item.Item.ItemName} {id}");
                    if (((int)id) >= 19 && ((int)id) <= 28)
                    {
                        LogUtility.Info($"Removing ascension via item {item.Item.ItemName}");
                        ProcessAscensionLevel(currentConfig, item.Item, true);
                    }
                }
            }
        }


        public void ProcessAscensionLevel(CharacterConfig? config, ItemInfo item, bool initial)
        {
            if(config?.CharOffset != (int) item.GetStSCharID())
            {
                // not for this character
                return;
            }
            var id = item.GetRawItemID();
            var level = ToAscensionLevel(id);
            if(level == AscensionLevel.None)
            {
                return;
            }

            if(initial)
            {
                CurrentAscension.Remove(level);
            }
            else
            {
                RemoveLevel(level);
            }
            UpdateHoverTip();

        }


        /// <summary>
        /// Converts the Ascension Down item id to the appropriate AscensionLevel.
        /// </summary>
        public AscensionLevel ToAscensionLevel(APItem rawItemId)
        {

            switch(rawItemId)
            {
                case APItem.SwarmingElites:
                    return AscensionLevel.SwarmingElites;
                case APItem.WearyTraveler:
                    return AscensionLevel.WearyTraveler;
                case APItem.Poverty:
                    return AscensionLevel.Poverty;
                case APItem.TightBelt:
                    return AscensionLevel.TightBelt;
                case APItem.AscenderBane:
                    return AscensionLevel.AscendersBane;
                case APItem.Inflation:
                    return AscensionLevel.Inflation;
                case APItem.Scarcity:
                    return AscensionLevel.Scarcity;
                case APItem.ToughEnemies:
                    return AscensionLevel.ToughEnemies;
                case APItem.DeadlyEnemies:
                    return AscensionLevel.DeadlyEnemies;
                case APItem.DoubleBoss:
                    return AscensionLevel.DoubleBoss;
                default:
                    LogUtility.Error($"Got unrecognized ascension level {rawItemId}");
                    return AscensionLevel.None;
            }
        }

        /// <summary>
        /// Attempts to parse the AscensionLevel from the provided string, as the actual enum value.
        /// </summary>
        public AscensionLevel? GetLevel(string level)
        {
            if(Enum.TryParse(typeof(AscensionLevel), level, true, out var result))
            {
                return (AscensionLevel) result;
            }
            return null;
        }

        /// <summary>
        /// Removes the AscensionLevel from the current run.  If extra processing needs to occur to ensure this works as intended,
        /// this method will do so.
        /// </summary>
        public async void RemoveLevel(AscensionLevel level)
        {
            if(!CurrentAscension.Remove(level))
            {
                return;
            }

            switch(level)
            {
                case AscensionLevel.AscendersBane:
                    // Note: For some reason a very janky curse is rendered on screen when this fires.  I don't think the game
                    // was every setup for you to remove the Ascender's Bane.  Nothing problematic gameplay wise occurs though.
                    var card = GameUtility.CurrentPlayer?.Deck.Cards.Where((c) => c is AscendersBane).FirstOrDefault();
                    if(card != null)
                    {
                        await CardPileCmd.RemoveFromDeck(card);
                    }
                    break;
                case AscensionLevel.DoubleBoss:
                    var state = RunManager.Instance.DebugOnlyGetState();
                    if(state != null)
                    {
                        foreach(var act in state.Acts)
                        {
                            act.SetSecondBossEncounter(null);
                        }
                        var currentAct = state.Act;
                        if(currentAct.Index == 2)
                        {
                            var secondBossPoint = state.Map.SecondBossMapPoint;
                            if(secondBossPoint != null)
                            {
                                state.Map.BossMapPoint.RemoveChildPoint(secondBossPoint);
                            }
                        }
                    }
                    break;
                case AscensionLevel.TightBelt:
                    var player = GameUtility.CurrentPlayer;
                    if(player != null)
                    {
                        Callable.From(() => {
                                PlayerCmd.GainMaxPotionCount(1, player);
                                return;
                            }).CallDeferred();
                    }
                    break;
                default:
                    // Nothing to do with the other ascensions
                    return;
            }
        }

    }
}
