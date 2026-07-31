using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace StS2AP.Models
{
    public sealed class ApItemPotionModel : PotionModel
    {
        #region AP Item Data
        public string ApItemName { get; private set; } = string.Empty;
        public string ApPlayerName { get; private set; } = string.Empty;
        public ApItemClassification ApClassification { get; private set; } = ApItemClassification.Filler;
        public long ApLocationId { get; private set; } = -1;

        #endregion
        public override PotionRarity Rarity => PotionRarity.Common;
        public override PotionUsage Usage => default;
        public override TargetType TargetType => TargetType.None;
        public override IEnumerable<IHoverTip> ExtraHoverTips
        {
            get
            {
                var description = new LocString("ap", "shop_potion_description");
                description.Add("item_name", ApItemName);
                description.Add("player_name", ApPlayerName);
                description.Add("classification", ClassificationLabel);
                yield return new HoverTip(new LocString("ap", "shop_potion_title"), description);
            }
        }

        /// <summary>Short label ("Prog.", "Useful", "Filler", "Trap") shown in the extra HoverTip</summary>
        private string ClassificationLabel => ApClassification switch
        {
            ApItemClassification.Progression => "Prog.",
            ApItemClassification.Useful => "Useful",
            ApItemClassification.Trap => "Trap",
            _ => "Filler",
        };

        /// <summary>Stamps this mutable clone with the shop slot's Archipelago item data</summary>
        internal void Stamp(string itemName, string playerName, ApItemClassification classification, long locationId)
        {
            ApItemName = itemName;
            ApPlayerName = playerName;
            ApClassification = classification;
            ApLocationId = locationId;
        }
        public static ApItemPotionModel CreateForSlot(
            string itemName, string playerName, ApItemClassification classification, long locationId)
        {
            var mutable = (ApItemPotionModel)ModelDb.Potion<ApItemPotionModel>().ToMutable();
            mutable.Stamp(itemName, playerName, classification, locationId);
            return mutable;
        }

        /// <summary>Call once at mod init, alongside the card/relic RegisterAll()/Register()</summary>
        public static void Register() => ModelDb.Inject(typeof(ApItemPotionModel));
    }
}
