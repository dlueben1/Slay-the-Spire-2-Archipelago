from BaseClasses import Tutorial
from worlds.AutoWorld import WebWorld
from Options import DeathLink, OptionGroup
from .options import (
    Characters, ModdedCharacters, DeathLinkDamagePercent, EnableDeathFragments, PickNumberCharacters, GoalNumChar,
    LockCharacters, UnlockedCharacter, Ascension, AscensionDown, AdvancedChar, CharacterOptions,
    IncludeFloorChecks, NeowSanity, CampfireSanity, GoldSanity, PotionSanity,
    ShopSanity, ShopCardSlots, ShopNeutralSlots, ShopRelicSlots, ShopPotionSlots, ShopRemoveSlots, ShopSanityCosts,
    AncientRelicLocation, AncientRelicPool, RelicRewardsAvailableAnytime, ReleaseOnVictory,
    CardReward, ProgressiveStarterCard, ProgressiveStarterRelic, BonusItems, SeededRun,
    OneGoldFillerWeight, FiveGoldFillerWeight,
    FreeAttackFillerWeight, FreePowerFillerWeight, FreeSkillFillerWeight,
    DexterityFillerWeight, StrengthFillerWeight, PlatingFillerWeight,
    FriendshipFillerWeight, PostCombatCardUpgradeFillerWeight, PostCombatCardRemovalFillerWeight,
    AdditionalCardRewardFillerWeight, BufferFillerWeight, VigorFillerWeight, ThornsFillerWeight, ArtifactFillerWeight,
    SingleColorlessCardFillerWeight,
)

# The info needed for the Archipelago Website, not the actual `apworld`
class SlayTheSpire2Web(WebWorld):
    tutorials = [
        Tutorial(
            "Setup Guide",
            "A very small test world for Slay the Spire II.",
            "English",
            "setup_en.md",
            "setup/en",
            ["Kirbyfanner", "Platano Bailando", "Lyxn", "Terairk"]
        )
    ]

    option_groups = [
        OptionGroup("Character Options", [
            Characters,
            ModdedCharacters,
            PickNumberCharacters,
            GoalNumChar,
            LockCharacters,
            UnlockedCharacter,
            Ascension,
            AscensionDown,
        ]),
        OptionGroup("Game Options", [
            AncientRelicLocation,
            AncientRelicPool,
            RelicRewardsAvailableAnytime,
            ProgressiveStarterCard,
            ProgressiveStarterRelic,
            CardReward,
        ]),        
        OptionGroup("Sanities", [
            IncludeFloorChecks,
            NeowSanity,
            CampfireSanity,
            GoldSanity,
            PotionSanity,
            ShopSanity,
            ShopCardSlots,
            ShopNeutralSlots,
            ShopRelicSlots,
            ShopPotionSlots,
            ShopRemoveSlots,
            ShopSanityCosts,
        ]),
        OptionGroup("Death Link", [
            DeathLink,
            EnableDeathFragments,
            DeathLinkDamagePercent,
        ]),
        OptionGroup("Advanced Options", [
            ReleaseOnVictory,
            SeededRun,
            AdvancedChar,
            CharacterOptions,
        ], start_collapsed=True),
        OptionGroup("Bonus Items", [
            BonusItems,
        ]),
        OptionGroup("Filler Items", [
            OneGoldFillerWeight,
            FiveGoldFillerWeight,
            FreeAttackFillerWeight,
            FreePowerFillerWeight,
            FreeSkillFillerWeight,
            DexterityFillerWeight,
            StrengthFillerWeight,
            PlatingFillerWeight,
            FriendshipFillerWeight,
            PostCombatCardUpgradeFillerWeight,
            PostCombatCardRemovalFillerWeight,
            AdditionalCardRewardFillerWeight,
            BufferFillerWeight,
            VigorFillerWeight,
            ThornsFillerWeight,
            ArtifactFillerWeight,
            #SingleColorlessCardFillerWeight,
        ]),
    ]
