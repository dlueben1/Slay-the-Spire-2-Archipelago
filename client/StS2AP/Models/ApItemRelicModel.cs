using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.HoverTips;
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
        // goober godot requires explicit texture definitions for different UI states
        protected override string BigIconPath
            => "res://.godot/imported/APIcon.png-b030ed7a050dcd9ae78eaea3be50ed9f.ctex";
        protected override string PackedIconOutlinePath
            => "res://.godot/imported/APIcon.png-b030ed7a050dcd9ae78eaea3be50ed9f.ctex";

        protected override IEnumerable<IHoverTip> ExtraHoverTips
        {
            get
            {
                var title = new LocString("relics", "shop_relic_title");
                title.Add("item_name", ApItemName);
                title.Add("classification", ClassificationLabel);

                var description = new LocString("relics", "shop_relic_description");
                description.Add("item_name", ApItemName);
                description.Add("player_name", ApPlayerName);
                description.Add("classification", ClassificationLabel);

                yield return new HoverTip(title, description);
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
