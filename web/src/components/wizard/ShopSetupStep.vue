<script setup lang="ts">
import type { RadioGroupItem } from "@nuxt/ui";
import type { ShopAnswers, ShopCostMode } from "../../wizard/WizardAnswers";
import type { GeneratedNumberRange } from "../../wizard/WizardOptionKey";
import { checkSetupStep } from "../../wizard/WizardStep";
import WizardQuestion from "./WizardQuestion.vue";

type ShopSlotAnswerKey =
  "cardSlots" | "neutralCardSlots" | "relicSlots" | "potionSlots";

interface ShopSlotDefinition {
  key: ShopSlotAnswerKey;
  label: string;
}

const props = defineProps<{
  modelValue: ShopAnswers;
  slotRanges: Record<ShopSlotAnswerKey, GeneratedNumberRange>;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: ShopAnswers];
}>();

const shopSlotDefinitions: readonly ShopSlotDefinition[] = [
  { key: "cardSlots", label: "Colored card slots" },
  { key: "neutralCardSlots", label: "Neutral card slots" },
  { key: "relicSlots", label: "Relic slots" },
  { key: "potionSlots", label: "Potion slots" },
];

const shopCostItems: RadioGroupItem[] = [
  {
    label: "Fixed",
    description: "Every shuffled shop purchase costs 15 gold.",
    value: "Fixed",
  },
  {
    label: "Super-discount tiered",
    description: "Use 20% of the usual rarity-based shop price.",
    value: "Super_Discount_Tiered",
  },
  {
    label: "Discount tiered",
    description: "Use 50% of the usual rarity-based shop price.",
    value: "Discount_Tiered",
  },
  {
    label: "Tiered",
    description: "Use the ordinary rarity-based price for each slot.",
    value: "Tiered",
  },
];

// Resolve question copy by ID so flow definitions remain the copy source of truth.
const questionTitles: Record<string, string> = {};

for (const question of checkSetupStep.questions) {
  questionTitles[question.id] = question.title;
}

/**
 * Emits a new Shop answer object with selected fields replaced.
 *
 * @param patch - Shop answer fields changed by one presentation control.
 * @returns Nothing; emits the complete immutable section answer.
 */
function updateAnswers(patch: Partial<ShopAnswers>): void {
  // Preserve every answer not owned by the control that initiated the update.
  const nextAnswers = {
    ...props.modelValue,
    ...patch,
  };

  // Keep the parent view as the sole owner of persistent wizard state.
  emit("update:modelValue", nextAnswers);
}

/**
 * Updates one shuffled shop slot count.
 *
 * @param answerKey - Semantic slot-count field represented by the control.
 * @param value - Numeric value emitted by Nuxt UI's input-number control.
 * @returns Nothing; ignores the input's temporary empty state.
 */
function setSlotCount(
  answerKey: ShopSlotAnswerKey,
  value: number | null | undefined,
): void {
  // Retain the last complete answer while the numeric control is temporarily empty.
  if (value === null || value === undefined) {
    return;
  }

  // Update only the semantic slot field selected by the rendered definition.
  updateAnswers({ [answerKey]: value });
}

/**
 * Updates whether card removal becomes three progressive act unlocks.
 *
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; ignores the checkbox's unsupported indeterminate state.
 */
function setRemoveSlots(value: unknown): void {
  // This binary answer never stores Nuxt UI's optional indeterminate state.
  if (typeof value !== "boolean") {
    return;
  }

  // Store the semantic decision for the Shop compiler.
  updateAnswers({ removeSlots: value });
}

/**
 * Updates the rarity-based pricing model for shuffled shop purchases.
 *
 * @param value - Semantic cost mode emitted by Nuxt UI's radio group.
 * @returns Nothing; ignores values outside the generated four-mode contract.
 */
function setCosts(value: unknown): void {
  // Narrow the generic event to the cost choices implemented by this section.
  if (
    value !== "Fixed" &&
    value !== "Super_Discount_Tiered" &&
    value !== "Discount_Tiered" &&
    value !== "Tiered"
  ) {
    return;
  }

  // Store the semantic choice; the section compiler owns its generated option key.
  updateAnswers({ costs: value as ShopCostMode });
}
</script>

<template>
  <div class="space-y-8">
    <WizardQuestion :title="questionTitles['shop-slots']!">
      <template #help>
        Each count controls how many slots of that type are unavailable until
        their corresponding AP items arrive.
      </template>

      <div class="wizard-number-grid">
        <label
          v-for="definition in shopSlotDefinitions"
          :key="definition.key"
          class="wizard-number-card"
        >
          <span>{{ definition.label }}</span>
          <UInputNumber
            :model-value="modelValue[definition.key]"
            :min="slotRanges[definition.key].minimum"
            :max="slotRanges[definition.key].maximum"
            color="primary"
            variant="outline"
            class="wizard-number-input"
            @update:model-value="setSlotCount(definition.key, $event)"
          />
        </label>
      </div>
    </WizardQuestion>

    <WizardQuestion :title="questionTitles['shop-removal']!">
      <UCheckbox
        :model-value="modelValue.removeSlots"
        label="Shuffle card removal"
        description="Add one progressive card-removal unlock per act; Act 4 uses the Act 3 unlock."
        color="primary"
        variant="card"
        class="cursor-pointer"
        :ui="{
          label: 'cursor-pointer',
          description: 'cursor-pointer',
        }"
        @update:model-value="setRemoveSlots"
      />
    </WizardQuestion>

    <WizardQuestion :title="questionTitles['shop-costs']!">
      <template #help>
        Logic does not account for these prices, so high costs can make an
        unlucky shop less convenient.
      </template>

      <URadioGroup
        :model-value="modelValue.costs"
        :items="shopCostItems"
        value-key="value"
        label-key="label"
        description-key="description"
        color="primary"
        variant="table"
        :ui="{ item: 'cursor-pointer' }"
        @update:model-value="setCosts"
      />
    </WizardQuestion>
  </div>
</template>

<style scoped src="./wizard.css"></style>
