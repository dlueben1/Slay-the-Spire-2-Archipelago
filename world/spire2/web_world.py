from BaseClasses import Tutorial
from worlds.AutoWorld import WebWorld
from Options import DeathLink, OptionGroup
from .options import (
    Characters, DeathLinkType, PickNumberCharacters, GoalNumChar,
    LockCharacters, UnlockedCharacter, Ascension,
    IncludeFloorChecks, CampfireSanity, GoldSanity, PotionSanity,
    CardReward,
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
            ["Kirbyfanner"]
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
        ]),
        OptionGroup("Death Link", [
            DeathLink,
            DeathLinkType,
        ])
    ]
