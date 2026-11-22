/**
 * @file Defines the hidden-information challenges and the Sight items that undo them.
 *
 * Each row ties one challenge's answer fields to its display copy and its canonical
 * `sight_bonuses` name, so the Challenges questions, review, compiler, importer and
 * filler count all walk the same list. A row's `copiesField` is also its key in
 * `CHALLENGE_OPTION_KEYS`, which maps it to the generated `hidden_*` option.
 */

import type { ChallengeAnswers } from "./WizardAnswers";

/** How many Sight items a hidden-information challenge adds; `off` leaves it off. */
export type SightCopies = "off" | "one" | "two" | "three" | "four";

/** Sight item counts written to the `hidden_*` options. */
export const SIGHT_COPY_COUNTS: Readonly<Record<SightCopies, number>> = {
  off: 0,
  one: 1,
  two: 2,
  three: 3,
  four: 4,
};

/** Which Sight items' second copy adds a bonus, one switch per Sight. */
export interface SightBonusAnswers {
  intent: boolean;
  health: boolean;
  pile: boolean;
  map: boolean;
  card: boolean;
  relic: boolean;
}

export interface Sight {
  /** Challenge answer holding this Sight's item count. */
  copiesField:
    | "hiddenIntents"
    | "hiddenHealth"
    | "hiddenPiles"
    | "hiddenMap"
    | "hiddenCards"
    | "hiddenRelics";
  /** Switch in `ChallengeAnswers.sightBonuses` for this Sight's bonus. */
  bonusField: keyof SightBonusAnswers;
  /** Canonical `sight_bonuses` name. */
  bonusName: string;
  itemName: string;
  /** Review sentence opening, finished by "until <Sight> is found." */
  hiddenSummary: string;
  bonusLabel: string;
  bonusEffect: string;
}

/** Every Sight, in display order. */
export const SIGHTS: readonly Sight[] = [
  {
    copiesField: "hiddenIntents",
    bonusField: "intent",
    bonusName: "IntentSight",
    itemName: "Intent Sight",
    hiddenSummary: "Enemy intents are hidden",
    bonusLabel: "Intent Sight: Forewarned",
    bonusEffect:
      "Start each combat with Forewarned: for each enemy whose attacks your Block stops completely on its turn, draw 1 more card next turn.",
  },
  {
    copiesField: "hiddenHealth",
    bonusField: "health",
    bonusName: "HealthSight",
    itemName: "Health Sight",
    hiddenSummary: "Health is hidden",
    bonusLabel: "Health Sight: Aged Cheese",
    bonusEffect:
      "Gain Aged Cheese: after defeating an Elite or a boss, raise your Max HP by 3.",
  },
  {
    copiesField: "hiddenPiles",
    bonusField: "pile",
    bonusName: "PileSight",
    itemName: "Pile Sight",
    hiddenSummary: "Combat piles are hidden",
    bonusLabel: "Pile Sight: Frozen Eye",
    bonusEffect:
      "Gain Frozen Eye: the draw pile shows cards in the order you will draw them.",
  },
  {
    copiesField: "hiddenMap",
    bonusField: "map",
    bonusName: "MapSight",
    itemName: "Map Sight",
    hiddenSummary: "Rooms beyond your next move are hidden",
    bonusLabel: "Map Sight: Scouting",
    bonusEffect:
      "The map shows your next 2 fights, your next Elite and what your next 2 ? rooms will be.",
  },
  {
    copiesField: "hiddenCards",
    bonusField: "card",
    bonusName: "CardSight",
    itemName: "Card Sight",
    hiddenSummary: "Cards are unknown",
    bonusLabel: "Card Sight: More Choice",
    bonusEffect:
      "Card rewards offer 1 more card, and Act 1 card rewards can be upgraded.",
  },
  {
    copiesField: "hiddenRelics",
    bonusField: "relic",
    bonusName: "RelicSight",
    itemName: "Relic Sight",
    hiddenSummary: "Relics are unknown",
    bonusLabel: "Relic Sight: Choose Your Relic",
    bonusEffect:
      "Relic rewards offer 2 relics to choose from. The one you leave goes back into the relic pool.",
  },
];

/**
 * Finds the choice for a Sight item count.
 *
 * @param count - A `hidden_*` value.
 * @returns The matching choice, or `undefined` for a count the builder does not offer.
 */
export function findSightCopies(count: unknown): SightCopies | undefined {
  return (Object.keys(SIGHT_COPY_COUNTS) as SightCopies[]).find(
    (choice) => SIGHT_COPY_COUNTS[choice] === count,
  );
}

/** The item-count answers of every Sight. */
export type SightCopiesAnswers = Pick<ChallengeAnswers, Sight["copiesField"]>;

/**
 * Builds the item-count answer of every Sight.
 *
 * @param readCopies - Gives one Sight's item count.
 * @returns One answer per Sight, in display order.
 */
export function buildSightCopiesAnswers(
  readCopies: (sight: Sight) => SightCopies,
): SightCopiesAnswers {
  return Object.fromEntries(
    SIGHTS.map((sight) => [sight.copiesField, readCopies(sight)]),
  ) as unknown as SightCopiesAnswers;
}

/** Checks whether a Sight adds a second copy, which is what gives its bonus. */
export function hasSecondSightCopy(
  answers: ChallengeAnswers,
  sight: Sight,
): boolean {
  return SIGHT_COPY_COUNTS[answers[sight.copiesField]] >= 2;
}

/** Counts the Sight items every hidden-information challenge adds together. */
export function countSightItems(answers: ChallengeAnswers): number {
  return SIGHTS.reduce(
    (total, sight) => total + SIGHT_COPY_COUNTS[answers[sight.copiesField]],
    0,
  );
}

/**
 * Turns `sight_bonuses` names into the bonus switches.
 *
 * @param names - Canonical Sight Bonus names.
 * @returns One switch per Sight, on when its name is listed.
 */
export function readSightBonusNames(
  names: Iterable<string>,
): SightBonusAnswers {
  const selected = new Set(names);
  return Object.fromEntries(
    SIGHTS.map((sight) => [sight.bonusField, selected.has(sight.bonusName)]),
  ) as unknown as SightBonusAnswers;
}

/**
 * Lists the switched-on Sight Bonuses in display order.
 *
 * @param answers - Sight Bonus switches.
 * @returns Canonical `sight_bonuses` names.
 */
export function getSightBonusNames(answers: SightBonusAnswers): string[] {
  return SIGHTS.filter((sight) => answers[sight.bonusField]).map(
    (sight) => sight.bonusName,
  );
}
