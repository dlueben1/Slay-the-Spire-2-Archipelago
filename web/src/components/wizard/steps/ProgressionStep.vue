<script setup lang="ts">
import { computed } from "vue";
import type { ProgressionAnswers } from "../../../wizard/WizardAnswers";
import {
  getVisibleQuestions,
  progressionSetupStep,
  resolveQuestionHelp,
} from "../../../wizard/WizardStep";
import WizardControl from "../core/WizardControl.vue";
import WizardQuestion from "../core/WizardQuestion.vue";
import { useWizardAnswers } from "../core/wizardAnswersContext";

/**
 * Progression step rendered entirely from declarative question definitions.
 *
 * The balancing slider (including its named presets) and the accessibility radio
 * group are described by `progressionSetupStep`; this component only supplies the
 * shared Archipelago Settings subsection shell around the generic renderer.
 */
defineProps<{
  modelValue: ProgressionAnswers;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: ProgressionAnswers];
}>();

// Flow predicates and help copy evaluate against the complete answer model.
const answers = useWizardAnswers();

const visibleQuestions = computed(() =>
  getVisibleQuestions(progressionSetupStep, answers),
);

/**
 * Forwards a generically rendered section update to the owning parent view.
 *
 * @param value - Complete immutable section emitted by the control renderer.
 * @returns Nothing; keeps the parent view the sole owner of persistent state.
 */
function forwardUpdate(value: object): void {
  // The renderer edits this step's bound section, so the cast restores its type.
  emit("update:modelValue", value as ProgressionAnswers);
}
</script>

<template>
  <section class="wizard-subsection pt-0! border-t-0!">
    <div class="wizard-subsection__header">
      <h3>Archipelago Settings</h3>
      <p>General generation settings shared with other Archipelago games.</p>
    </div>

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
  </section>
</template>

<style scoped src="../core/wizard.css"></style>
