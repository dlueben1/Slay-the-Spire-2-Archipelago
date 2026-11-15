<script setup lang="ts">
import { computed } from "vue";
import { BOON_GROUPS, BOON_TABLE_ROWS } from "../../../wizard/Boon";
import type { BoonAnswers } from "../../../wizard/WizardAnswers";
import {
  boonSetupStep,
  getVisibleQuestions,
  resolveQuestionHelp,
} from "../../../wizard/WizardStep";
import ModifierTable from "../bespoke/ModifierTable.vue";
import WizardControl from "../core/WizardControl.vue";
import WizardQuestion from "../core/WizardQuestion.vue";
import { useWizardAnswers } from "../core/wizardAnswersContext";

/**
 * Boons step: the bespoke Boon table, then the Blitz merchant slider.
 *
 * Copy and control descriptors live in `boonSetupStep`; the Boons compiler owns the
 * YAML mapping.
 */
const props = defineProps<{
  modelValue: BoonAnswers;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: BoonAnswers];
}>();

// Flow predicates and help copy evaluate against the complete answer model.
const answers = useWizardAnswers();

const visibleQuestions = computed(() =>
  getVisibleQuestions(boonSetupStep, answers),
);

/**
 * Forwards a generically rendered section update to the owning parent view.
 *
 * @param value - Complete immutable section emitted by the control renderer.
 * @returns Nothing; keeps the parent view the sole owner of persistent state.
 */
function forwardUpdate(value: object): void {
  // The renderer edits this step's bound section, so the cast restores its type.
  emit("update:modelValue", value as BoonAnswers);
}

/**
 * Replaces the selected Boons.
 *
 * @param selected - Ordered canonical names emitted by the table.
 * @returns Nothing; emits a new section object.
 */
function setSelected(selected: string[]): void {
  emit("update:modelValue", { ...props.modelValue, selected });
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
      <ModifierTable
        v-if="question.id === 'boons'"
        :model-value="modelValue.selected"
        :rows="BOON_TABLE_ROWS"
        :groups="BOON_GROUPS"
        label="Boons"
        column-label="Boon"
        badge-width="6.25rem"
        @update:model-value="setSelected"
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
