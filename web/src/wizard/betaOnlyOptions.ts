/**
 * @file Finds option choices that need a game feature only the game's beta branch has.
 *
 * The APWorld cannot know which game branch a player runs, so it marks such choices and
 * the mod refuses a slot that needs a feature its game version lacks. The wizard reads
 * the same marker from the generated schema to label those choices and to warn on Review.
 */

import { optionCatalog, type OptionCatalog } from "../generated/optionCatalog";
import type { ChallengeAnswers } from "./WizardAnswers";
import { CHALLENGE_OPTION_KEYS } from "./WizardOptionKey";

/**
 * Checks whether one choice of an option needs a game feature only the game's beta branch has.
 *
 * @param key - Generated option key.
 * @param choice - Generated choice name.
 * @param catalog - Generated schema naming the game features choices require.
 * @returns Whether the choice is marked beta-only.
 */
export function isBetaOnlyChoice(
  key: string,
  choice: string,
  catalog: OptionCatalog = optionCatalog,
): boolean {
  return Boolean(catalog.options[key]?.required_game_features?.[choice]);
}

/**
 * Lists the active challenges that need the game's beta branch.
 *
 * @param answers - Current Challenges answers.
 * @param catalog - Generated schema naming the game features choices require.
 * @returns Display names of the active beta-only challenges.
 */
export function getActiveBetaOnlyChallenges(
  answers: ChallengeAnswers,
  catalog: OptionCatalog = optionCatalog,
): string[] {
  const names: string[] = [];
  const key = CHALLENGE_OPTION_KEYS.gameMode;

  if (answers.enabled && isBetaOnlyChoice(key, answers.gameMode, catalog)) {
    const choice = catalog.options[key]?.choices?.find(
      (candidate) => candidate.name === answers.gameMode,
    );
    names.push(choice?.display_name ?? answers.gameMode);
  }

  return names;
}
