/**
 * @file Translates Filler Setup slider answers into canonical filler options.
 *
 * The UI stores four readable levels numbered 0 through 3. This section compiler
 * converts those semantic levels to the generated catalog's canonical choice names:
 * `none`, `low`, `medium`, and `high`. Raw Python numeric weights are deliberately
 * not duplicated here; the generated schema remains responsible for their meaning.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import { FILLER_ITEM_DEFINITIONS, FILLER_WEIGHT_NAMES } from "../FillerItem";
import type { FillerAnswers, FillerWeightLevel } from "../WizardAnswers";
import type { CompiledOptions } from "./applyCharacterOptions";

/**
 * Checks whether a value is one of the four supported slider levels.
 *
 * @param value - Player-facing filler level to inspect.
 * @returns Whether the value is an integer from zero through three.
 */
function isFillerWeightLevel(value: number): value is FillerWeightLevel {
  // Both integer shape and bounds matter because slider state may be manually altered.
  return Number.isInteger(value) && value >= 0 && value <= 3;
}

/**
 * Applies all player-facing filler weights to a compiler-owned option object.
 *
 * @param target - Fresh complete option object being assembled by the root compiler.
 * @param answers - Player-facing filler levels keyed by semantic filler IDs.
 * @param catalog - Generated schema used to confirm each canonical choice still exists.
 * @returns Nothing; replaces only options listed in `FILLER_ITEM_DEFINITIONS`.
 * @throws When a slider level is invalid or generated filler metadata has drifted.
 * @remarks Call only from the root compiler after its default option snapshot is built.
 */
export function applyFillerOptions(
  target: CompiledOptions,
  answers: FillerAnswers,
  catalog: OptionCatalog,
): void {
  // Compile every semantic filler answer through the shared definition mapping.
  for (const definition of FILLER_ITEM_DEFINITIONS) {
    const option = catalog.options[definition.optionKey];

    // Missing options indicate that the generated schema and guided mapping diverged.
    if (!option) {
      throw new Error(
        `Generated option catalog is missing '${definition.optionKey}'.`,
      );
    }

    const level = answers.weights[definition.id];

    // Defensively validate persisted or programmatically constructed answer state.
    if (!isFillerWeightLevel(level)) {
      throw new Error(
        `Filler weight '${definition.id}' must be an integer from 0 through 3.`,
      );
    }

    // Translate the ordered slider level into its canonical generated choice name.
    const choiceName = FILLER_WEIGHT_NAMES[level];
    let choiceExists = false;

    for (const choice of option.choices ?? []) {
      if (choice.name === choiceName) {
        choiceExists = true;
        break;
      }
    }

    // Fail loudly if a future schema changes or removes the four-level contract.
    if (!choiceExists) {
      throw new Error(
        `Generated filler option '${definition.optionKey}' does not accept '${choiceName}'.`,
      );
    }

    // Write the canonical choice string expected by YAML and final schema validation.
    target[definition.optionKey] = choiceName;
  }
}
