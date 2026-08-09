/**
 * @file Declares wizard navigation and conditional question visibility.
 *
 * Add flow-level questions here, their persisted values in `WizardAnswers.ts`, and
 * their controls in the matching `components/wizard` step. This file must not know
 * Archipelago option keys; `wizard/compiler` owns that translation.
 */

import type { WizardAnswers } from "./WizardAnswers";

export interface WizardQuestion {
  id: string;
  title: string;
  isVisible?: (answers: WizardAnswers) => boolean;
}

export interface WizardStep {
  id: string;
  title: string;
  description?: string;
  questions: WizardQuestion[];
}

/**
 * Checks whether random character selection needs its count prompt.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether the random-count question should be shown.
 */
function usesRandomCharacterSelection(answers: WizardAnswers): boolean {
  // A count is irrelevant when every selected character is used.
  return answers.characters.selectionMode === "random";
}

/**
 * Checks whether character unlocking needs a fixed-start prompt.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether the starting-character question should be shown.
 */
function usesFixedStartingCharacter(answers: WizardAnswers): boolean {
  // A named character is irrelevant for all-at-start and random-start modes.
  return answers.characters.availability === "fixed";
}

/**
 * Checks whether entered modded characters need their name-and-help table.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether at least one modded character row exists.
 */
function hasModdedCharacters(answers: WizardAnswers): boolean {
  // The table is mounted only after the portrait card's plus button creates a row.
  return answers.characters.moddedCharacters.length > 0;
}

/**
 * Checks whether one Ascension configuration applies to the complete roster.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether the shared Ascension checklist should be shown.
 */
function usesSharedAscensions(answers: WizardAnswers): boolean {
  // Shared mode compiles through the standard `ascension` option arrays.
  return answers.characters.ascensionMode === "shared";
}

/**
 * Checks whether each configured character needs independent Ascension settings.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether the per-character advanced editor should be shown.
 */
function usesIndividualAscensions(answers: WizardAnswers): boolean {
  // Individual mode compiles through the `advanced_characters` dictionary.
  return answers.characters.ascensionMode === "individual";
}

/**
 * Checks whether Shop Sanity's dependent configuration is meaningful.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether shop slot and cost questions should be shown.
 */
function usesShopSanity(answers: WizardAnswers): boolean {
  // Slot counts and pricing have no effect while Shop Sanity is disabled.
  return answers.checksAndRewards.shop.enabled;
}

/**
 * Checks whether Death Link's dependent behavior is meaningful.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether damage and fragment questions should be shown.
 */
function usesDeathLink(answers: WizardAnswers): boolean {
  // Received-link behavior has no effect while Death Link is disabled.
  return answers.deathLink.enabled;
}

/**
 * Checks whether received Death Link damage needs its percentage slider.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Whether nonlethal maximum-health damage is enabled and editable.
 */
function usesDeathLinkDamage(answers: WizardAnswers): boolean {
  // Lethal mode owns the generated 100% value and disables nonlethal controls.
  return (
    answers.deathLink.enabled &&
    answers.deathLink.receiveDamage &&
    !answers.deathLink.beKilled
  );
}

/** Character Setup definition consumed by its component and wizard navigation. */
export const characterSetupStep: WizardStep = {
  id: "characters",
  title: "Character Setup",
  description:
    "Choose built-in or modded characters, their Ascensions, and roster progression.",
  questions: [
    { id: "characters", title: "Which characters do you want to play?" },
    {
      id: "modded-characters",
      title: "Which modded character IDs should be used?",
      isVisible: hasModdedCharacters,
    },
    {
      id: "ascension-mode",
      title: "Should characters have different Ascension settings?",
    },
    {
      id: "shared-ascensions",
      title: "Which Ascensions should every character use?",
      isVisible: usesSharedAscensions,
    },
    {
      id: "individual-ascensions",
      title: "Configure each character's Ascensions",
      isVisible: usesIndividualAscensions,
    },
    { id: "selection", title: "Should every selected character be used?" },
    {
      id: "random-count",
      title: "How many characters should be randomly selected?",
      isVisible: usesRandomCharacterSelection,
    },
    { id: "availability", title: "How should characters become available?" },
    {
      id: "starting-character",
      title: "Which character should start unlocked?",
      isVisible: usesFixedStartingCharacter,
    },
    {
      id: "goal",
      title: "How many characters must complete a run to finish your goal?",
    },
  ],
};

