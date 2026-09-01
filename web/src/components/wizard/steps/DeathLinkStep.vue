<script setup lang="ts">
import { computed } from "vue";
import type { DeathLinkAnswers } from "../../../wizard/WizardAnswers";
import {
  deathLinkSetupStep,
  getVisibleQuestions,
  resolveQuestionHelp,
} from "../../../wizard/WizardStep";
import WizardControl from "../core/WizardControl.vue";
import WizardQuestion from "../core/WizardQuestion.vue";
import { useWizardAnswers } from "../core/wizardAnswersContext";

/**
 * Death Link step rendered entirely from declarative question definitions.
 *
 * The mutual-exclusion and at-least-one-effect rules live in
 * `wizard/deathLinkTransitions.ts` and are bound to the effect cards through their
 * descriptors, so this component contains no gameplay logic. Question visibility
 * (effects only while enabled, the damage slider only in nonlethal damage mode)
 * flows from the step's shared predicates instead of hand-written `v-if` chains.
 */
defineProps<{
  modelValue: DeathLinkAnswers;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: DeathLinkAnswers];
}>();

// Flow predicates and help copy evaluate against the complete answer model.
const answers = useWizardAnswers();

const visibleQuestions = computed(() =>
  getVisibleQuestions(deathLinkSetupStep, answers),
);

/**
 * Forwards a generically rendered section update to the owning parent view.
 *
 * @param value - Complete immutable section emitted by the control renderer.
 * @returns Nothing; keeps the parent view the sole owner of persistent state.
 */
function forwardUpdate(value: object): void {
  // The renderer edits this step's bound section, so the cast restores its type.
  emit("update:modelValue", value as DeathLinkAnswers);
}
</script>

<template>
  <div class="space-y-8">
    <WizardMarkdownDocument
      source="docs/faq-deathlink.md"
      fallback-title="Death Link Help"
    />

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
