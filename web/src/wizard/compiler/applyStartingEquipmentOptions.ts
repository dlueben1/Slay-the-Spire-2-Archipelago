/**
 * @file Translates the Starting Equipment subsection into progressive item toggles.
 *
 * Starting Equipment lives inside the combined Checks & Rewards step because both
 * progressive options consume Floor Check filler slots. This compiler preserves that
 * Python-world dependency explicitly: when Floor Checks are disabled, both generated
 * toggles are normalized to false even if stale UI state reaches the compiler. The
 * root compiler invokes this module before the other Checks & Rewards families, while
 * generated-schema validation remains responsible for primitive option validity.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import type { StartingEquipmentAnswers } from "../WizardAnswers";
import { STARTING_EQUIPMENT_OPTION_KEYS } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

/**
 * Applies progressive starter-card and starter-relic choices to generated options.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param answers - Player-facing progressive Starting Equipment choices.
 * @param includeFloorChecks - Whether the required Floor Check item budget exists.
 * @param catalog - Generated schema used to detect missing owned options.
 * @returns Nothing; replaces only options in `STARTING_EQUIPMENT_OPTION_KEYS`.
 * @throws When a regenerated catalog no longer contains an owned option.
 * @remarks Call only from the combined Checks & Rewards compiler so the Floor Checks
 * dependency is supplied from the same immutable answer snapshot.
 */
export function applyStartingEquipmentOptions(
  target: CompiledOptions,
  answers: StartingEquipmentAnswers,
  includeFloorChecks: boolean,
  catalog: OptionCatalog,
): void {
  // Detect schema drift before writing either member of this dependent option family.
  for (const optionKey of Object.values(STARTING_EQUIPMENT_OPTION_KEYS)) {
    if (!catalog.options[optionKey]) {
      throw new Error(`Generated option catalog is missing '${optionKey}'.`);
    }
  }

  // Match Python's early-generation normalization when no Floor Check budget exists.
  const progressiveStarterCard =
    includeFloorChecks && answers.progressiveStarterCard;
  const progressiveStarterRelic =
    includeFloorChecks && answers.progressiveStarterRelic;

  // Write canonical booleans so review YAML reflects the configuration Python will use.
  target[STARTING_EQUIPMENT_OPTION_KEYS.progressiveStarterCard] =
    progressiveStarterCard;
  target[STARTING_EQUIPMENT_OPTION_KEYS.progressiveStarterRelic] =
    progressiveStarterRelic;
}
