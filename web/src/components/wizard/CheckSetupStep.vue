<script setup lang="ts">
import type { RadioGroupItem } from "@nuxt/ui";
import type { FillerDisplayItem } from "../../wizard/FillerItem";
import type {
  AncientAnswers,
  CheckAnswers,
  ChecksAndRewardsAnswers,
  FillerAnswers,
  ShopAnswers,
} from "../../wizard/WizardAnswers";
import type { GeneratedNumberRange } from "../../wizard/WizardOptionKey";
import {
  checkSetupStep,
  type WizardQuestion as WizardQuestionDefinition,
} from "../../wizard/WizardStep";
import FillerStep from "./FillerStep.vue";
import ShopSetupStep from "./ShopSetupStep.vue";
import WizardQuestion from "./WizardQuestion.vue";
import { MarkdownDocument } from "../../models/MarkdownDocument.ts";

type ShopSlotAnswerKey =
  "cardSlots" | "neutralCardSlots" | "relicSlots" | "potionSlots";

const props = defineProps<{
  modelValue: ChecksAndRewardsAnswers;
  fillerItems: FillerDisplayItem[];
  shopSlotRanges: Record<ShopSlotAnswerKey, GeneratedNumberRange>;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: ChecksAndRewardsAnswers];
}>();

interface CheckToggleDefinition {
  key: keyof CheckAnswers;
  label: string;
  description: string;
}

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
      "Claim linked choices from the Archipelago reward menu at any time.",
    value: "anytime",
  },
];

const ancientPoolItems: RadioGroupItem[] = [
  {
    label: "Vanilla",
    description:
      "Use the base game behavior: Visiting an Ancient provides relics from their own relic pool.",
    value: "balanced",
  },
  {
    label: "Chaos",
    description:
      "Ancient Relic Rewards are comprised of any relic an Ancient from that Act can give you.",
    value: "chaos",
  },
  {
    label: "True Chaos",
    description:
      "Ancient Relic Rewards are comprised of any relic from any Ancient in the game, regardless of Act (except Neow)",
    value: "true_chaos",
  },
];

/**
 * Player-facing rows for the independent additional check and reward toggles.
 *
 * @remarks Add a new row here only after its semantic answer, generated-key registry,
 * compiler assignment, and generated schema entry have also been added.
 */
const checkToggleDefinitions: readonly CheckToggleDefinition[] = [
  {
    key: "neowSanity",
    label: "Neow's Blessing",
    description:
      "Include Neow's Blessing as a third Progressive Ancient reward.",
  },
  {
    key: "includeFloorChecks",
    label: "Floor Checks",
    description:
      "Make reaching new floors into locations and add helpful filler items to fill them.",
  },
  {
    key: "campfireSanity",
    label: "Campfire Actions",
    description:
      "Shuffle Progressive Rest and Smith items into the Multiworld, per character, per Act.",
  },
  {
    key: "goldSanity",
    label: "Gold Rewards",
    description:
      "Move combat, elite, and boss gold rewards into the Multiworld.",
  },
  {
    key: "potionSanity",
    label: "Potion Drops",
    description: "Move potion rewards into the Multiworld.",
  },
  {
    key: "shuffleAllCards",
    label: "All Card Rewards",
    description:
      "Shuffle every card reward instead of the default behavior of shuffling every other reward.",
  },
];

// Resolve full nested question definitions by ID from the combined declarative step.
const questionsById: Record<string, WizardQuestionDefinition> = {};

for (const question of checkSetupStep.questions) {
  questionsById[question.id] = question;
}

/**
 * Emits a complete combined answer with one nested section replaced.
 *
 * @param patch - Checks, Shop, or Filler section changed by a child control.
 * @returns Nothing; emits the immutable combined Checks & Rewards answer.
 */
function updateAnswers(patch: Partial<ChecksAndRewardsAnswers>): void {
  // Preserve nested sections not owned by the control that initiated the update.
  const nextAnswers = {
    ...props.modelValue,
    ...patch,
  };

  // Keep the parent view as the sole owner of persistent wizard state.
  emit("update:modelValue", nextAnswers);
}

/**
 * Updates one ordinary check or reward toggle.
 *
 * @param answerKey - Semantic answer field represented by the changed row.
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; ignores the checkbox's unsupported indeterminate state.
 */
function setCheckToggle(answerKey: keyof CheckAnswers, value: unknown): void {
  // Binary sanity answers never store Nuxt UI's optional indeterminate state.
  if (typeof value !== "boolean") {
    return;
  }

  // Clone the nested check answers before replacing them in the combined section.
  const checks = {
    ...props.modelValue.checks,
    [answerKey]: value,
  };

  // Emit player intent without exposing generated option names to the component.
  updateAnswers({ checks });
}

/** Updates one Ancient reward setting above the additional checks subsection. */
function setAncientAnswers(patch: Partial<AncientAnswers>): void {
  updateAnswers({ ancients: { ...props.modelValue.ancients, ...patch } });
}

