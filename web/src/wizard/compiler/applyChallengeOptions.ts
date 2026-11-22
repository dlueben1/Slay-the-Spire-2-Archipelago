/**
 * @file Translates Challenges answers into generated difficulty options.
 *
 * Every option this compiler owns makes runs harder without changing logic. It writes
 * the Run Modifier set, the per-act enemy scaling percentage, the Sight item counts of the
 * two hidden-information challenges, and Strict Card Rewards. Generic catalog validation still runs afterwards.
 *
 * Challenges are opt-in: while the player's switch is off, the generated defaults
 * written by the root compiler stay in place, even if challenges were ticked earlier.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import { getOrderedOptionNames } from "../ModifierTableRow";
import { RUN_MODIFIERS } from "../RunModifier";
import { getSightBonusNames, SIGHT_COPY_COUNTS, SIGHTS } from "../Sight";
import type { ChallengeAnswers } from "../WizardAnswers";
import { CHALLENGE_OPTION_KEYS } from "../WizardOptionKey";
import type { CompiledOptions } from "./applyCharacterOptions";
import { requireAcceptedNames } from "./requireAcceptedNames";

/**
 * Applies player-facing Challenges answers to a compiler-owned option object.
 *
 * @param target - Fresh complete option object assembled by the root compiler.
 * @param answers - Selected Run Modifiers, enemy scaling, and challenge toggles.
 * @param catalog - Generated schema used to detect missing options and modifier names.
 * @returns Nothing; replaces only options declared in `CHALLENGE_OPTION_KEYS`.
 * @throws When a regenerated catalog no longer contains an owned option or accepts a
 * selected Run Modifier.
 */
export function applyChallengeOptions(
  target: CompiledOptions,
  answers: ChallengeAnswers,
  catalog: OptionCatalog,
): void {
  // Detect schema drift before writing any part of this section.
  for (const optionKey of Object.values(CHALLENGE_OPTION_KEYS)) {
    if (!catalog.options[optionKey]) {
      throw new Error(`Generated option catalog is missing '${optionKey}'.`);
    }
  }

  // Keep the root compiler's defaults; ticked challenges stay in the answers only.
  if (!answers.enabled) {
    return;
  }

  const runModifiers = getOrderedOptionNames(
    RUN_MODIFIERS,
    answers.runModifiers,
  );
  requireAcceptedNames(
    catalog,
    CHALLENGE_OPTION_KEYS.runModifiers,
    runModifiers,
  );
  const sightBonuses = getSightBonusNames(answers.sightBonuses);
  requireAcceptedNames(
    catalog,
    CHALLENGE_OPTION_KEYS.sightBonuses,
    sightBonuses,
  );

  target[CHALLENGE_OPTION_KEYS.runModifiers] = runModifiers;
  target[CHALLENGE_OPTION_KEYS.enemyHpScaling] = answers.enemyHpScaling;
  target[CHALLENGE_OPTION_KEYS.enemyDamageScaling] = answers.enemyDamageScaling;
  target[CHALLENGE_OPTION_KEYS.dreadMode] = answers.dreadMode;
  for (const sight of SIGHTS) {
    target[CHALLENGE_OPTION_KEYS[sight.copiesField]] =
      SIGHT_COPY_COUNTS[answers[sight.copiesField]];
  }
  target[CHALLENGE_OPTION_KEYS.sightBonuses] = sightBonuses;
  target[CHALLENGE_OPTION_KEYS.strictCardRewards] = answers.strictCardRewards;
}
