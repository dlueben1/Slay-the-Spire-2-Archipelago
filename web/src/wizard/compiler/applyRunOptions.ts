/**
 * @file Translates Run Settings answers into generated relic and seed options.
 *
 * The Run Settings step groups settings that change how an individual climb is offered
 * and rewarded without changing the selected character roster. This compiler owns the
 * immediate Relic availability, overpowered-relic removal, victory release, and seeded-run
 * fields. Generic catalog validation still runs after every section.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import type { OpRelicRemovalAnswers, RunAnswers } from "../WizardAnswers";
import { OP_RELIC_CATEGORY_KEYS, RUN_OPTION_KEYS } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";

/**
 * Lists the generated `remove_op_relics` categories selected by the player.
 *
 * @param answers - One boolean per overpowered-relic category.
 * @param catalog - Generated schema that must still accept every category name.
 * @returns Selected category names in registry order; empty keeps every relic.
 * @throws When a regenerated catalog no longer accepts a category name.
 */
function compileOpRelicCategories(
  answers: OpRelicRemovalAnswers,
  catalog: OptionCatalog,
): string[] {
  // Detect a renamed or removed category before writing a list the generator would reject.
  const validKeys =
    catalog.options[RUN_OPTION_KEYS.removeOpRelics]?.valid_keys ?? [];

  const selected: string[] = [];
  for (const [field, category] of Object.entries(OP_RELIC_CATEGORY_KEYS)) {
    if (!validKeys.includes(category)) {
      throw new Error(
        `Generated option '${RUN_OPTION_KEYS.removeOpRelics}' no longer accepts '${category}'.`,
      );
    }

    if (answers[field as keyof OpRelicRemovalAnswers]) {
      selected.push(category);
    }
  }
  return selected;
}

/**
 * Applies player-facing Run Settings answers to a compiler-owned option object.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param answers - Player-facing Relic availability and removal, victory, and seed choices.
 * @param catalog - Generated schema used to detect missing owned options.
 * @returns Nothing; replaces only options declared in `RUN_OPTION_KEYS`.
 * @throws When a regenerated catalog no longer contains an owned option.
 */
export function applyRunOptions(
  target: CompiledOptions,
  answers: RunAnswers,
  catalog: OptionCatalog,
): void {
  // Detect schema drift before writing any part of this section.
  for (const optionKey of Object.values(RUN_OPTION_KEYS)) {
    if (!catalog.options[optionKey]) {
      throw new Error(`Generated option catalog is missing '${optionKey}'.`);
    }
  }

  // Map semantic answers to the canonical generated fields owned by this section.
  target[RUN_OPTION_KEYS.relicRewardsAvailableAnytime] =
    answers.relicRewardsAvailableAnytime;
  target[RUN_OPTION_KEYS.removeOpRelics] = compileOpRelicCategories(
    answers.removeOpRelics,
    catalog,
  );
  target[RUN_OPTION_KEYS.releaseOnVictory] = answers.releaseOnVictory;
  target[RUN_OPTION_KEYS.seeded] = answers.seeded;
}
