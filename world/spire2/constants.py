# 10,000 this time to create plenty of space for items and locations
CHAR_OFFSET = 10000

# Maximum selected modded characters, including predefined and unnamed characters.
NUM_CUSTOM = 20

# These are permanent ID ranges, not counts of currently installed characters.
# Only the first NUM_CUSTOM generic blocks are registered, because each roster fills them in
# order; raising NUM_CUSTOM adds blocks without moving any existing ID.
# IDs must stay below 2**31; the last predefined block ends below 10,000,000.
VANILLA_CHARACTER_BLOCKS = range(1, 11)
GENERIC_CHARACTER_BLOCKS = range(11, 200)
PREDEFINED_CHARACTER_BLOCKS = range(200, 1000)

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
