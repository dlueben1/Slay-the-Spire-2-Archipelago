<script setup lang="ts">
import type { FillerDisplayItem } from "../../../wizard/FillerItem";
import type {
  FillerAnswers,
  FillerItemId,
  FillerWeightLevel,
} from "../../../wizard/WizardAnswers";
import type { WizardQuestion as Question } from "../../../wizard/WizardStep";
import WizardQuestion from "../core/WizardQuestion.vue";
import ItemWeightTable from "./ItemWeightTable.vue";

const props = defineProps<{
  modelValue: FillerAnswers;
  items: FillerDisplayItem[];
  question: Question;
}>();
const emit = defineEmits<{ "update:modelValue": [value: FillerAnswers] }>();

function updateWeight(id: FillerItemId, level: number): void {
  emit("update:modelValue", {
    weights: { ...props.modelValue.weights, [id]: level as FillerWeightLevel },
  });
}
</script>

<template>
  <WizardQuestion :question="question">
    <template #help>
      Weights are relative to one another. Raising one filler makes it more
      likely than fillers with lower settings; it does not guarantee an exact
      percentage.
    </template>
    <ItemWeightTable
      heading="Buff"
      :items="items"
      :weights="modelValue.weights"
      @update:weight="updateWeight"
    />
  </WizardQuestion>
</template>
