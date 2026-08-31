/**
 * @file Declares which generated option keys belong to each guided wizard section.
 *
 * This is the technical registry between the player-facing answer model and the
 * generated Archipelago catalog. Components must not import these keys; section
 * compilers use them to write canonical options, while the review pipeline uses the
 * same registry to select guided YAML fields. Add a new key here when expanding a
 * section, then update that section's answers, component, compiler, and tests.
 */

import type { OptionCatalog } from "../generated/optionCatalog";

/** Generated option keys owned by the Character Setup compiler in both modes. */
export const CHARACTER_OPTION_KEYS = {
  characters: "characters",
  moddedCharacters: "modded_characters",
  pickNumCharacters: "pick_num_characters",
  numCharactersGoal: "num_chars_goal",
  lockCharacters: "lock_characters",
  unlockedCharacter: "unlocked_character",
  useAdvancedCharacters: "use_advanced_characters",
  advancedCharacters: "advanced_characters",
  ascension: "ascension",
  ascensionDown: "ascension_down",
} as const;

/** Generated option keys owned by the Gameplay Modifiers compiler. */
export const RUN_OPTION_KEYS = {
  relicRewardsAvailableAnytime: "relic_rewards_available_anytime",
  releaseOnVictory: "release_on_victory",
  seeded: "seeded",
} as const;

/** Generated option keys owned by Checks & Rewards' Starting Equipment subsection. */
export const STARTING_EQUIPMENT_OPTION_KEYS = {
  progressiveStarterCard: "progressive_starter_card",
  progressiveStarterRelic: "progressive_starter_relic",
} as const;

/** Generated option keys owned by the Ancient subsection of Checks & Rewards. */
export const ANCIENT_OPTION_KEYS = {
  relicLocation: "ancient_relic_location",
  relicPool: "ancient_relic_pool",
} as const;

/** Generated option keys owned by the Progression compiler. */
export const PROGRESSION_OPTION_KEYS = {
  progressionBalancing: "progression_balancing",
  accessibility: "accessibility",
} as const;

/** Generated option keys owned by the Checks & Rewards compiler. */
export const CHECK_OPTION_KEYS = {
  neowSanity: "neow_sanity",
  includeFloorChecks: "include_floor_checks",
  campfireSanity: "campfire_sanity",
  goldSanity: "gold_sanity",
  potionSanity: "potion_sanity",
  shuffleAllCards: "shuffle_all_cards",
} as const;

/** Generated option keys owned by the Shop compiler. */
export const SHOP_OPTION_KEYS = {
  enabled: "shop_sanity",
  cardSlots: "shop_card_slots",
  neutralCardSlots: "shop_neutral_card_slots",
  relicSlots: "shop_relic_slots",
  potionSlots: "shop_potion_slots",
  removeSlots: "shop_remove_slots",
  costs: "shop_sanity_costs",
} as const;

/** Generated option key owned by the Bonus Items compiler. */
export const BONUS_ITEM_OPTION_KEY = "bonus_items" as const;

/** Generated option keys owned by the Death Link compiler. */
export const DEATH_LINK_OPTION_KEYS = {
  enabled: "death_link",
  enableFragments: "enable_death_fragments",
  damagePercent: "death_link_damage_percent",
} as const;

export interface GeneratedNumberRange {
  minimum: number;
  maximum: number;
}

/**
 * Reads required numeric bounds for a guided range control.
 *
 * @param catalog - Generated option catalog containing authoritative bounds.
 * @param optionKey - Generated range option whose bounds are required by the UI.
 * @returns Inclusive minimum and maximum values for the control.
 * @throws When the option is absent or does not expose both numeric bounds.
 */
export function getGeneratedNumberRange(
  catalog: OptionCatalog,
  optionKey: string,
): GeneratedNumberRange {
  // Locate the generated entry before inspecting its range metadata.
  const option = catalog.options[optionKey];

  if (!option) {
    throw new Error(`Generated option catalog is missing '${optionKey}'.`);
  }

  // Range-backed controls require both endpoints to remain schema-driven.
  if (option.minimum === undefined || option.maximum === undefined) {
    throw new Error(`Generated option '${optionKey}' has no numeric range.`);
  }

  // Return a small immutable-by-convention presentation model.
  return {
    minimum: option.minimum,
    maximum: option.maximum,
  };
}
