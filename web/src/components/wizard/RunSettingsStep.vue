<script setup lang="ts">
import type { RadioGroupItem } from "@nuxt/ui";
import type {
  AccessibilityMode,
  AncientRelicLocation,
  AncientRelicPool,
  RunAnswers,
} from "../../wizard/WizardAnswers";
import type { GeneratedNumberRange } from "../../wizard/WizardOptionKey";
import {
  runSetupStep,
  type WizardQuestion as WizardQuestionDefinition,
} from "../../wizard/WizardStep";
import WizardQuestion from "./WizardQuestion.vue";

const props = defineProps<{
  modelValue: RunAnswers;
  relicChoiceRange: GeneratedNumberRange;
  progressionBalancingRange: GeneratedNumberRange;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: RunAnswers];
}>();

/** Preset values named by Archipelago's progression-balancing documentation. */
const PROGRESSION_PRESETS = [
  { label: "Extreme", value: 99 },
  { label: "Normal", value: 50 },
  { label: "Disabled", value: 0 },
] as const;

// Keep the public slider fixed to the requested 0-99 range and schema limits.
const progressionMinimum = Math.max(0, props.progressionBalancingRange.minimum);
const progressionMaximum = Math.min(
  99,
  props.progressionBalancingRange.maximum,
);

const ancientLocationItems: RadioGroupItem[] = [
  {
    label: "At the start of each act",
    description:
      "Claim the reward through that act's normal Ancient encounter.",
    value: "start_of_act",
  },
  {
    label: "As soon as it arrives",
    description:
      "Claim linked choices from the Archipelago reward menu immediately.",
    value: "anytime",
  },
];

const ancientPoolItems: RadioGroupItem[] = [
  {
    label: "Balanced",
    description:
      "Use the Ancient relic pool naturally associated with each act.",
    value: "balanced",
  },
  {
    label: "Chaos",
    description:
      "Allow any Ancient from the appropriate act to supply the reward choices.",
    value: "chaos",
  },
  {
    label: "True Chaos",
    description:
      "Combine the Act 2 and Act 3 Ancient pools for both progressive rewards.",
    value: "true_chaos",
  },
];

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

// Resolve full question definitions by ID so flow definitions remain the copy source of truth.
const questionsById: Record<string, WizardQuestionDefinition> = {};

for (const question of runSetupStep.questions) {
  questionsById[question.id] = question;
}

/**
 * Emits a new Run Rules answer object with selected fields replaced.
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
 * Updates when Progressive Ancient rewards become available.
 *
 * @param value - Semantic timing value emitted by Nuxt UI's radio group.
 * @returns Nothing; ignores values outside the supported timing modes.
 */
function setAncientRelicLocation(value: unknown): void {
  // Narrow the generic component event to the two player-facing timing choices.
  if (value !== "start_of_act" && value !== "anytime") {
    return;
  }

  // Store semantic intent rather than writing a generated option key.
  updateAnswers({ ancientRelicLocation: value as AncientRelicLocation });
}

/**
 * Updates which Ancient relic pool supplies reward choices.
 *
 * @param value - Semantic pool value emitted by Nuxt UI's radio group.
 * @returns Nothing; ignores values outside the supported pool modes.
 */
