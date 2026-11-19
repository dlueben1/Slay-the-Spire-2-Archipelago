<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  BONUS_MAX_HP_DEFAULT,
  BONUS_MAX_HP_LIMIT,
} from "../../../wizard/compiler/applyBonusItemOptions";
import type { BonusItemAnswer } from "../../../wizard/WizardAnswers";

const props = defineProps<{
  open: boolean;
  /** The existing item when editing, or `null` when adding a new one. */
  editItem: BonusItemAnswer | null;
}>();

const emit = defineEmits<{
  "update:open": [value: boolean];
  submit: [value: BonusItemAnswer];
}>();

const amount = ref(BONUS_MAX_HP_DEFAULT);

const dialogTitle = computed(() =>
  props.editItem ? "Edit Max HP" : "Add Max HP",
);
const submitLabel = computed(() => (props.editItem ? "Save" : "Add"));
const isSubmittable = computed(
  () =>
    Number.isInteger(amount.value) &&
    amount.value >= 1 &&
    amount.value <= BONUS_MAX_HP_LIMIT,
);

watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) {
      return;
    }
    const item = props.editItem;
    amount.value =
      item && item.kind === "MAX_HP" ? item.amount : BONUS_MAX_HP_DEFAULT;
  },
  { immediate: true }
);

function setAmount(value: unknown): void {
  if (typeof value === "number") {
    amount.value = value;
  }
}

function submit(): void {
  if (!isSubmittable.value) {
    return;
  }
  emit("submit", { kind: "MAX_HP", amount: amount.value });
  emit("update:open", false);
}
</script>

<template>
  <UModal
    :open="open"
    :title="dialogTitle"
    description="When claimed, raise your max HP (and current HP) by this much."
    :ui="{ footer: 'justify-end' }"
    @update:open="emit('update:open', $event)"
  >
    <template #body>
      <label class="max-hp-dialog">
        <span>Max HP</span>
        <UInputNumber
          :model-value="amount"
          :min="1"
          :max="BONUS_MAX_HP_LIMIT"
          color="primary"
          variant="outline"
          class="wizard-number-input"
          @update:model-value="setAmount"
        />
      </label>
    </template>

    <template #footer>
      <UButton
        label="Cancel"
        color="neutral"
        variant="outline"
        class="cursor-pointer"
        @click="emit('update:open', false)"
      />
      <UButton
        :label="submitLabel"
        color="primary"
        class="cursor-pointer"
        :disabled="!isSubmittable"
        @click="submit"
      />
    </template>
  </UModal>
</template>

<style scoped src="../core/wizard.css" />

<style scoped>
.max-hp-dialog {
  display: grid;
  gap: 0.5rem;
  color: var(--ui-text-highlighted);
  font-weight: 700;
}
</style>
