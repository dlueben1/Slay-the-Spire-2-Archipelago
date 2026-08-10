<script setup lang="ts">
import type { RunAnswers } from "../../wizard/WizardAnswers";
import type { GeneratedNumberRange } from "../../wizard/WizardOptionKey";
import {
  runSetupStep,
  type WizardQuestion as WizardQuestionDefinition,
} from "../../wizard/WizardStep";
import WizardQuestion from "./WizardQuestion.vue";

const props = defineProps<{
  modelValue: RunAnswers;
  relicChoiceRange: GeneratedNumberRange;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: RunAnswers];
}>();


// Resolve full question definitions by ID so flow definitions remain the copy source of truth.
const questionsById: Record<string, WizardQuestionDefinition> = {};

for (const question of runSetupStep.questions) {
  questionsById[question.id] = question;
}

/**
 * Emits a new Gameplay Modifiers answer object with selected fields replaced.
 *
 * @param patch - Run answer fields changed by one presentation control.
 * @returns Nothing; emits the complete immutable section answer.
 */
function updateAnswers(patch: Partial<RunAnswers>): void {
  // Preserve every answer not owned by the control that initiated the update.
  const nextAnswers = {
    ...props.modelValue,
    ...patch,
  };

  // Keep the parent view as the sole owner of persistent wizard state.
  emit("update:modelValue", nextAnswers);
}

/**
 * Updates the number of relics offered by an Archipelago Relic item.
 *
 * @param value - Numeric value emitted by Nuxt UI's input-number control.
 * @returns Nothing; ignores the input's temporary empty state.
 */
function setRelicChoiceCount(value: number | null | undefined): void {
  // Retain the last complete answer while the numeric control is temporarily empty.
  if (value === null || value === undefined) {
    return;
  }

  // Range enforcement remains authoritative in generated-schema validation.
  updateAnswers({ relicChoiceCount: value });
}

/**
 * Updates whether generated characters receive fixed run seeds.
 *
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; ignores the checkbox's unsupported indeterminate state.
 */
function setSeeded(value: unknown): void {
  // This binary answer never stores Nuxt UI's optional indeterminate state.
  if (typeof value !== "boolean") {
    return;
  }

  // Persist the player-facing toggle for later section compilation.
  updateAnswers({ seeded: value });
}

</script>

<template>
  <div class="space-y-8">
    <WizardQuestion :question="questionsById['relic-choice-count']!">
      <template #help>
        This affects Relic items received from Archipelago, not ordinary game or
        mod relic rewards.
      </template>

      <UInputNumber
        :model-value="modelValue.relicChoiceCount"
        :min="relicChoiceRange.minimum"
        :max="relicChoiceRange.maximum"
        color="primary"
        variant="outline"
        class="wizard-number-input"
        @update:model-value="setRelicChoiceCount"
      />
    </WizardQuestion>

    <WizardQuestion :question="questionsById.seeded!">
      <UCheckbox
        :model-value="modelValue.seeded"
        label="Use fixed seeds"
        description="Each generated character receives a repeatable seed for climbing the Spire."
        color="primary"
        variant="card"
        class="cursor-pointer"
        :ui="{
          label: 'cursor-pointer',
          description: 'cursor-pointer',
        }"
        @update:model-value="setSeeded"
      />
    </WizardQuestion>

  </div>
</template>

<style scoped src="./wizard.css"></style>
