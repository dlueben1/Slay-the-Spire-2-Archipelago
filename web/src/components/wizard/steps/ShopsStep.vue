<script setup lang="ts">
import { computed } from "vue";
import { optionCatalog } from "../../../generated/optionCatalog";
import { createShopCardKindDisplayItems } from "../../../wizard/ShopCardKind";
import {
  createShopStockDisplayItems,
  SHOP_POTION_KIND_DEFINITIONS,
  SHOP_RELIC_KIND_DEFINITIONS,
} from "../../../wizard/ShopStockKind";
import type {
  FillerWeightLevel,
  ShopsAnswers,
} from "../../../wizard/WizardAnswers";
import {
  getQuestionById,
  getVisibleQuestionIds,
  resolveQuestionHelp,
  shopSetupStep,
  type WizardQuestion as WizardQuestionDefinition,
} from "../../../wizard/WizardStep";
import ItemWeightTable from "../bespoke/ItemWeightTable.vue";
import WizardControl from "../core/WizardControl.vue";
import WizardQuestion from "../core/WizardQuestion.vue";
import { useWizardAnswers } from "../core/wizardAnswersContext";

/**
 * Shops step: Shop Sanity first, then what shops sell.
 *
 * Standard questions render through `WizardControl` from `shopSetupStep`. The card,
 * relic and potion weight tables are bespoke; this shell owns the subsection headers
 * that keep Shop Sanity (checks) apart from the shop's stock (no checks).
 */
const props = defineProps<{
  modelValue: ShopsAnswers;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: ShopsAnswers];
}>();

// Flow predicates and help copy evaluate against the complete answer model.
const answers = useWizardAnswers();

/** Currently visible question IDs; dependent questions follow their switches. */
const visibleQuestionIds = computed(
  () => new Set(getVisibleQuestionIds(shopSetupStep, answers)),
);

/** Resolves one declared question for the template without non-null assertions. */
function question(questionId: string): WizardQuestionDefinition {
  return getQuestionById(shopSetupStep, questionId);
}

/**
 * Emits a complete section answer with one nested family replaced.
 *
 * @param patch - Family changed by a bespoke child control.
 * @returns Nothing; emits the immutable Shops answer.
 */
function updateAnswers(patch: Partial<ShopsAnswers>): void {
  emit("update:modelValue", { ...props.modelValue, ...patch });
}

/**
 * Forwards a generically rendered section update to the owning parent view.
 *
 * @param value - Complete immutable section emitted by the control renderer.
 * @returns Nothing; keeps the parent view the sole owner of persistent state.
 */
function forwardUpdate(value: object): void {
  // The renderer edits this step's bound section, so the cast restores its type.
  emit("update:modelValue", value as ShopsAnswers);
}

const shopCardKinds = createShopCardKindDisplayItems(optionCatalog);
const shopRelicKinds = createShopStockDisplayItems(
  SHOP_RELIC_KIND_DEFINITIONS,
  optionCatalog,
);
const shopPotionKinds = createShopStockDisplayItems(
  SHOP_POTION_KIND_DEFINITIONS,
  optionCatalog,
);
const SHOP_WEIGHT_LABELS = ["Never", "Low", "Medium", "High"] as const;

/** Shop stock families whose kinds each have a weight level. */
type ShopWeightFamily = "shopCards" | "shopRelics" | "shopPotions";

/** Replaces one kind's weight level in a shop stock table. */
function setShopWeight(
  family: ShopWeightFamily,
  id: string,
  level: number,
): void {
  const current = props.modelValue[family];
  updateAnswers({
    [family]: {
      ...current,
      weights: { ...current.weights, [id]: level as FillerWeightLevel },
    },
  });
}
</script>