/** Run Rules definition consumed by its component and wizard navigation. */
export const runSetupStep: WizardStep = {
  id: "run",
  title: "Run Rules",
  description:
    "Configure climb rules plus Archipelago's progression and accessibility defaults.",
  questions: [
    {
      id: "ancient-location",
      title: "When should Progressive Ancient rewards be available?",
    },
    {
      id: "ancient-pool",
      title: "Which Ancient relics may appear in each reward?",
    },
    {
      id: "relic-choice-count",
      title: "How many relic choices should Archipelago rewards offer?",
    },
    { id: "neow", title: "Should Neow's starting blessing be shuffled?" },
    { id: "seeded", title: "Should each character use a fixed run seed?" },
    {
      id: "progression-balancing",
      title: "How strongly should Archipelago balance progression items?",
    },
    {
      id: "accessibility",
      title: "Which locations must be reachable?",
    },
  ],
};

/** Checks & Rewards definition consumed by its component and navigation. */
export const checkSetupStep: WizardStep = {
  id: "checks",
  title: "Checks & Rewards",
  description:
    "Choose shuffled checks, optional Shop slots, and the filler reward pool.",
  questions: [
    {
      id: "check-types",
      title: "Which additional checks and rewards should be shuffled?",
    },
    {
      id: "shop-slots",
      title: "How many slots of each type should be shuffled?",
      isVisible: usesShopSanity,
    },
    {
      id: "shop-removal",
      title: "Should card removal become a progressive unlock?",
      isVisible: usesShopSanity,
    },
    {
      id: "shop-costs",
      title: "How expensive should shuffled shop slots be?",
      isVisible: usesShopSanity,
    },
    {
      id: "filler-weights",
      title: "How often should each filler item appear?",
    },
  ],
};

/** Death Link definition consumed by its component and wizard navigation. */
export const deathLinkSetupStep: WizardStep = {
  id: "death-link",
  title: "Death Link",
  description:
    "Choose whether deaths are shared and what happens when another player dies.",
  questions: [
    { id: "death-link-enabled", title: "Should Death Link be enabled?" },
    {
      id: "death-link-effects",
      title: "What should happen when a Death Link is received?",
      isVisible: usesDeathLink,
    },
    {
      id: "death-link-damage",
      title:
        "How much maximum-health damage should a received Death Link deal?",
      isVisible: usesDeathLinkDamage,
    },
  ],
};

/** Declarative step and question ordering for the guided wizard. */
export const wizardSteps: WizardStep[] = [
  characterSetupStep,
  runSetupStep,
  checkSetupStep,
  deathLinkSetupStep,
  {
    id: "review",
    title: "Review",
    questions: [],
  },
];

/**
 * Lists Character Setup questions applicable to the current answers.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Visible question identifiers in display order.
 * @remarks This evaluates presentation flow only; it does not validate or compile.
 */
export function visibleCharacterQuestionIds(answers: WizardAnswers): string[] {
  // Character Setup is the first guided step in the current vertical slice.
  const characterQuestions = characterSetupStep.questions;

  // Questions without an explicit predicate are always visible.
  const visibleQuestionIds: string[] = [];

  for (const question of characterQuestions) {
    const isVisible = question.isVisible?.(answers) ?? true;

    if (isVisible) {
      visibleQuestionIds.push(question.id);
    }
  }

  // Expose stable identifiers instead of mutable definition objects.
  return visibleQuestionIds;
}

/**
 * Lists Checks & Rewards questions applicable to the current answers.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Visible Checks & Rewards question identifiers in display order.
 */
export function visibleCheckQuestionIds(answers: WizardAnswers): string[] {
  // Evaluate the shared step predicates through one reusable local collection pass.
  const visibleQuestionIds: string[] = [];

  for (const question of checkSetupStep.questions) {
    const isVisible = question.isVisible?.(answers) ?? true;

    if (isVisible) {
      visibleQuestionIds.push(question.id);
    }
  }

  // Expose stable identifiers instead of mutable definition objects.
  return visibleQuestionIds;
}

/**
 * Lists Death Link questions applicable to the current answers.
 *
 * @param answers - Current player-facing wizard answers.
 * @returns Visible Death Link question identifiers in display order.
 */
export function visibleDeathLinkQuestionIds(answers: WizardAnswers): string[] {
  // Evaluate the shared step predicates through one reusable local collection pass.
  const visibleQuestionIds: string[] = [];

  for (const question of deathLinkSetupStep.questions) {
    const isVisible = question.isVisible?.(answers) ?? true;

    if (isVisible) {
      visibleQuestionIds.push(question.id);
    }
  }

  // Expose stable identifiers instead of mutable definition objects.
  return visibleQuestionIds;
}