function setAncientRelicPool(value: unknown): void {
  // Narrow the generic component event to the three player-facing pool choices.
  if (value !== "balanced" && value !== "chaos" && value !== "true_chaos") {
    return;
  }

  // Store semantic intent rather than writing a generated option key.
  updateAnswers({ ancientRelicPool: value as AncientRelicPool });
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
 * Updates whether Neow's starting blessing is shuffled.
 *
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; ignores the checkbox's unsupported indeterminate state.
 */
function setNeowSanity(value: unknown): void {
  // This binary answer never stores Nuxt UI's optional indeterminate state.
  if (typeof value !== "boolean") {
    return;
  }

  // Persist the player-facing toggle for later section compilation.
  updateAnswers({ neowSanity: value });
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

/**
 * Updates how strongly Archipelago moves progression toward earlier checks.
 *
 * @param value - Scalar or array value emitted by Nuxt UI's generic slider API.
 * @returns Nothing; ignores array, empty, decimal, and out-of-range states.
 */
function setProgressionBalancing(value: number | number[] | undefined): void {
  // The one-thumb slider stores only whole scalar values inside its generated bounds.
  if (
    typeof value !== "number" ||
    !Number.isInteger(value) ||
    value < progressionMinimum ||
    value > progressionMaximum
  ) {
    return;
  }

  // Persist the selected value for the Run compiler and preset-state display.
  updateAnswers({ progressionBalancing: value });
}

/**
 * Applies one named progression-balancing preset.
 *
 * @param value - Exact preset value selected by the player.
 * @returns Nothing; delegates to the same guarded update path as the slider.
 */
function setProgressionPreset(value: number): void {
  // Reuse slider validation so UI buttons cannot bypass generated range limits.
  setProgressionBalancing(value);
}

/**
 * Gets the name associated with a progression-balancing value.
 *
 * @param value - Current whole-number value from zero through ninety-nine.
 * @returns Extreme, Normal, Disabled, or Custom for every non-preset value.
 */
function getProgressionPresetLabel(value: number): string {
  // Compare the three documented values before falling back to a custom setting.
  for (const preset of PROGRESSION_PRESETS) {
    if (preset.value === value) {
      return preset.label;
    }
  }

  // Any other valid slider position is intentionally presented as custom.
  return "Custom";
}

/**
 * Updates the reachability standard used during Archipelago generation.
 *
 * @param value - Semantic accessibility mode emitted by Nuxt UI's radio group.
 * @returns Nothing; ignores values outside the two generated modes.
 */
function setAccessibility(value: unknown): void {
  // Narrow the generic component event to supported player-facing accessibility modes.
  if (value !== "full" && value !== "minimal") {
    return;
  }

  // Store semantic intent while the Run compiler owns the generated option key.
  updateAnswers({ accessibility: value as AccessibilityMode });
}
</script>

<template>
  <div class="space-y-8">
    <WizardQuestion :question="questionsById['ancient-location']!">
      <template #help>
        Progressive Ancient items provide linked relic choices independently of
        normal relic rewards.
      </template>

      <URadioGroup
        :model-value="modelValue.ancientRelicLocation"
        :items="ancientLocationItems"
        value-key="value"
        label-key="label"
        description-key="description"
        color="primary"
        variant="table"
        :ui="{ item: 'cursor-pointer' }"
        @update:model-value="setAncientRelicLocation"
      />
    </WizardQuestion>

    <WizardQuestion :question="questionsById['ancient-pool']!">
      <URadioGroup
        :model-value="modelValue.ancientRelicPool"
        :items="ancientPoolItems"
        value-key="value"
        label-key="label"
        description-key="description"
        color="primary"
        variant="table"
        :ui="{ item: 'cursor-pointer' }"
        @update:model-value="setAncientRelicPool"
      />
    </WizardQuestion>

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

    <WizardQuestion :question="questionsById.neow!">
      <UCheckbox
        :model-value="modelValue.neowSanity"
        label="Shuffle Neow's blessing"
        description="Adds Neow's Act 1 Ancient encounter as a location and a third Progressive Ancient reward."
        color="primary"
        variant="card"
        class="cursor-pointer"
        :ui="{
          label: 'cursor-pointer',
          description: 'cursor-pointer',
        }"
        @update:model-value="setNeowSanity"
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

    <section class="wizard-subsection">
      <div class="wizard-subsection__header">
        <h3>Archipelago defaults</h3>
        <p>General generation settings shared with other Archipelago games.</p>
      </div>

      <div class="space-y-8">
        <WizardQuestion :question="questionsById['progression-balancing']!">
          <template #help>
            Lower values permit more early-game droughts. Higher values move
            progression earlier; 0 disables balancing, 50 is normal, and 99 is
            extreme.
          </template>

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
                @click="setProgressionPreset(preset.value)"
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
  </div>
</template>

<style scoped src="./wizard.css"></style>
