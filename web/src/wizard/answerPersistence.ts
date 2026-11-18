/**
 * @file Keeps the builder's answers in the browser between visits.
 *
 * A refresh or an accidental Back used to throw every answer away. The wizard now
 * saves its answer model to `localStorage` on each change and restores it on load.
 *
 * Saved answers may come from an older deployment whose answer model differed, so
 * restoring never trusts their shape: it starts from fresh defaults and copies a saved
 * value only where its type matches the default's. Anything that does not fit falls
 * back to the default. Values that fit the shape but are no longer valid (a removed
 * choice, say) surface through the normal compiler validation, like any other answer.
 * Answers that only moved to another section are carried over by `moveRelocatedAnswers`.
 */

import type { WizardAnswers } from "./WizardAnswers";

/**
 * Bump the version when the answer model changes in a way the shape check below would
 * accept but misread; older saves are then ignored instead of restored.
 */
const STORAGE_KEY = "sts2ap.builder.answers.v1";

/** What the builder stores: the answers plus the step the player was on. */
export interface SavedWizardState {
  answers: WizardAnswers;
  stepId: string;
}

/** The subset of the Web Storage API this module needs, so tests can pass a fake. */
export type AnswerStorage = Pick<Storage, "getItem" | "setItem" | "removeItem">;

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** Strings, numbers and null, which some answers mix (see `mergeSaved`). */
function isScalarOrNull(value: unknown): boolean {
  return (
    value === null || typeof value === "string" || typeof value === "number"
  );
}

/**
 * Copies saved values over a default value wherever their shapes agree.
 *
 * @param fallback - Fresh default value that defines the expected shape.
 * @param saved - Parsed value from storage, of unknown shape.
 * @returns A value with the default's shape, filled from `saved` where possible.
 * @remarks Objects merge key by key. Keys that exist only in the save are kept,
 * because several answers are records keyed by character name, and optional answers
 * (such as starter overrides) are absent from the defaults. Arrays are taken whole.
 * Booleans must stay booleans. Strings, numbers and null may replace each other,
 * since some answers mix them (a goal is `"all"` or a number; the starting character
 * is a name or null).
 */
function mergeSaved(fallback: unknown, saved: unknown): unknown {
  if (isPlainObject(fallback)) {
    if (!isPlainObject(saved)) {
      return fallback;
    }

    const merged: Record<string, unknown> = { ...saved };
    for (const [key, defaultValue] of Object.entries(fallback)) {
      merged[key] =
        key in saved ? mergeSaved(defaultValue, saved[key]) : defaultValue;
    }
    return merged;
  }

  if (Array.isArray(fallback)) {
    return Array.isArray(saved) ? saved : fallback;
  }

  if (typeof fallback === "boolean") {
    return typeof saved === "boolean" ? saved : fallback;
  }

  if (isScalarOrNull(fallback) && isScalarOrNull(saved)) {
    return saved;
  }

  return fallback;
}

/**
 * Moves answers saved under an older section layout to where they live now.
 *
 * @param saved - Parsed saved answers, of unknown shape.
 * @returns The same answers with relocated values moved; anything else is untouched.
 * @remarks Checks & Rewards was one section until it was split into Checks, Shops and
 * Item Pool, with its Ancient choices moving to Run Settings; the Blitz merchant
 * chance moved from Run Settings to Boons. A value already in its new place wins.
 */
function moveRelocatedAnswers(
  saved: Record<string, unknown>,
): Record<string, unknown> {
  const old = saved.checksAndRewards;
  const moved: Record<string, unknown> = { ...saved };
  const run = isPlainObject(saved.run) ? { ...saved.run } : undefined;

  if (isPlainObject(old)) {
    const sections: Record<string, Record<string, unknown>> = {
      checks: { startingEquipment: old.startingEquipment, types: old.checks },
      shops: {
        shop: old.shop,
        shopCards: old.shopCards,
        shopRelics: old.shopRelics,
        shopPotions: old.shopPotions,
      },
      itemPool: {
        bonusItems: old.bonusItems,
        filler: old.filler,
        traps: old.traps,
      },
    };
    for (const [key, section] of Object.entries(sections)) {
      if (!(key in saved)) {
        moved[key] = section;
      }
    }
    if (run && !("ancients" in run)) {
      run.ancients = old.ancients;
    }
    delete moved.checksAndRewards;
  }

  if (run && "blitzMerchantChance" in run) {
    if (!("boons" in saved)) {
      moved.boons = { blitzMerchantChance: run.blitzMerchantChance };
    }
    delete run.blitzMerchantChance;
  }
  if (run) {
    moved.run = run;
  }

  return moved;
}

/**
 * Reads the saved builder state, if there is a usable one.
 *
 * @param storage - Where the state was saved, normally `window.localStorage`.
 * @param defaults - Fresh default answers that define the current answer shape.
 * @returns The restored state, or `null` when nothing usable was saved.
 */
export function loadSavedWizardState(
  storage: AnswerStorage | undefined,
  defaults: WizardAnswers,
): SavedWizardState | null {
  let raw: string | null;
  try {
    // Storage can throw in private windows or when site data is blocked.
    raw = storage?.getItem(STORAGE_KEY) ?? null;
  } catch {
    return null;
  }

  if (raw === null) {
    return null;
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return null;
  }

  if (!isPlainObject(parsed) || !isPlainObject(parsed.answers)) {
    return null;
  }

  return {
    answers: mergeSaved(
      defaults,
      moveRelocatedAnswers(parsed.answers),
    ) as WizardAnswers,
    stepId: typeof parsed.stepId === "string" ? parsed.stepId : "",
  };
}

/**
 * Saves the builder state. Failures are ignored: saving is a convenience, and the
 * builder must keep working when storage is full or unavailable.
 */
export function saveWizardState(
  storage: AnswerStorage | undefined,
  state: SavedWizardState,
): void {
  try {
    storage?.setItem(STORAGE_KEY, JSON.stringify(state));
  } catch {
    // Nothing useful to do; the answers stay in memory for this visit.
  }
}

/** Forgets the saved builder state, for Start over. */
export function clearSavedWizardState(
  storage: AnswerStorage | undefined,
): void {
  try {
    storage?.removeItem(STORAGE_KEY);
  } catch {
    // As above: storage problems never block the builder.
  }
}

/**
 * Returns `window.localStorage`, or `undefined` where reading it throws (some
 * browsers do that when site data is blocked).
 */
export function getBrowserAnswerStorage(): AnswerStorage | undefined {
  try {
    return window.localStorage;
  } catch {
    return undefined;
  }
}