/** Narrows and updates the selected Progressive Ancient reward timing. */
function setAncientLocation(value: unknown): void {
  if (value === "start_of_act" || value === "anytime") {
    setAncientAnswers({ relicLocation: value });
  }
}

/** Narrows and updates the selected pool for Progressive Ancient rewards. */
function setAncientPool(value: unknown): void {
  if (value === "balanced" || value === "chaos" || value === "true_chaos") {
    setAncientAnswers({ relicPool: value });
  }
}

/**
 * Updates whether Shop Slots and their dependent subsection are enabled.
 *
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; ignores the checkbox's unsupported indeterminate state.
 */
function setShopEnabled(value: unknown): void {
  // This binary answer never stores Nuxt UI's optional indeterminate state.
  if (typeof value !== "boolean") {
    return;
  }

  // Preserve configured Shop values while hiding or revealing the subsection.
  const shop = {
    ...props.modelValue.shop,
    enabled: value,
  };

  // Replace only the nested Shop answer in the combined section.
  updateAnswers({ shop });
}

/**
 * Accepts a complete Shop subsection update from its focused child component.
 *
 * @param shop - Updated Shop slot, removal, and pricing answers.
 * @returns Nothing; replaces the nested Shop answer immutably.
 */
function setShopAnswers(shop: ShopAnswers): void {
  // Delegate combined-section ownership to the shared immutable update helper.
  updateAnswers({ shop });
}

/**
 * Accepts a complete Filler subsection update from the existing Filler UI.
 *
 * @param filler - Updated semantic filler-weight answers.
 * @returns Nothing; replaces the nested Filler answer immutably.
 */
function setFillerAnswers(filler: FillerAnswers): void {
  // Delegate combined-section ownership to the shared immutable update helper.
  updateAnswers({ filler });
}
</script>

<template>
  <div class="space-y-10">
    <WizardMarkdownDocument
      source="docs/faq-progressive.md"
      fallback-title="Progressive Items"
    />

    <section class="wizard-subsection border-t-0! pt-0!">
      <div class="wizard-subsection__header">
        <h3>Ancients</h3>
        <p>
          Configure the behavior of unlocking Ancients and receiving their
          relics.
        </p>
      </div>

      <WizardQuestion :question="questionsById['ancient-location']!">
        <URadioGroup
          :model-value="modelValue.ancients.relicLocation"
          :items="ancientLocationItems"
          value-key="value"
          label-key="label"
          description-key="description"
          color="primary"
          variant="table"
          :ui="{ item: 'cursor-pointer' }"
          @update:model-value="setAncientLocation"
        />
      </WizardQuestion>

      <WizardQuestion :question="questionsById['ancient-pool']!" class="mt-6">
        <URadioGroup
          :model-value="modelValue.ancients.relicPool"
          :items="ancientPoolItems"
          value-key="value"
          label-key="label"
          description-key="description"
          color="primary"
          variant="table"
          :ui="{ item: 'cursor-pointer' }"
          @update:model-value="setAncientPool"
        />
      </WizardQuestion>
    </section>

    <USeparator color="primary" class="opacity-40" />

    <WizardQuestion :question="questionsById['check-types']!">
      <template #help>
        Each enabled option adds or changes locations and items for every
        generated character.
      </template>

      <div class="wizard-toggle-grid">
        <UCheckbox
          v-for="definition in checkToggleDefinitions"
          :key="definition.key"
          :model-value="modelValue.checks[definition.key]"
          :label="definition.label"
          :description="definition.description"
          color="primary"
          variant="card"
          class="cursor-pointer"
          :ui="{
            label: 'cursor-pointer',
            description: 'cursor-pointer',
          }"
          @update:model-value="setCheckToggle(definition.key, $event)"
        />

        <UCheckbox
          :model-value="modelValue.shop.enabled"
          label="Shop Slots"
          description="Parts of the Shop can be shuffled into the Multiworld."
          color="primary"
          variant="card"
          class="cursor-pointer"
          :ui="{
            label: 'cursor-pointer',
            description: 'cursor-pointer',
          }"
          @update:model-value="setShopEnabled"
        />
      </div>
    </WizardQuestion>

    <section v-if="modelValue.shop.enabled" class="wizard-subsection">
      <div class="wizard-subsection__header">
        <h3>Shop Sanity</h3>
        <p>
          Configure which parts of the Shop are shuffled into the Multiworld,
          and how much AP Items cost.
        </p>
      </div>

      <ShopSetupStep
        :model-value="modelValue.shop"
        :slot-ranges="shopSlotRanges"
        @update:model-value="setShopAnswers"
      />
    </section>

    <section class="wizard-subsection">
      <div class="wizard-subsection__header">
        <h3>Filler Items</h3>
        <p>
          Choose how often each helpful filler reward appears relative to the
          others.
        </p>
      </div>

      <FillerStep
        :model-value="modelValue.filler"
        :items="fillerItems"
        :question="questionsById['filler-weights']!"
        @update:model-value="setFillerAnswers"
      />
    </section>
  </div>
</template>

<style scoped src="./wizard.css" />
