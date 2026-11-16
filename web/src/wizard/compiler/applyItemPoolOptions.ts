/**
 * @file Compiles the Item Pool step: Bonus Items, filler weights and traps.
 *
 * All three share the filler slots the Python world creates, so they sit on one step:
 * Bonus Items take slots first, and traps replace some of the remaining filler.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import type { ItemPoolAnswers } from "../WizardAnswers";
import { applyBonusItemOptions } from "./applyBonusItemOptions";
import type { CompiledOptions } from "./applyCharacterOptions";
import { applyFillerOptions } from "./applyFillerOptions";
import { applyTrapOptions } from "./applyTrapOptions";

/**
 * Applies every option owned by the Item Pool step.
 *
 * @param target - Mutable complete option snapshot being assembled by the root compiler.
 * @param answers - Bonus Item, filler weight and trap intent.
 * @param catalog - Generated schema used by each focused option-family compiler.
 * @returns Nothing; the supplied option snapshot is updated in place.
 */
export function applyItemPoolOptions(
  target: CompiledOptions,
  answers: ItemPoolAnswers,
  catalog: OptionCatalog,
): void {
  applyBonusItemOptions(target, answers.bonusItems, catalog);
  applyFillerOptions(target, answers.filler, catalog);
  applyTrapOptions(target, answers.traps);
}
