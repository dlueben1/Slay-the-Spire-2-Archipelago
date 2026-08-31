<script setup lang="ts">
import type { RadioGroupItem } from "@nuxt/ui";
import { computed } from "vue";
import { getAnswerAtPath, setAnswerAtPath } from "../../../wizard/answerPath";
import {
  narrowBooleanValue,
  narrowChoiceValue,
  narrowIntegerValue,
} from "../../../wizard/controlAdapters";
import type {
  WizardCheckboxGridItem,
  WizardControlRange,
  WizardControlValue,
  WizardSectionTransition,
} from "../../../wizard/QuestionControl";
import type { GeneratedNumberRange } from "../../../wizard/WizardOptionKey";
import {
  isQuestionEnabled,
  type WizardQuestion as WizardQuestionDefinition,
} from "../../../wizard/WizardStep";
import WizardSliderValue from "./controls/WizardSliderValue.vue";
import { useWizardAnswers } from "./wizardAnswersContext";

/**
 * Renders the standard control declared by one wizard question.
 *
 * Step components pass their section answer object as `modelValue`; this component
 * reads and writes the descriptor's `field` path, narrows Nuxt UI's loose event
 * values once through the shared adapters, and emits a complete immutable section.
 * Cross-field rules run through the descriptor's pure `applyChange` transition, so
 * no gameplay behavior lives in this presentation dispatcher.
 */
const props = defineProps<{
  question: WizardQuestionDefinition;
  modelValue: object;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: object];
}>();

// Flow predicates and dynamic ranges evaluate against the complete answer model.
const answers = useWizardAnswers();

/** Narrowed per-kind views of the descriptor for typed template bindings. */
const control = computed(() => props.question.control ?? null);
const radioControl = computed(() =>
  control.value?.kind === "radio" ? control.value : null,
);
const checkboxControl = computed(() =>
  control.value?.kind === "checkbox" ? control.value : null,
);
const checkboxGridControl = computed(() =>
  control.value?.kind === "checkbox-grid" ? control.value : null,
);
const numberControl = computed(() =>
  control.value?.kind === "number" ? control.value : null,
);
const numberGridControl = computed(() =>
  control.value?.kind === "number-grid" ? control.value : null,
);
const sliderControl = computed(() =>
  control.value?.kind === "slider" ? control.value : null,
);

/** Whether the whole question currently accepts input. */
const questionEnabled = computed(() =>
  isQuestionEnabled(props.question, answers),
);

/** Nuxt UI items derived from the descriptor's declarative radio choices. */
const radioItems = computed<RadioGroupItem[]>(
  () =>
    radioControl.value?.choices.map((choice) => ({
      label: choice.label,
      description: choice.description,
      value: choice.value,
    })) ?? [],
);

/** Reads one bound field for display; descriptors guarantee the stored type. */
function fieldValue(field: string): unknown {
  return getAnswerAtPath(props.modelValue, field);
}

/** Resolves a descriptor range against current answers just before rendering. */
function resolveRange(range: WizardControlRange): GeneratedNumberRange {
  return range(answers);
}

/** Whether one checkbox-grid card accepts input right now. */
function isItemEnabled(item: WizardCheckboxGridItem): boolean {
  return questionEnabled.value && (item.isEnabled?.(answers) ?? true);
}

/**
 * Applies one narrowed value to the bound section and emits the result.
 *
 * @param field - Section-relative path owned by the changed control.
 * @param value - Value already narrowed by a shared control adapter.
 * @param applyChange - Optional pure cross-field rule replacing the direct write.
 * @returns Nothing; the parent step remains the owner of persistent state.
 */
function applyValue(
  field: string,
  value: WizardControlValue,
  applyChange?: WizardSectionTransition,
): void {
  const nextSection = applyChange
    ? applyChange(props.modelValue, value)
    : setAnswerAtPath(props.modelValue, field, value);

  emit("update:modelValue", nextSection);
}

