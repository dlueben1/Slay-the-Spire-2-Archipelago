/**
 * @file Pure cross-field rules for the Death Link answer section.
 *
 * The Death Link step presents received fragments, percentage damage, and immediate
 * death as separate concepts with two invariants: an enabled Death Link always keeps
 * at least one received effect selected, and Die is mutually exclusive with both
 * nonlethal effects. These transitions used to live inside the step component's
 * setters; extracting them keeps gameplay rules unit-testable without mounting Vue,
 * while `applyDeathLinkOptions.ts` continues to re-validate at the compile boundary.
 */

import type { WizardSectionTransition } from "./QuestionControl";
import type { DeathLinkAnswers } from "./WizardAnswers";

/**
 * Updates whether deaths are shared with other Death Link players.
 *
 * @param answers - Current Death Link answers.
 * @param enabled - Whether Death Link should be active.
 * @returns A new answer object; enabling never leaves every effect deselected.
 */
export function setDeathLinkEnabled(
  answers: DeathLinkAnswers,
  enabled: boolean,
): DeathLinkAnswers {
  // Enabling Death Link must also leave one incoming effect selected.
  if (
    enabled &&
    !answers.receiveFragment &&
    !answers.receiveDamage &&
    !answers.beKilled
  ) {
    return { ...answers, enabled: true, receiveFragment: true };
  }

  return { ...answers, enabled };
}

/**
 * Updates whether a received death grants a Death Fragment Curse card.
 *
 * @param answers - Current Death Link answers.
 * @param value - Whether the fragment effect should be selected.
 * @returns A new answer object, unchanged when the rules reject the change.
 */
export function setReceiveFragment(
  answers: DeathLinkAnswers,
  value: boolean,
): DeathLinkAnswers {
  // Selecting a nonlethal effect replaces Die just as Die replaces these effects.
  if (answers.beKilled) {
    if (value) {
      return { ...answers, beKilled: false, receiveFragment: true };
    }

    return answers;
  }

  // Never allow the last selected received-link effect to be cleared.
  if (!value && !answers.receiveDamage) {
    return answers;
  }

  return { ...answers, receiveFragment: value };
}

/**
 * Updates whether a received death deals configurable maximum-health damage.
 *
 * @param answers - Current Death Link answers.
 * @param value - Whether the damage effect should be selected.
 * @returns A new answer object, unchanged when the rules reject the change.
 */
export function setReceiveDamage(
  answers: DeathLinkAnswers,
  value: boolean,
): DeathLinkAnswers {
  // Selecting a nonlethal effect replaces Die just as Die replaces these effects.
  if (answers.beKilled) {
    if (value) {
      return { ...answers, beKilled: false, receiveDamage: true };
    }

    return answers;
  }

  // Never allow the last selected received-link effect to be cleared.
  if (!value && !answers.receiveFragment) {
    return answers;
  }

  return { ...answers, receiveDamage: value };
}

/**
 * Updates whether an incoming Death Link should kill the player immediately.
 *
 * @param answers - Current Death Link answers.
 * @param value - Whether lethal mode should be selected.
 * @returns A new answer object with a valid effect selection in either direction.
 * @remarks Python represents this semantic choice as 100% maximum-health damage;
 * that translation stays in `applyDeathLinkOptions.ts`.
 */
export function setBeKilled(
  answers: DeathLinkAnswers,
  value: boolean,
): DeathLinkAnswers {
  // Die replaces both nonlethal choices. Turning it off restores a valid default.
  return value
    ? {
        ...answers,
        beKilled: true,
        receiveFragment: false,
        receiveDamage: false,
      }
    : { ...answers, beKilled: false, receiveFragment: true };
}

/*
 * Descriptor-shaped wrappers bound to Death Link questions in `WizardStep.ts`.
 * Each cast is safe because the descriptor lives on a question inside the step
 * that owns the `deathLink` section; descriptor integrity tests exercise them.
 */

/** Transition wrapper for the Enable Death Link checkbox. */
export const deathLinkEnabledTransition: WizardSectionTransition = (
  section,
  value,
) => setDeathLinkEnabled(section as DeathLinkAnswers, value as boolean);

/** Transition wrapper for the Receive a Death Fragment effect card. */
export const receiveFragmentTransition: WizardSectionTransition = (
  section,
  value,
) => setReceiveFragment(section as DeathLinkAnswers, value as boolean);

/** Transition wrapper for the Take Max HP damage effect card. */
export const receiveDamageTransition: WizardSectionTransition = (
  section,
  value,
) => setReceiveDamage(section as DeathLinkAnswers, value as boolean);

/** Transition wrapper for the Die effect card. */
export const beKilledTransition: WizardSectionTransition = (section, value) =>
  setBeKilled(section as DeathLinkAnswers, value as boolean);
