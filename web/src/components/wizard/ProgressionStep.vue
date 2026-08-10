<script setup lang="ts">
import type { RadioGroupItem } from "@nuxt/ui";
import type {
  AccessibilityMode,
  ProgressionAnswers,
} from "../../wizard/WizardAnswers";
import type { GeneratedNumberRange } from "../../wizard/WizardOptionKey";
import {
  progressionSetupStep,
  type WizardQuestion as WizardQuestionDefinition,
} from "../../wizard/WizardStep";
import WizardQuestion from "./WizardQuestion.vue";

const props = defineProps<{
  modelValue: ProgressionAnswers;
  progressionBalancingRange: GeneratedNumberRange;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: ProgressionAnswers];
}>();

const PROGRESSION_PRESETS = [
  { label: "Extreme", value: 99 },
  { label: "Normal", value: 50 },
  { label: "Disabled", value: 0 },
] as const;
const progressionMinimum = Math.max(0, props.progressionBalancingRange.minimum);
const progressionMaximum = Math.min(
  99,
  props.progressionBalancingRange.maximum,
);

const accessibilityItems: RadioGroupItem[] = [
  {
    label: "Full",
    description:
      "Require every location and item in the world to remain reachable.",
    value: "full",
  },
  {
    label: "Minimal",
    description:
      "Require only the items and locations needed to complete the goal to remain reachable.",
    value: "minimal",
  },
];

const questionsById: Record<string, WizardQuestionDefinition> = {};
for (const question of progressionSetupStep.questions) {
  questionsById[question.id] = question;
}

/** Emits a complete Progression answer with the supplied fields replaced. */
function updateAnswers(patch: Partial<ProgressionAnswers>): void {
  emit("update:modelValue", { ...props.modelValue, ...patch });
}

/** Updates progression balancing with a whole value inside the generated bounds. */
function setProgressionBalancing(value: number | number[] | undefined): void {
  if (
    typeof value === "number" &&
    Number.isInteger(value) &&
    value >= progressionMinimum &&
    value <= progressionMaximum
  ) {
    updateAnswers({ progressionBalancing: value });
  }
}

/** Returns the matching named preset, or Custom for any other valid value. */
function getProgressionPresetLabel(value: number): string {
  return (
    PROGRESSION_PRESETS.find((preset) => preset.value === value)?.label ??
    "Custom"
  );
}

/** Updates Archipelago's reachability standard. */
function setAccessibility(value: unknown): void {
  if (value === "full" || value === "minimal") {
    updateAnswers({ accessibility: value as AccessibilityMode });
  }
}
</script>

<template>
  <section class="wizard-subsection pt-0! border-t-0!">
    <div class="wizard-subsection__header">
      <h3>Archipelago Settings</h3>
      <p>General generation settings shared with other Archipelago games.</p>
    </div>

    <div class="space-y-8">
      <WizardQuestion :question="questionsById['progression-balancing']!">
        <div class="space-y-4">
          <div class="flex items-center justify-between gap-4">
            <UBadge color="primary" variant="subtle">
              {{ getProgressionPresetLabel(modelValue.progressionBalancing) }}
            </UBadge>
            <output class="font-bold text-amber-300">
              {{ modelValue.progressionBalancing }}
            </output>
          </div>
          <USlider
            :model-value="modelValue.progressionBalancing"
            :min="progressionMinimum"
            :max="progressionMaximum"
            :step="1"
            :tooltip="true"
            color="primary"
            size="lg"
            class="cursor-pointer"
            aria-label="Progression balancing value"
            @update:model-value="setProgressionBalancing"
          />
          <div class="flex flex-wrap gap-2">
            <UButton
              v-for="preset in PROGRESSION_PRESETS"
              :key="preset.label"
              color="primary"
              :variant="
                modelValue.progressionBalancing === preset.value
                  ? 'solid'
                  : 'soft'
              "
              size="sm"
              class="cursor-pointer"
              @click="setProgressionBalancing(preset.value)"
            >
              {{ preset.label }}
            </UButton>
          </div>
        </div>
      </WizardQuestion>

      <WizardQuestion :question="questionsById.accessibility!">
        <URadioGroup
          :model-value="modelValue.accessibility"
          :items="accessibilityItems"
          value-key="value"
          label-key="label"
          description-key="description"
          color="primary"
          variant="table"
          :ui="{ item: 'cursor-pointer' }"
          @update:model-value="setAccessibility"
        />
      </WizardQuestion>
    </div>
  </section>
</template>

<style scoped src="./wizard.css"></style>
