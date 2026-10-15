# 10,000 this time to create plenty of space for items and locations
CHAR_OFFSET = 10000

# Maximum selected modded characters, including predefined and unnamed characters.
NUM_CUSTOM = 20

# These are permanent ID ranges, not counts of currently installed characters.
VANILLA_CHARACTER_BLOCKS = range(1, 11)
GENERIC_CHARACTER_BLOCKS = range(11, 31)
PREDEFINED_CHARACTER_BLOCKS = range(40, 100)

ASCENSIONS = {
    'SwarmingElites': 'Disable Swarming Elites',
    'WearyTraveler': 'Disable Weary Traveler',
    'Poverty': 'Disable Poverty',
    'TightBelt': 'Disable Tight Belt',
    "AscendersBane": "Disable Ascender's Bane",
    'Inflation': 'Disable Inflation',
    'Scarcity': 'Disable Scarcity',
    'ToughEnemies': 'Disable Tough Enemies',
    'DeadlyEnemies': 'Disable Deadly Enemies',
    'DoubleBoss': 'Disable Double Boss',
}

ASCENSION_LIST = list(ASCENSIONS.keys())
