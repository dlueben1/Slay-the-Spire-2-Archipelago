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
  relicRewardsAvailableAnytimeRange: GeneratedNumberRange;
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
 * Updates how many received Relic items may be claimed without an in-run reward.
 *
 * @param value - Numeric value emitted by Nuxt UI's input-number control.
 * @returns Nothing; ignores the input's temporary empty state.
 */
function setRelicRewardsAvailableAnytime(
  value: number | null | undefined,
): void {
  // Retain the last complete answer while the numeric control is temporarily empty.
  if (value === null || value === undefined) {
    return;
  }

  // Generated-schema validation remains authoritative for the inclusive 0-10 range.
  updateAnswers({ relicRewardsAvailableAnytime: value });
}

/**
 * Updates whether victory releases the winning character's remaining checks.
 *
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; ignores the checkbox's unsupported indeterminate state.
 */
function setReleaseOnVictory(value: unknown): void {
  // This generated toggle accepts only a concrete true or false value.
  if (typeof value !== "boolean") {
    return;
  }

  // Persist the player-facing toggle for Gameplay Modifiers compilation.
  updateAnswers({ releaseOnVictory: value });
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

    <WizardQuestion :question="questionsById['release-on-victory']!">
      <UCheckbox
        :model-value="modelValue.releaseOnVictory"
        label="Release remaining checks on victory"
        description="Release the winning character's unfinished checks as soon as their goal is recorded."
        color="primary"
        variant="card"
        class="cursor-pointer"
        :ui="{
          label: 'cursor-pointer',
          description: 'cursor-pointer',
        }"
        @update:model-value="setReleaseOnVictory"
      />
    </WizardQuestion>

    <WizardQuestion
      :question="questionsById['relic-rewards-available-anytime']!"
    >
      <template #help
        >Note: Relics will always be shuffled into the Multiworld, this setting
        just controls how many Relics can be claimed from the Loot Menu versus a
        Vanilla location.</template
      >

      <UInputNumber
        :model-value="modelValue.relicRewardsAvailableAnytime"
        :min="relicRewardsAvailableAnytimeRange.minimum"
        :max="relicRewardsAvailableAnytimeRange.maximum"
        color="primary"
        variant="outline"
        class="wizard-number-input"
        @update:model-value="setRelicRewardsAvailableAnytime"
      />
    </WizardQuestion>
  </div>
</template>

<style scoped src="./wizard.css"></style>
