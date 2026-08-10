/**
 * @file Orchestrates the wizard compiler from player intent to canonical options.
 *
 * A compiler answers "what settings express these player choices?" It first rebuilds
 * every generated option from its catalog default, then lets section compilers replace
 * the fields they own. Finally, the separate validation layer checks that the completed
 * object is still legal according to the generated schema. Add each future section
 * compiler between default construction and the final validation call.
 */

import type { OptionCatalog, OptionValue } from "../../generated/optionCatalog";
import type { WizardAnswers } from "../WizardAnswers";
import { validateOptions } from "../validation/validateOptions";
import {
  applyCharacterOptions,
  type CompiledOptions,
} from "./applyCharacterOptions";
import { applyChecksAndRewardsOptions } from "./applyChecksAndRewardsOptions";
import { applyDeathLinkOptions } from "./applyDeathLinkOptions";
import { applyProgressionOptions } from "./applyProgressionOptions";
import { applyRunOptions } from "./applyRunOptions";

/**
 * Compiles all guided answers into a complete Archipelago option configuration.
 *
 * @param answers - Player-facing intent collected by the guided wizard.
 * @param catalog - Generated metadata describing every accepted Archipelago option.
 * @returns A new, complete option object in generated catalog order.
 * @throws When the catalog is incomplete, a section cannot express the answers, or
 * the final configuration fails schema validation.
 * @remarks This pure boundary must be rerun from all answers after every UI change.
 * Callers must not preserve and incrementally mutate its previous return value.
 */
export function compileWizardAnswers(
  answers: WizardAnswers,
  catalog: OptionCatalog,
): CompiledOptions {
  // Rebuild the baseline from schema defaults so stale values cannot survive edits.
  const options: CompiledOptions = {};

  for (const key of catalog.option_order) {
    const option = catalog.options[key];

    // Catalog ordering and catalog entries must remain internally consistent.
    if (!option) {
      throw new Error(`Generated option catalog is missing '${key}'.`);
    }

    // Clone collection defaults so compiled output never mutates imported JSON.
    options[key] = structuredClone(option.default as OptionValue);
  }

  // Section compilers own the only mapping from player concepts to option keys.
  applyCharacterOptions(options, answers.characters, catalog);
  applyRunOptions(options, answers.run, catalog);
  applyChecksAndRewardsOptions(options, answers.checksAndRewards, catalog);
  applyDeathLinkOptions(options, answers.deathLink, catalog);
  applyProgressionOptions(options, answers.progression, catalog);

  // Validation is deliberately last: it checks the final product, not player intent.
  validateOptions(options, catalog);

  // Return the newly constructed snapshot for display or YAML serialization.
  return options;
}
