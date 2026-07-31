using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;

namespace StS2AP.Models
{
    public enum ApItemClassification
    {
        Progression,
        Useful,
        Filler,
        Trap,
    }
    public abstract class ApItemCardModelBase : CardModel
    {
        #region AP Item Data

        /// <summary>Display name of the Archipelago item this shop slot represents</summary>
        public string ApItemName { get; private set; } = string.Empty;

        /// <summary>Name of the Archipelago player who will ultimately receive this item</summary>
        public string ApPlayerName { get; private set; } = string.Empty;
        public long ApLocationId { get; private set; } = -1;

        #endregion

        protected ApItemCardModelBase(CardRarity rarity)
            : base(-1, CardType.Skill, rarity, TargetType.None)
        {
        }

        /// <summary>Short label ("Prog.", "Useful", "Filler", "Trap") baked into this leaf's description.</summary>
        protected abstract string ClassificationLabel { get; }
        public override CardPoolModel Pool => ModelDb.CardPool<CurseCardPool>();

        /// <summary>Just a location-check vessel(deltarune reference(sorry I had to lol))</summary>
        public override int MaxUpgradeLevel => 0;

        /// <summary>Keeps this from being spawned by "add a random card" style effects</summary>
        public override bool CanBeGeneratedByModifiers => false;

        /// <summary>Unplayable so it can't be played from hand; Eternal so it can't be removed</summary>
        public override IEnumerable<CardKeyword> CanonicalKeywords
            => new[] { CardKeyword.Unplayable, CardKeyword.Eternal };
        protected override void AddExtraArgsToDescription(LocString description)
        {
            description.Add("item_name", ApItemName);
            description.Add("player_name", ApPlayerName);
            description.Add("classification", ClassificationLabel);
        }

        /// <summary>Stamps this mutable clone with the shop slot's Archipelago item data</summary>
        internal void Stamp(string itemName, string playerName, long locationId)
        {
            ApItemName = itemName;
            ApPlayerName = playerName;
            ApLocationId = locationId;
        }

        public static ApItemCardModelBase CreateForSlot(
            string itemName, string playerName, ApItemClassification classification, long locationId)
        {
            ApItemCardModelBase mutable = classification switch
            {
                ApItemClassification.Progression => (ApItemCardModelBase)ModelDb.Card<ApItemCardProgression>().ToMutable(),
                ApItemClassification.Useful       => (ApItemCardModelBase)ModelDb.Card<ApItemCardUseful>().ToMutable(),
                ApItemClassification.Trap         => (ApItemCardModelBase)ModelDb.Card<ApItemCardTrap>().ToMutable(),
                _                                  => (ApItemCardModelBase)ModelDb.Card<ApItemCardFiller>().ToMutable(),
            };
            mutable.Stamp(itemName, playerName, locationId);
            return mutable;
        }

        /// <summary>Call once at mod init before any shop screen can open</summary>
        public static void RegisterAll()
        {
            ModelDb.Inject(typeof(ApItemCardProgression));
            ModelDb.Inject(typeof(ApItemCardUseful));
            ModelDb.Inject(typeof(ApItemCardFiller));
            ModelDb.Inject(typeof(ApItemCardTrap));
        }
    }
    public sealed class ApItemCardProgression : ApItemCardModelBase
    {
        public ApItemCardProgression() : base(CardRarity.Rare) { }
        protected override string ClassificationLabel => "Prog.";
        public override string PortraitPath
            => "res://.godot/imported/progression.png-1a868e33a86b46c0acaf1ccb2628a834.ctex";
    }
    public sealed class ApItemCardUseful : ApItemCardModelBase
    {
        public ApItemCardUseful() : base(CardRarity.Uncommon) { }
        protected override string ClassificationLabel => "Useful";
        public override string PortraitPath
            => "res://.godot/imported/useful.png-416e19b0a04e757192c7a97b3d0e44df.ctex";
    }
    public sealed class ApItemCardFiller : ApItemCardModelBase
    {
        public ApItemCardFiller() : base(CardRarity.Common) { }
        protected override string ClassificationLabel => "Filler";
        public override string PortraitPath
            => "res://.godot/imported/filler.png-77630ffc1a28b10b9bd254812bf18d16.ctex";
    }
    public sealed class ApItemCardTrap : ApItemCardModelBase
    {
        public ApItemCardTrap() : base(CardRarity.Curse) { }
        protected override string ClassificationLabel => "Trap";
        public override string PortraitPath
            => "res://.godot/imported/trap.png-8773981e5356b07fbd009b3300e98ad6.ctex";
    }
}