<template>
  <div class="space-y-10">
    <section class="wizard-subsection border-t-0! pt-0!">
      <div class="wizard-subsection__header">
        <h3>Shop Sanity</h3>
        <p>
          Shuffle parts of the shop into the Multiworld, and choose how much
          they cost.
        </p>
      </div>

      <div class="space-y-8">
        <WizardQuestion :question="question('shop-sanity')">
          <WizardControl
            :question="question('shop-sanity')"
            :model-value="modelValue"
            @update:model-value="forwardUpdate"
          />
        </WizardQuestion>

        <template v-if="visibleQuestionIds.has('shop-slots')">
          <WizardQuestion
            :question="question('shop-slots')"
            :help-text="resolveQuestionHelp(question('shop-slots'), answers)"
          >
            <WizardControl
              :question="question('shop-slots')"
              :model-value="modelValue"
              @update:model-value="forwardUpdate"
            />
          </WizardQuestion>

          <WizardQuestion :question="question('shop-removal')">
            <WizardControl
              :question="question('shop-removal')"
              :model-value="modelValue"
              @update:model-value="forwardUpdate"
            />
          </WizardQuestion>

          <WizardQuestion :question="question('shop-costs')">
            <WizardControl
              :question="question('shop-costs')"
              :model-value="modelValue"
              @update:model-value="forwardUpdate"
            />
          </WizardQuestion>
        </template>
      </div>
    </section>

    <section class="wizard-subsection">
      <div class="wizard-subsection__header">
        <h3>Shop Cards</h3>
        <p>
          Change which cards the shop sells. This is separate from Shop Slots
          and adds no checks.
        </p>
      </div>

      <div class="space-y-8">
        <WizardQuestion
          :question="question('random-shop-cards')"
          :help-text="
            resolveQuestionHelp(question('random-shop-cards'), answers)
          "
        >
          <WizardControl
            :question="question('random-shop-cards')"
            :model-value="modelValue"
            @update:model-value="forwardUpdate"
          />
        </WizardQuestion>

        <WizardQuestion
          v-if="visibleQuestionIds.has('shop-card-weights')"
          :question="question('shop-card-weights')"
        >
          <ItemWeightTable
            heading="Card"
            :items="shopCardKinds"
            :weights="modelValue.shopCards.weights"
            :labels="SHOP_WEIGHT_LABELS"
            @update:weight="
              (id, level) => setShopWeight('shopCards', id, level)
            "
          />
        </WizardQuestion>
      </div>
    </section>

    <section class="wizard-subsection">
      <div class="wizard-subsection__header">
        <h3>Shop Relics</h3>
        <p>
          Change which relics the shop sells. This is separate from Shop Slots
          and adds no checks.
        </p>
      </div>

      <div class="space-y-8">
        <WizardQuestion
          :question="question('random-shop-relics')"
          :help-text="
            resolveQuestionHelp(question('random-shop-relics'), answers)
          "
        >
          <WizardControl
            :question="question('random-shop-relics')"
            :model-value="modelValue"
            @update:model-value="forwardUpdate"
          />
        </WizardQuestion>

        <WizardQuestion
          v-if="visibleQuestionIds.has('shop-relic-weights')"
          :question="question('shop-relic-weights')"
        >
          <ItemWeightTable
            heading="Relic"
            :items="shopRelicKinds"
            :weights="modelValue.shopRelics.weights"
            :labels="SHOP_WEIGHT_LABELS"
            @update:weight="
              (id, level) => setShopWeight('shopRelics', id, level)
            "
          />
        </WizardQuestion>
      </div>
    </section>

    <section class="wizard-subsection">
      <div class="wizard-subsection__header">
        <h3>Shop Potions</h3>
        <p>
          Change which potions the shop sells. This is separate from Shop Slots
          and adds no checks.
        </p>
      </div>

      <div class="space-y-8">
        <WizardQuestion
          :question="question('random-shop-potions')"
          :help-text="
            resolveQuestionHelp(question('random-shop-potions'), answers)
          "
        >
          <WizardControl
            :question="question('random-shop-potions')"
            :model-value="modelValue"
            @update:model-value="forwardUpdate"
          />
        </WizardQuestion>

        <WizardQuestion
          v-if="visibleQuestionIds.has('shop-potion-weights')"
          :question="question('shop-potion-weights')"
        >
          <ItemWeightTable
            heading="Potion"
            :items="shopPotionKinds"
            :weights="modelValue.shopPotions.weights"
            :labels="SHOP_WEIGHT_LABELS"
            @update:weight="
              (id, level) => setShopWeight('shopPotions', id, level)
            "
          />
        </WizardQuestion>
      </div>
    </section>
  </div>
</template>

<style scoped src="../core/wizard.css" />
