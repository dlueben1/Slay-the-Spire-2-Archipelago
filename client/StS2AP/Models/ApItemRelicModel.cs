using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace StS2AP.Models
{
    public sealed class ApItemRelicModel : RelicModel
    {
        #region AP Item Data
        public string ApItemName { get; private set; } = string.Empty;
        public string ApPlayerName { get; private set; } = string.Empty;
        public ApItemClassification ApClassification { get; private set; } = ApItemClassification.Filler;
        public long ApLocationId { get; private set; } = -1;

        #endregion

        public override RelicRarity Rarity => RelicRarity.Shop;
        public override string PackedIconPath
            => "res://.godot/imported/APIcon.png-b030ed7a050dcd9ae78eaea3be50ed9f.ctex";
        public override LocString Title
        {
            get
            {
                var title = new LocString("ap", "shop_relic_title");
                title.Add("item_name", ApItemName);
                title.Add("player_name", ApPlayerName);
                title.Add("classification", ClassificationLabel);
                return title;
            }
        }

        /// <summary>Short label ("Prog.", "Useful", "Filler", "Trap") appended to the relic's title</summary>
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
        public static ApItemRelicModel CreateForSlot(
            string itemName, string playerName, ApItemClassification classification, long locationId)
        {
            var mutable = (ApItemRelicModel)ModelDb.Relic<ApItemRelicModel>().ToMutable();
            mutable.Stamp(itemName, playerName, classification, locationId);
            return mutable;
        }
        public static void Register() => ModelDb.Inject(typeof(ApItemRelicModel));
    }
}
