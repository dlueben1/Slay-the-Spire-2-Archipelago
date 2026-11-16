<script setup lang="ts">
import type { ChecksAnswers } from "../../../wizard/WizardAnswers";
import {
  checkSetupStep,
  getQuestionById,
  resolveQuestionHelp,
  type WizardQuestion as WizardQuestionDefinition,
} from "../../../wizard/WizardStep";
import WizardControl from "../core/WizardControl.vue";
import WizardQuestion from "../core/WizardQuestion.vue";
import { useWizardAnswers } from "../core/wizardAnswersContext";

/**
 * Checks step: the check-types grid, then Starting Equipment.
 *
 * Both questions render through `WizardControl` from `checkSetupStep`; the Floor Checks
 * dependency rule lives in `wizard/checksTransitions.ts`, and this shell only adds the
 * Starting Equipment subsection header.
 */
defineProps<{
  modelValue: ChecksAnswers;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: ChecksAnswers];
}>();

// Flow predicates and help copy evaluate against the complete answer model.
const answers = useWizardAnswers();

/** Resolves one declared question for the template without non-null assertions. */
function question(questionId: string): WizardQuestionDefinition {
  return getQuestionById(checkSetupStep, questionId);
}

/**
 * Forwards a generically rendered section update to the owning parent view.
 *
 * @param value - Complete immutable section emitted by the control renderer.
 * @returns Nothing; keeps the parent view the sole owner of persistent state.
 */
function forwardUpdate(value: object): void {
  // The renderer edits this step's bound section, so the cast restores its type.
  emit("update:modelValue", value as ChecksAnswers);
}
</script>

<template>
  <div class="space-y-10">
    <WizardMarkdownDocument
      source="docs/faq-progressive.md"
      fallback-title="Progressive Items"
    />

    <WizardQuestion
      :question="question('check-types')"
      :help-text="resolveQuestionHelp(question('check-types'), answers)"
    >
      <WizardControl
        :question="question('check-types')"
        :model-value="modelValue"
        @update:model-value="forwardUpdate"
      />
    </WizardQuestion>

    <section class="wizard-subsection">
      <div class="wizard-subsection__header">
        <h3>Starting Equipment</h3>
        <p>
          Choose whether compatible starter cards and relics are restored and
          upgraded through progressive Archipelago items.
        </p>
      </div>

      <WizardQuestion
        :question="question('starting-equipment')"
        :help-text="
          resolveQuestionHelp(question('starting-equipment'), answers)
        "
      >
        <WizardControl
          :question="question('starting-equipment')"
          :model-value="modelValue"
          @update:model-value="forwardUpdate"
        />
      </WizardQuestion>
    </section>
  </div>
</template>

<style scoped src="../core/wizard.css" />