/** Narrows and applies a radio selection to its declared choice values. */
function setChoice(value: unknown): void {
  const descriptor = radioControl.value;

  if (!descriptor) {
    return;
  }

  const narrowed = narrowChoiceValue(
    value,
    descriptor.choices.map((choice) => choice.value),
  );

  if (narrowed !== null) {
    applyValue(descriptor.field, narrowed, descriptor.applyChange);
  }
}

/** Narrows and applies one boolean toggle from a card or grid checkbox. */
function setBoolean(
  field: string,
  value: unknown,
  applyChange?: WizardSectionTransition,
): void {
  const narrowed = narrowBooleanValue(value);

  if (narrowed !== null) {
    applyValue(field, narrowed, applyChange);
  }
}

/** Narrows and applies one bounded integer from a number input or slider. */
function setInteger(
  field: string,
  value: unknown,
  range: WizardControlRange,
): void {
  const narrowed = narrowIntegerValue(value, resolveRange(range));

  // Retain the last complete answer while the numeric control is temporarily empty.
  if (narrowed !== null) {
    applyValue(field, narrowed);
  }
}
</script>

<template>
  <URadioGroup
    v-if="radioControl"
    :model-value="fieldValue(radioControl.field) as string"
    :items="radioItems"
    :disabled="!questionEnabled"
    value-key="value"
    label-key="label"
    description-key="description"
    color="primary"
    variant="table"
    :ui="{ item: 'cursor-pointer' }"
    @update:model-value="setChoice"
  />

  <UCheckbox
    v-else-if="checkboxControl"
    :model-value="fieldValue(checkboxControl.field) as boolean"
    :label="checkboxControl.label"
    :description="checkboxControl.description"
    :disabled="!questionEnabled"
    color="primary"
    variant="card"
    class="cursor-pointer"
    :ui="{
      label: 'cursor-pointer',
      description: 'cursor-pointer',
    }"
    @update:model-value="
      setBoolean(checkboxControl.field, $event, checkboxControl.applyChange)
    "
  />

  <div
    v-else-if="checkboxGridControl"
    :class="
      checkboxGridControl.layout === 'stack'
        ? 'space-y-3'
        : 'wizard-toggle-grid'
    "
  >
    <UCheckbox
      v-for="item in checkboxGridControl.items"
      :key="item.field"
      :model-value="fieldValue(item.field) as boolean"
      :label="item.label"
      :description="item.description"
      :disabled="!isItemEnabled(item)"
      color="primary"
      variant="card"
      class="cursor-pointer"
      :ui="{
        label: 'cursor-pointer',
        description: 'cursor-pointer',
      }"
      @update:model-value="setBoolean(item.field, $event, item.applyChange)"
    />
  </div>

  <UInputNumber
    v-else-if="numberControl"
    :model-value="fieldValue(numberControl.field) as number"
    :min="resolveRange(numberControl.range).minimum"
    :max="resolveRange(numberControl.range).maximum"
    :disabled="!questionEnabled"
    color="primary"
    variant="outline"
    class="wizard-number-input"
    @update:model-value="
      setInteger(numberControl.field, $event, numberControl.range)
    "
  />

  <div v-else-if="numberGridControl" class="wizard-number-grid">
    <label
      v-for="gridField in numberGridControl.fields"
      :key="gridField.field"
      class="wizard-number-card"
    >
      <span>{{ gridField.label }}</span>
      <UInputNumber
        :model-value="fieldValue(gridField.field) as number"
        :min="resolveRange(gridField.range).minimum"
        :max="resolveRange(gridField.range).maximum"
        :disabled="!questionEnabled"
        color="primary"
        variant="outline"
        class="wizard-number-input"
        @update:model-value="
          setInteger(gridField.field, $event, gridField.range)
        "
      />
    </label>
  </div>

  <WizardSliderValue
    v-else-if="sliderControl"
    :model-value="fieldValue(sliderControl.field) as number"
    :range="resolveRange(sliderControl.range)"
    :presets="sliderControl.presets"
    :unit="sliderControl.unit"
    :ariaLabelText="sliderControl.ariaLabel"
    @update:model-value="applyValue(sliderControl.field, $event)"
  />
</template>

<style scoped src="./wizard.css" />
