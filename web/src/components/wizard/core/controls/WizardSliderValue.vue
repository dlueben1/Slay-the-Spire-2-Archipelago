<script setup lang="ts">
import { computed } from "vue";
import { narrowIntegerValue } from "../../../../wizard/controlAdapters";
import type { WizardSliderPreset } from "../../../../wizard/QuestionControl";
import type { GeneratedNumberRange } from "../../../../wizard/WizardOptionKey";

/**
 * Whole-number slider with a live value readout for declarative slider controls.
 *
 * Two established layouts are reproduced from the descriptor alone: presets render
 * the stacked badge-header arrangement (Progression balancing), while preset-free
 * sliders render the inline value-suffix arrangement (Death Link damage).
 */
const props = defineProps<{
  modelValue: number;
  range: GeneratedNumberRange;
  presets?: readonly WizardSliderPreset[];
  /** Suffix such as `%` appended to the value readout. */
  unit?: string;
  /** Accessible slider name; named to avoid Vue's global `aria-label` attribute. */
  ariaLabelText: string;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: number];
}>();

/** Returns the matching named preset, or Custom for any other valid value. */
function getPresetLabel(value: number): string {
  return (
    props.presets?.find((preset) => preset.value === value)?.label ?? "Custom"
  );
}

const presetLabel = computed(() => getPresetLabel(props.modelValue));

/**
 * Emits a slider change once it narrows to a supported whole number.
 *
 * @param value - Scalar or array value emitted by Nuxt UI's generic slider API.
 * @returns Nothing; ignores array and temporarily empty slider states.
 */
function setValue(value: number | number[] | undefined): void {
  const narrowed = narrowIntegerValue(value, props.range);

  // Retain the last complete answer while the control reports an unusable state.
  if (narrowed === null) {
    return;
  }

  emit("update:modelValue", narrowed);
}
</script>

<template>
  <!-- Preset sliders use the stacked layout with a named-value badge header. -->
  <div v-if="presets?.length" class="space-y-4">
    <div class="flex items-center justify-between gap-4">
      <UBadge color="primary" variant="subtle">
        {{ presetLabel }}
      </UBadge>
      <output class="font-bold text-amber-300">
        {{ modelValue }}{{ unit }}
      </output>
    </div>
    <USlider
      :model-value="modelValue"
      :min="range.minimum"
      :max="range.maximum"
      :step="1"
      :tooltip="true"
      color="primary"
      size="lg"
      class="cursor-pointer"
      :aria-label="ariaLabelText"
      @update:model-value="setValue"
    />
    <div class="flex flex-wrap gap-2">
      <UButton
        v-for="preset in presets"
        :key="preset.label"
        color="primary"
        :variant="modelValue === preset.value ? 'solid' : 'soft'"
        size="sm"
        class="cursor-pointer"
        @click="setValue(preset.value)"
      >
        {{ preset.label }}
      </UButton>
    </div>
  </div>

  <!-- Preset-free sliders keep the compact inline readout beside the track. -->
  <div v-else class="flex items-center gap-4">
    <USlider
      :model-value="modelValue"
      :min="range.minimum"
      :max="range.maximum"
      :step="1"
      :tooltip="true"
      color="primary"
      size="lg"
      class="cursor-pointer"
      :aria-label="ariaLabelText"
      @update:model-value="setValue"
    />

    <output
      class="min-w-16 rounded-md border border-amber-500/30 bg-black/25 px-3 py-1 text-center font-bold text-amber-300"
    >
      {{ modelValue }}{{ unit }}
    </output>
  </div>
</template>
