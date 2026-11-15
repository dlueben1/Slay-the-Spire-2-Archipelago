<script setup lang="ts">
import { computed } from "vue";
import type { FillerDisplayItem } from "../../../wizard/FillerItem";
import type {
  BonusItemAnswer,
  FillerAnswers,
  ItemPoolAnswers,
} from "../../../wizard/WizardAnswers";
import type { TrapAnswers } from "../../../wizard/TrapItem";
import {
  getQuestionById,
  getVisibleQuestionIds,
  itemPoolSetupStep,
  resolveQuestionHelp,
  type WizardQuestion as WizardQuestionDefinition,
} from "../../../wizard/WizardStep";
import BonusItemsStep from "../bespoke/BonusItemsStep.vue";
import FillerStep from "../bespoke/FillerStep.vue";
import TrapStep from "../bespoke/TrapStep.vue";
import WizardControl from "../core/WizardControl.vue";
import WizardQuestion from "../core/WizardQuestion.vue";
import { useWizardAnswers } from "../core/wizardAnswersContext";

/**
 * Item Pool step: Bonus Items, filler weights and traps, which share the filler slots.
 *
 * The Bonus Items table, Filler odds table and trap weights are bespoke child
 * components; Trap Chance renders through `WizardControl` from `itemPoolSetupStep`.
 */
const props = defineProps<{
  modelValue: ItemPoolAnswers;
  fillerItems: FillerDisplayItem[];
}>();

const emit = defineEmits<{
  "update:modelValue": [value: ItemPoolAnswers];
}>();

// Flow predicates and help copy evaluate against the complete answer model.
const answers = useWizardAnswers();

/** Currently visible question IDs; trap weights follow the trap chance. */
const visibleQuestionIds = computed(
  () => new Set(getVisibleQuestionIds(itemPoolSetupStep, answers)),
);

/** Resolves one declared question for the template without non-null assertions. */
function question(questionId: string): WizardQuestionDefinition {
  return getQuestionById(itemPoolSetupStep, questionId);
}

/**
 * Emits a complete section answer with one nested family replaced.
 *
 * @param patch - Family changed by a bespoke child control.
 * @returns Nothing; emits the immutable Item Pool answer.
 */
function updateAnswers(patch: Partial<ItemPoolAnswers>): void {
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
  emit("update:modelValue", value as ItemPoolAnswers);
}

function setBonusItems(bonusItems: BonusItemAnswer[]): void {
  updateAnswers({ bonusItems });
}

function setFillerAnswers(filler: FillerAnswers): void {
  updateAnswers({ filler });
}

function setTrapAnswers(traps: TrapAnswers): void {
  updateAnswers({ traps });
}
</script>

<template>
  <div class="space-y-10">
    <section class="wizard-subsection border-t-0! pt-0!">
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

    <section class="wizard-subsection">
      <div class="wizard-subsection__header">
        <h3>Traps</h3>
        <p>
          Replace some filler rewards with traps. Leave the chance at 0% to play
          without traps.
        </p>
      </div>
      <WizardQuestion
        :question="question('trap-chance')"
        :help-text="resolveQuestionHelp(question('trap-chance'), answers)"
      >
        <WizardControl
          :question="question('trap-chance')"
          :model-value="modelValue"
          @update:model-value="forwardUpdate"
        />
      </WizardQuestion>
      <TrapStep
        v-if="visibleQuestionIds.has('trap-weights')"
        class="mt-6"
        :model-value="modelValue.traps"
        :question="question('trap-weights')"
        @update:model-value="setTrapAnswers"
      />
    </section>
  </div>
</template>

<style scoped src="../core/wizard.css" />
