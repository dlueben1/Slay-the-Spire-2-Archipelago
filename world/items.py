from BaseClasses import ItemClassification

START_ID = 900000

item_table = {
    "Gold Reward": {
        "id": START_ID + 1,
        "classification": ItemClassification.progression
    },
    "Card Reward": {
        "id": START_ID + 2,
        "classification": ItemClassification.progression
    }
}