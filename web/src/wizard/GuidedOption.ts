/**
 * @file Selects the generated options explicitly owned by guided wizard sections.
 *
 * The root compiler intentionally produces every catalog option so validation can
 * catch schema drift. Review YAML is narrower: it shows only settings the player was
 * asked about. This module assembles that ordered ownership list from the same section
 * registries used by compilers, preventing the view from maintaining a second mapping.
 */

import type { OptionValue } from "../generated/optionCatalog";
import { FILLER_ITEM_DEFINITIONS } from "./FillerItem";
import {
  CHARACTER_OPTION_KEYS,
  ANCIENT_OPTION_KEYS,
  BONUS_ITEM_OPTION_KEY,
  CHECK_OPTION_KEYS,
  DEATH_LINK_OPTION_KEYS,
  PROGRESSION_OPTION_KEYS,
  RUN_OPTION_KEYS,
  SHOP_OPTION_KEYS,
  STARTING_EQUIPMENT_OPTION_KEYS,
} from "./WizardOptionKey";

/** Basic character keys omitted when the advanced dictionary is authoritative. */
const BASIC_CHARACTER_OPTION_KEYS = new Set([
  "characters",
  "modded_characters",
  "ascension",
  "ascension_down",
]);

/** Advanced character payload omitted while shared Ascensions are authoritative. */
const ADVANCED_CHARACTER_OPTION_KEY = "advanced_characters";

/**
 * Builds guided option keys in the same order as their wizard steps.
 *
 * @returns A fresh ordered list covering every implemented section compiler.
 * @remarks Character Setup owns both standard and advanced generated representations,
 * while inherited Archipelago template options without controls remain absent.
 */
export function getGuidedOptionKeys(): string[] {
  // Begin with each fixed section registry in navigation order.
  const optionKeys: string[] = [
    ...CHARACTER_OPTION_KEYS,
    ...Object.values(RUN_OPTION_KEYS),
    ...Object.values(STARTING_EQUIPMENT_OPTION_KEYS),
    ...Object.values(ANCIENT_OPTION_KEYS),
    ...Object.values(CHECK_OPTION_KEYS),
    ...Object.values(SHOP_OPTION_KEYS),
    BONUS_ITEM_OPTION_KEY,
  ];

  // Filler keys come from their semantic item definitions rather than a duplicate list.
  for (const definition of FILLER_ITEM_DEFINITIONS) {
    optionKeys.push(definition.optionKey);
  }

  // Death Link follows the combined Checks & Rewards ownership group.
  optionKeys.push(...Object.values(DEATH_LINK_OPTION_KEYS));

  // Common Archipelago settings close the dedicated Progression step.
  optionKeys.push(...Object.values(PROGRESSION_OPTION_KEYS));

  // Return the complete ordered ownership snapshot.
  return optionKeys;
}

/**
 * Checks whether a guided option has meaning in the compiled character mode.
 *
 * @param optionKey - Registered generated option under consideration.
 * @param usesAdvancedCharacters - Whether the advanced character dictionary is active.
 * @returns Whether review YAML should include the option in the current mode.
 * @remarks The complete compiler still emits every schema key for validation. This
 * filter removes only ignored character fields from the player-facing YAML preview.
 */
function isActiveGuidedOption(
  optionKey: string,
  usesAdvancedCharacters: boolean,
): boolean {
  // Advanced mode replaces the basic roster and shared Ascension arrays.
  if (usesAdvancedCharacters && BASIC_CHARACTER_OPTION_KEYS.has(optionKey)) {
    return false;
  }

  // Shared mode does not need to expose the ignored advanced dictionary payload.
  if (!usesAdvancedCharacters && optionKey === ADVANCED_CHARACTER_OPTION_KEY) {
    return false;
  }

  // Shared questions and all non-character sections remain visible in either mode.
  return true;
}

/**
 * Selects guided values from a complete compiled option object.
 *
 * @param compiled - Complete validated output from the root wizard compiler.
 * @returns An ordered record suitable for guided YAML review.
 * @throws When a registered guided option is unexpectedly absent.
 */
export function selectGuidedOptions(
  compiled: Record<string, OptionValue>,
): Record<string, OptionValue> {
  // Preserve step order by collecting entries from the central ownership registry.
  const guidedEntries: [string, OptionValue][] = [];
  const usesAdvancedCharacters = compiled.use_advanced_characters === true;

  for (const optionKey of getGuidedOptionKeys()) {
    // Omit technical character fields that the selected YAML mode ignores.
    if (!isActiveGuidedOption(optionKey, usesAdvancedCharacters)) {
      continue;
    }

    const value = compiled[optionKey];

    if (value === undefined) {
      throw new Error(
        `Compiled options are missing guided key '${optionKey}'.`,
      );
    }

    guidedEntries.push([optionKey, value]);
  }

  // Convert ordered entries to the serializer's expected mapping shape.
  return Object.fromEntries(guidedEntries);
}
