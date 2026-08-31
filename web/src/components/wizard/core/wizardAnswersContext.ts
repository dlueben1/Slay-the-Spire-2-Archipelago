/**
 * @file Shares the root wizard answer model with descendant wizard components.
 *
 * Step components own writes to their section through `v-model`, but flow logic —
 * visibility predicates, enabledness, and dynamic control ranges declared in
 * `WizardStep.ts` — is defined against the complete `WizardAnswers` model. Rather
 * than threading a duplicate root prop beside every section `modelValue`,
 * `WizardView.vue` provides the reactive model once and components inject it
 * read-only. Writes must still flow through section `update:modelValue` events.
 */

import { inject, provide, type InjectionKey } from "vue";
import type { WizardAnswers } from "../../../wizard/WizardAnswers";

/** Typed injection key private to the provide/use pair below. */
const wizardAnswersKey: InjectionKey<WizardAnswers> = Symbol("wizard-answers");

/**
 * Exposes the root reactive answer model to the wizard component tree.
 *
 * @param answers - The single reactive answer model owned by the wizard view.
 * @returns Nothing; descendants read the same reactive object via injection.
 */
export function provideWizardAnswers(answers: WizardAnswers): void {
  provide(wizardAnswersKey, answers);
}

/**
 * Reads the root reactive answer model for flow evaluation.
 *
 * @returns The reactive answer model provided by the wizard view.
 * @throws When used outside a provided wizard tree, so wiring mistakes fail loudly.
 * @remarks Treat the result as read-only: mutate answers only by emitting section
 * updates so the view remains the sole owner of persistent wizard state.
 */
export function useWizardAnswers(): WizardAnswers {
  const answers = inject(wizardAnswersKey);

  if (!answers) {
    throw new Error(
      "useWizardAnswers requires provideWizardAnswers in an ancestor component.",
    );
  }

  return answers;
}
