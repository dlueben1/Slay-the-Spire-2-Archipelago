# 10,000 this time to create plenty of space for items and locations
CHAR_OFFSET = 10000

# Maximum selected characters without a predefined block, i.e. registered "Custom Character N" blocks.
# Vanilla and predefined modded characters have their own blocks and do not count against it.
NUM_CUSTOM = 20

# These are permanent ID ranges, not counts of currently installed characters.
# Only the first NUM_CUSTOM generic blocks are registered, because each roster fills them in
# order; raising NUM_CUSTOM adds blocks without moving any existing ID.
# the last predefined block ends below 10,000,000 which still leaves plenty of room

# the devs plan on making 8 ish characters in the base game I believe
VANILLA_CHARACTER_BLOCKS = range(1, 11)

# 11 - 200 allows any users - for whatever reason to only need to change NUM_CUSTOM
# in order to get more modded character support
GENERIC_CHARACTER_BLOCKS = range(11, 200)

# Big range so that we can have a lot of potential predefined characters
# Will be more relevant when dynamic datapackages come out
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
