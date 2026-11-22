/**
 * @file Finds options that need a game feature only the game's beta branch has.
 *
 * The APWorld cannot know which game branch a player runs, so it marks such options. The
 * mod plays a slot without a feature its game version lacks and tells the player when
 * connecting. The wizard reads the same marker from the generated schema to label those
 * options and to warn on Review.
 */

import { optionCatalog, type OptionCatalog } from "../generated/optionCatalog";
import type { ChallengeAnswers } from "./WizardAnswers";
import { CHALLENGE_OPTION_KEYS } from "./WizardOptionKey";

/**
 * Checks whether an option needs a game feature only the game's beta branch has.
 *
 * @param key - Generated option key.
 * @param catalog - Generated schema naming each option's required game feature.
 * @returns Whether the option is marked beta-only.
 */
export function isBetaOnlyOption(
  key: string,
  catalog: OptionCatalog = optionCatalog,
): boolean {
  return Boolean(catalog.options[key]?.required_game_feature);
}

/**
 * Lists the active challenges that need the game's beta branch.
 *
 * @param answers - Current Challenges answers.
 * @param catalog - Generated schema naming each option's required game feature.
 * @returns Display names of the active beta-only challenges.
 */
export function getActiveBetaOnlyChallenges(
  answers: ChallengeAnswers,
  catalog: OptionCatalog = optionCatalog,
): string[] {
  const names: string[] = [];

  if (
    answers.enabled &&
    answers.dreadMode &&
    isBetaOnlyOption(CHALLENGE_OPTION_KEYS.dreadMode, catalog)
  ) {
    names.push(catalog.options[CHALLENGE_OPTION_KEYS.dreadMode]!.display_name);
  }

  return names;
}
