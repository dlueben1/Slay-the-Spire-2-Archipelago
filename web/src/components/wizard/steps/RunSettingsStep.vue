<script setup lang="ts">
import { computed } from "vue";
import type { RunAnswers } from "../../../wizard/WizardAnswers";
import {
  getVisibleQuestions,
  resolveQuestionHelp,
  runSetupStep,
} from "../../../wizard/WizardStep";
import WizardControl from "../core/WizardControl.vue";
import WizardQuestion from "../core/WizardQuestion.vue";
import { useWizardAnswers } from "../core/wizardAnswersContext";

/**
 * Gameplay Modifiers step rendered entirely from declarative question definitions.
 *
 * Every question here uses a standard control, so this component only iterates the
 * step's visible questions; copy, control types, and bindings all live in
 * `runSetupStep`. Bespoke markup belongs in steps like Character Setup instead.
 */
defineProps<{
  modelValue: RunAnswers;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: RunAnswers];
}>();

// Flow predicates and help copy evaluate against the complete answer model.
const answers = useWizardAnswers();

const visibleQuestions = computed(() =>
  getVisibleQuestions(runSetupStep, answers),
);

/**
 * Forwards a generically rendered section update to the owning parent view.
 *
 * @param value - Complete immutable section emitted by the control renderer.
 * @returns Nothing; keeps the parent view the sole owner of persistent state.
 */
function forwardUpdate(value: object): void {
  // The renderer edits this step's bound section, so the cast restores its type.
  emit("update:modelValue", value as RunAnswers);
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
      <WizardControl
        :question="question"
        :model-value="modelValue"
        @update:model-value="forwardUpdate"
      />
    </WizardQuestion>
  </div>
</template>

<style scoped src="../core/wizard.css"></style>
