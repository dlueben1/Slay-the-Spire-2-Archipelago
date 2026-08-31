<script setup lang="ts">
import { computed } from "vue";
import type { FillerDisplayItem } from "../../../wizard/FillerItem";
import type {
  BonusItemAnswer,
  ChecksAndRewardsAnswers,
  FillerAnswers,
} from "../../../wizard/WizardAnswers";
import {
  checkSetupStep,
  getQuestionById,
  getVisibleQuestionIds,
  resolveQuestionHelp,
  type WizardQuestion as WizardQuestionDefinition,
} from "../../../wizard/WizardStep";
import BonusItemsStep from "../bespoke/BonusItemsStep.vue";
import FillerStep from "../bespoke/FillerStep.vue";
import WizardControl from "../core/WizardControl.vue";
import WizardQuestion from "../core/WizardQuestion.vue";
import { useWizardAnswers } from "../core/wizardAnswersContext";

/**
 * Checks & Rewards step: subsection layout around declaratively rendered controls.
 *
 * Every standard question — Starting Equipment, Ancients, the check-types grid, and
 * the dependent Shop Sanity family — renders through `WizardControl` from its
 * definition in `checkSetupStep`; the Floor Checks dependency rule lives in
 * `wizard/checksTransitions.ts`. Only the Bonus Items table and the Filler odds
 * table remain bespoke child components, and this shell owns the subsection
 * headers that group everything for the player.
 */
const props = defineProps<{
  modelValue: ChecksAndRewardsAnswers;
  fillerItems: FillerDisplayItem[];
}>();

const emit = defineEmits<{
  "update:modelValue": [value: ChecksAndRewardsAnswers];
}>();

// Flow predicates and help copy evaluate against the complete answer model.
const answers = useWizardAnswers();

/** Currently visible question IDs; subsection shells follow their questions. */
const visibleQuestionIds = computed(
  () => new Set(getVisibleQuestionIds(checkSetupStep, answers)),
);

/** Resolves one declared question for the template without non-null assertions. */
function question(questionId: string): WizardQuestionDefinition {
  return getQuestionById(checkSetupStep, questionId);
}

/**
 * Emits a complete combined answer with one nested section replaced.
 *
 * @param patch - Bonus Items or Filler section changed by a bespoke child control.
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
 * Forwards a generically rendered section update to the owning parent view.
 *
 * @param value - Complete immutable section emitted by the control renderer.
 * @returns Nothing; keeps the parent view the sole owner of persistent state.
 */
function forwardUpdate(value: object): void {
  // The renderer edits this step's bound section, so the cast restores its type.
  emit("update:modelValue", value as ChecksAndRewardsAnswers);
}

/**
 * Accepts a complete Bonus Items list update from its focused child component.
 *
 * @param bonusItems - Updated ordered Bonus Item answers.
 * @returns Nothing; replaces the nested Bonus Items answer immutably.
 */
function setBonusItems(bonusItems: BonusItemAnswer[]): void {
  // Delegate combined-section ownership to the shared immutable update helper.
  updateAnswers({ bonusItems });
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
        <h3>Starting Equipment</h3>
        <p>
          Choose whether compatible starter cards and relics are restored and
          upgraded through progressive Archipelago items.
        </p>
      </div>

      <WizardQuestion
        :question="question('starting-equipment')"
        :help-text="resolveQuestionHelp(question('starting-equipment'), answers)"
      >
        <WizardControl
          :question="question('starting-equipment')"
          :model-value="modelValue"
          @update:model-value="forwardUpdate"
        />
      </WizardQuestion>
    </section>

    <section class="wizard-subsection">
      <div class="wizard-subsection__header">
        <h3>Ancients</h3>
        <p>
          Configure the behavior of unlocking Ancients and receiving their
          relics.
        </p>
      </div>

      <WizardQuestion :question="question('ancient-location')">
        <WizardControl
          :question="question('ancient-location')"
          :model-value="modelValue"
          @update:model-value="forwardUpdate"
        />
      </WizardQuestion>

      <WizardQuestion :question="question('ancient-pool')" class="mt-6">
        <WizardControl
          :question="question('ancient-pool')"
          :model-value="modelValue"
          @update:model-value="forwardUpdate"
        />
      </WizardQuestion>
    </section>

    <USeparator color="primary" class="opacity-40" />

    <WizardQuestion
      :question="question('check-types')"
      :help-text="resolveQuestionHelp(question('check-types'), answers)"
    >
      <WizardControl
        :question="question('check-types')"
        :model-value="modelValue"
        @update:model-value="forwardUpdate"
      />
    </WizardQuestion>

    <section
      v-if="visibleQuestionIds.has('shop-slots')"
      class="wizard-subsection"
    >
      <div class="wizard-subsection__header">
        <h3>Shop Sanity</h3>
        <p>
          Configure which parts of the Shop are shuffled into the Multiworld,
          and how much AP Items cost.
        </p>
      </div>

      <div class="space-y-8">
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
      </div>
    </section>

    <section class="wizard-subsection">
      <div class="wizard-subsection__header">
        <h3>Bonus Items</h3>
        <p>
          Add powerful guaranteed rewards to the item pool, ahead of any filler.
        </p>
      </div>

      <BonusItemsStep
        :model-value="modelValue.bonusItems"
        :question="question('bonus-items')"
        @update:model-value="setBonusItems"
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
        :question="question('filler-weights')"
        @update:model-value="setFillerAnswers"
      />
    </section>
  </div>
</template>

<style scoped src="../core/wizard.css" />
