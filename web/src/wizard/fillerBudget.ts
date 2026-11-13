/**
 * @file Counts the filler slots Bonus Items and Sight items can use.
 *
 * Generation fails when Bonus Items plus challenge Sight items outnumber the filler
 * slots left over from Floor Checks. This module repeats the Python world's count from
 * answers, so the builder can show what is left and refuse a YAML that cannot
 * generate. Keep it in step with `create_items` in `world/spire2/world.py`.
 */

import {
  SIGHT_COPY_COUNTS,
  type AscensionConfigurationAnswers,
  type CharacterStarterOverrides,
  type StarterOverride,
  type WizardAnswers,
} from "./WizardAnswers";

/** Floor-check filler slots for each character before anything takes them. */
export const FLOOR_CHECK_FILLER_SLOTS = 48;

/** Floor-check filler slots each progressive starter option replaces. */
const PROGRESSIVE_STARTER_ITEMS = 2;

/** Double Boss (A10): its second boss adds one more floor check while it stays active. */
const DOUBLE_BOSS_LEVEL = 10;

export interface FillerBudget {
  /** Filler slots across the characters that generation will use. */
  available: number;
  /** Slots that Bonus Items and Sight items take. */
  used: number;
  /** `available - used`; negative means generation would fail. */
  remaining: number;
  /** Whether Floor Checks, the only source of these slots, are on. */
  floorChecks: boolean;
  /**
   * True when random character selection could pick characters with different budgets;
   * `available` is then the smallest total any pick can have.
   */
  isWorstCase: boolean;
}

/** Resolves one per-character starter override against the global setting. */
function resolveStarter(
  override: StarterOverride | undefined,
  globalValue: boolean,
): boolean {
  if (override === "enabled") return true;
  if (override === "disabled") return false;
  return globalValue;
}

/** Filler slots one character contributes, following `create_items`. */
function getCharacterSlots(
  ascensions: AscensionConfigurationAnswers,
  starterOverrides: CharacterStarterOverrides | undefined,
  answers: WizardAnswers,
): number {
  const enabled = new Set(ascensions.enabled);
  // A Down only exists for an active Ascension, and only while Downs are on.
  const downs = ascensions.ascensionDownsEnabled
    ? ascensions.downs.filter((level) => enabled.has(level))
    : [];

  let slots = FLOOR_CHECK_FILLER_SLOTS - downs.length;
  if (enabled.has(DOUBLE_BOSS_LEVEL) && !downs.includes(DOUBLE_BOSS_LEVEL)) {
    slots += 1;
  }

  const equipment = answers.checksAndRewards.startingEquipment;
  if (
    resolveStarter(
      starterOverrides?.progressiveStarterCard,
      equipment.progressiveStarterCard,
    )
  ) {
    slots -= PROGRESSIVE_STARTER_ITEMS;
  }
  if (
    resolveStarter(
      starterOverrides?.progressiveStarterRelic,
      equipment.progressiveStarterRelic,
    )
  ) {
    slots -= PROGRESSIVE_STARTER_ITEMS;
  }
  return slots;
}

/** Filler slots per configured character, keyed by the name used in the roster. */
function getSlotsByCharacter(answers: WizardAnswers): Map<string, number> {
  const characters = answers.characters;
  const shared = characters.ascensionMode === "shared";
  const slots = new Map<string, number>();

  for (const name of characters.selectedCharacters) {
    const ascensions = shared
      ? characters.sharedAscensions
      : (characters.individualAscensions[name] ?? characters.sharedAscensions);
    slots.set(
      name,
      getCharacterSlots(
        ascensions,
        characters.individualStarterOverrides?.[name],
        answers,
      ),
    );
  }

  for (const modded of characters.moddedCharacters) {
    const name = modded.name.trim();
    if (!name) continue;
    slots.set(
      name,
      getCharacterSlots(
        shared ? characters.sharedAscensions : modded.ascensions,
        modded.starterOverrides,
        answers,
      ),
    );
  }

  return slots;
}

/** Slots Bonus Items and Sight items take. Sight items only count while challenges are on. */
function getUsedSlots(answers: WizardAnswers): number {
  let used = answers.checksAndRewards.bonusItems.length;
  const challenges = answers.challenges;
  if (challenges.enabled) {
    used +=
      SIGHT_COPY_COUNTS[challenges.hiddenIntents] +
      SIGHT_COPY_COUNTS[challenges.hiddenHealth] +
      SIGHT_COPY_COUNTS[challenges.hiddenPiles] +
      SIGHT_COPY_COUNTS[challenges.hiddenMap] +
      SIGHT_COPY_COUNTS[challenges.hiddenCards];
  }
  return used;
}

/**
 * Counts free filler slots for the current answers.
 *
 * @param answers - Complete wizard answers.
 * @returns Available, used and remaining slots. With random character selection, the
 * available count is the smallest any pick can have, so a YAML that passes here
 * generates whichever characters are drawn.
 */
export function getFillerBudget(answers: WizardAnswers): FillerBudget {
  const used = getUsedSlots(answers);
  const floorChecks = answers.checksAndRewards.checks.includeFloorChecks;

  if (!floorChecks) {
    return {
      available: 0,
      used,
      remaining: -used,
      floorChecks,
      isWorstCase: false,
    };
  }

  const slotsByCharacter = getSlotsByCharacter(answers);
  const characters = answers.characters;
  const pickCount = characters.randomCharacterCount;
  const picksSubset =
    characters.selectionMode === "random" &&
    pickCount > 0 &&
    pickCount < slotsByCharacter.size;

  let available = 0;
  let isWorstCase = false;

  if (!picksSubset) {
    for (const slots of slotsByCharacter.values()) available += slots;
  } else {
    // A fixed starting character is always drawn; the rest of the pick is random.
    const fixed =
      characters.availability === "fixed" && characters.startingCharacter
        ? characters.startingCharacter
        : null;
    const fixedSlots = fixed !== null ? slotsByCharacter.get(fixed) : undefined;
    const others = [...slotsByCharacter]
      .filter(([name]) => fixedSlots === undefined || name !== fixed)
      .map(([, slots]) => slots)
      .sort((a, b) => a - b);
    const drawn = fixedSlots === undefined ? pickCount : pickCount - 1;

    available =
      (fixedSlots ?? 0) +
      others.slice(0, drawn).reduce((sum, slots) => sum + slots, 0);
    isWorstCase = new Set(others).size > 1;
  }

  return {
    available,
    used,
    remaining: available - used,
    floorChecks,
    isWorstCase,
  };
}

/**
 * Throws a player-readable error when Bonus Items and Sight items do not fit.
 *
 * @param answers - Complete wizard answers.
 * @throws When generation would fail for lack of filler slots.
 */
export function validateFillerBudget(answers: WizardAnswers): void {
  const budget = getFillerBudget(answers);
  if (budget.remaining >= 0) return;

  if (!budget.floorChecks) {
    throw new Error(
      `Bonus Items and Sight items need Floor Checks, which provide their filler slots. ` +
        `Turn Floor Checks on, or remove ${budget.used} of them.`,
    );
  }

  throw new Error(
    `Bonus Items and Sight items need ${budget.used} filler slots, but only ` +
      `${budget.available} are free${budget.isWorstCase ? " for every possible character pick" : ""}. ` +
      `Remove ${-budget.remaining}, or free slots by turning off Ascension Downs or progressive starting equipment.`,
  );
}
