<script setup lang="ts">
import { computed } from "vue";
import type { ChallengeAnswers } from "../../../wizard/WizardAnswers";
import {
  challengeSetupStep,
  getVisibleQuestions,
  resolveQuestionHelp,
} from "../../../wizard/WizardStep";
import ChallengeOptIn from "../bespoke/ChallengeOptIn.vue";
import RunModifierTable from "../bespoke/RunModifierTable.vue";
import WizardControl from "../core/WizardControl.vue";
import WizardQuestion from "../core/WizardQuestion.vue";
import { useWizardAnswers } from "../core/wizardAnswersContext";

/**
 * Challenges step: the bespoke opt-in switch and Run Modifier table, then standard controls.
 *
 * Copy and control descriptors live in `challengeSetupStep`; the step-level warning is
 * rendered by the wizard view from the same definition.
 */
const props = defineProps<{
  modelValue: ChallengeAnswers;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: ChallengeAnswers];
}>();

// Flow predicates and help copy evaluate against the complete answer model.
const answers = useWizardAnswers();

const visibleQuestions = computed(() =>
  getVisibleQuestions(challengeSetupStep, answers),
);

/**
 * Forwards a generically rendered section update to the owning parent view.
 *
 * @param value - Complete immutable section emitted by the control renderer.
 * @returns Nothing; keeps the parent view the sole owner of persistent state.
 */
function forwardUpdate(value: object): void {
  // The renderer edits this step's bound section, so the cast restores its type.
  emit("update:modelValue", value as ChallengeAnswers);
}

/**
 * Turns challenges on or off once the opt-in switch has its confirmations.
 *
 * @param enabled - Whether challenges are now on.
 * @returns Nothing; emits a new section object.
 */
function setEnabled(enabled: boolean): void {
  emit("update:modelValue", { ...props.modelValue, enabled });
}

/**
 * Replaces the selected Run Modifiers.
 *
 * @param runModifiers - Ordered canonical names emitted by the table.
 * @returns Nothing; emits a new section object.
 */
function setRunModifiers(runModifiers: string[]): void {
  emit("update:modelValue", { ...props.modelValue, runModifiers });
}
</script>

<template>
  <div class="space-y-8">
    <WizardQuestion
      v-for="question in visibleQuestions"
      :key="question.id"
      :question="question"
      :help-text="resolveQuestionHelp(question, answers)"
    >
      <ChallengeOptIn
        v-if="question.id === 'challenges-enabled'"
        :model-value="modelValue.enabled"
        @update:model-value="setEnabled"
      />
      <RunModifierTable
        v-else-if="question.id === 'run-modifiers'"
        :model-value="modelValue.runModifiers"
        @update:model-value="setRunModifiers"
      />
      <WizardControl
        v-else
        :question="question"
        :model-value="modelValue"
        @update:model-value="forwardUpdate"
      />
    </WizardQuestion>
  </div>
</template>

<style scoped src="../core/wizard.css"></style>
