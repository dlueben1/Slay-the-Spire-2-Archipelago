<script setup lang="ts">
import { computed } from "vue";
import { optionCatalog } from "../../../generated/optionCatalog";
import {
  createTrapDisplayItems,
  TRAP_WEIGHTS,
  type TrapAnswers,
  type TrapItemId,
} from "../../../wizard/TrapItem";
import type { WizardQuestion as Question } from "../../../wizard/WizardStep";
import WizardQuestion from "../core/WizardQuestion.vue";
import ItemWeightTable from "./ItemWeightTable.vue";

const props = defineProps<{ modelValue: TrapAnswers; question: Question }>();
const emit = defineEmits<{ "update:modelValue": [value: TrapAnswers] }>();
const items = createTrapDisplayItems(optionCatalog);
const levels = computed(
  () =>
    Object.fromEntries(
      items.map(({ id }) => [
        id,
        TRAP_WEIGHTS.indexOf(props.modelValue.weights[id]),
      ]),
    ) as Record<TrapItemId, number>,
);

function updateWeight(id: TrapItemId, level: number): void {
  const weight = TRAP_WEIGHTS[level];
  if (!weight) return;
  emit("update:modelValue", {
    ...props.modelValue,
    weights: { ...props.modelValue.weights, [id]: weight },
  });
}
</script>

<template>
  <WizardQuestion :question="question">
    <template #help>
      Weights are relative. Exempt excludes a trap; keep at least one type
      enabled when Trap Chance is above 0%.
    </template>
    <ItemWeightTable
      heading="Trap"
      :items="items"
      :weights="levels"
      @update:weight="updateWeight"
    />
  </WizardQuestion>
</template>
