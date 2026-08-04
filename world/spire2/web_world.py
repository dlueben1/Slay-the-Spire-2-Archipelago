from BaseClasses import Tutorial
from worlds.AutoWorld import WebWorld
from Options import DeathLink, OptionGroup
from .options import (
    Characters, DeathLinkDamagePercent, EnableDeathFragments, PickNumberCharacters, GoalNumChar,
    LockCharacters, UnlockedCharacter, Ascension,
    IncludeFloorChecks, CampfireSanity, GoldSanity, PotionSanity, AncientChaos,
    CardReward,
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
            ["Kirbyfanner", "Platano Bailando", "Lyxn"]
        )
    ]

    option_groups = [
        OptionGroup("Character Options", [
            Characters,
            PickNumberCharacters,
            GoalNumChar,
            LockCharacters,
            UnlockedCharacter,
        ]),
        OptionGroup("Sanities", [
            IncludeFloorChecks,
            CampfireSanity,
            GoldSanity,
            PotionSanity,
            CardReward,
            AncientChaos,
        ]),
        OptionGroup("Death Link", [
            DeathLink,
            EnableDeathFragments,
            DeathLinkDamagePercent,
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
