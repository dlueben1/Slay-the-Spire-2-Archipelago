<script setup lang="ts">
import type { DropdownMenuItem } from "@nuxt/ui";
import { computed, ref } from "vue";
import {
  getBonusItemDisplayRow,
  type BonusItemDisplayRow,
} from "../../wizard/BonusItemDisplay";
import type { BonusItemAnswer } from "../../wizard/WizardAnswers";
import type { WizardQuestion as WizardQuestionDefinition } from "../../wizard/WizardStep";
import WaxRelicDialog from "./WaxRelicDialog.vue";
import WizardQuestion from "./WizardQuestion.vue";

const props = defineProps<{
  modelValue: BonusItemAnswer[];
  question: WizardQuestionDefinition;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: BonusItemAnswer[]];
}>();

/** Dialog visibility plus the index being edited (`null` means Add mode). */
const isDialogOpen = ref(false);
const editIndex = ref<number | null>(null);

/** The item loaded into the dialog when editing, or `null` for a new row. */
const editItem = computed<BonusItemAnswer | null>(() =>
  editIndex.value === null ? null : (props.modelValue[editIndex.value] ?? null),
);

/**
 * Add-menu entries. Only Wax Relic exists in this pass; the remaining entries are
 * deliberate disabled affordances for bonus item types arriving in a future pass.
 */
const addMenuItems: DropdownMenuItem[] = [
  {
    label: "Add Wax Relic",
    icon: "i-glyphs-plus-bold",
    onSelect() {
      openAddDialog();
    },
  },
];

/**
 * Resolves the display row for one answer, tolerating corrupted persisted state.
 *
 * @param item - Semantic bonus item answer to render.
 * @returns The image/name/details triple, or a placeholder row for invalid entries.
 * @remarks The compiler rejects invalid entries at the validation boundary; the table
 * must still render them without crashing so the player can remove the broken row.
 */
function getDisplayRow(item: BonusItemAnswer): BonusItemDisplayRow {
  try {
    return getBonusItemDisplayRow(item);
  } catch {
    return {
      imageUrl: "",
      name: "Unknown Bonus Item",
      details: "This entry is no longer valid and should be removed.",
    };
  }
}

/** Opens the dialog with a fresh draft for a new Wax Relic. */
function openAddDialog(): void {
  editIndex.value = null;
  isDialogOpen.value = true;
}

/** Opens the dialog pre-filled with the requested existing row. */
function openEditDialog(index: number): void {
  if (index < 0 || index >= props.modelValue.length) {
    return;
  }
  editIndex.value = index;
  isDialogOpen.value = true;
}

/**
 * Removes one row by index.
 *
 * @param index - Zero-based row selected for removal.
 * @returns Nothing; emits the immutable collection without the removed row.
 * @remarks Bonus Items are optional and the table may legitimately become empty,
 * unlike the mandatory modded-character row.
 */
function removeBonusItem(index: number): void {
  if (index < 0 || index >= props.modelValue.length) {
    return;
  }

  const bonusItems = props.modelValue.filter(
    (_, itemIndex) => itemIndex !== index,
  );
  emit("update:modelValue", bonusItems);
}

/**
 * Commits the dialog's result: appends in Add mode, replaces in Edit mode.
 *
 * @param item - Completed semantic answer produced by the dialog.
 * @returns Nothing; emits the immutable collection containing the item.
 */
function submitBonusItem(item: BonusItemAnswer): void {
  if (editIndex.value === null) {
    emit("update:modelValue", [...props.modelValue, item]);
    return;
  }

  const bonusItems = [...props.modelValue];
  if (editIndex.value < bonusItems.length) {
    bonusItems[editIndex.value] = item;
  }
  emit("update:modelValue", bonusItems);
}
</script>

<template>
  <WizardQuestion :question="question">
    <template #help>
      Bonus Items are placed in the item pool before any filler is generated.
      There is no limit here, but generation fails if there are not enough
      filler slots for them.
    </template>

    <div class="bonus-item-section">
      <div class="bonus-item-table" role="table" aria-label="Bonus Items">
        <div class="bonus-item-table__header" role="row">
          <span class="bonus-item-table__image-heading" role="columnheader">
            <span class="sr-only">Image</span>
          </span>
          <span role="columnheader">Name</span>
          <span role="columnheader">Details</span>
          <div class="bonus-item-table__action" role="columnheader">
            <UDropdownMenu
              :items="addMenuItems"
              :modal="false"
              :content="{ align: 'end', side: 'bottom', sideOffset: 8 }"
            >
              <UTooltip text="Add Bonus Item">
                <UButton
                  type="button"
                  icon="i-glyphs-plus-bold"
                  color="primary"
                  variant="soft"
                  size="sm"
                  square
                  class="cursor-pointer"
                  aria-label="Add Bonus Item"
                  aria-haspopup="menu"
                />
              </UTooltip>
            </UDropdownMenu>
          </div>
        </div>

        <div
          v-if="!modelValue.length"
          class="bonus-item-table__empty"
          role="row"
        >
          <span role="cell">
            No Bonus Items added. Use the + button to add one.
          </span>
        </div>

        <div
          v-for="(item, index) in modelValue"
          :key="index"
          class="bonus-item-table__row"
          role="row"
        >
          <span class="bonus-item-table__image-cell" role="cell">
            <img
              v-if="getDisplayRow(item).imageUrl"
              :src="getDisplayRow(item).imageUrl"
              :alt="`${getDisplayRow(item).name} image`"
              class="bonus-item-table__image sepia"
              loading="lazy"
            />
          </span>
          <span class="bonus-item-table__name" role="cell">
            {{ getDisplayRow(item).name }}
          </span>
          <span class="bonus-item-table__details" role="cell">
            {{ getDisplayRow(item).details }}
          </span>
          <div class="bonus-item-table__action" role="cell">
            <UTooltip text="Edit Bonus Item">
              <UButton
                type="button"
                icon="i-glyphs-edit-1-bold"
                color="primary"
                variant="ghost"
                size="sm"
                square
                class="cursor-pointer"
                :aria-label="`Edit Bonus Item ${index + 1}`"
                @click="openEditDialog(index)"
              />
            </UTooltip>
            <UTooltip text="Remove Bonus Item">
              <UButton
                type="button"
                icon="i-glyphs-minus-bold"
                color="error"
                variant="ghost"
                size="sm"
                square
                class="cursor-pointer"
                :aria-label="`Remove Bonus Item ${index + 1}`"
                @click="removeBonusItem(index)"
              />
            </UTooltip>
          </div>
        </div>
      </div>
    </div>

    <WaxRelicDialog
      v-model:open="isDialogOpen"
      :edit-item="editItem"
      @submit="submitBonusItem"
    />
  </WizardQuestion>
</template>

<style scoped src="./wizard.css" />

<style scoped>
.bonus-item-section {
  display: grid;
  gap: 1rem;
}

.bonus-item-table {
  overflow: hidden;
  border: 1px solid var(--ui-border-muted);
  border-radius: 0.75rem;
  background: color-mix(in oklab, var(--ui-bg-elevated) 78%, transparent);
}

.bonus-item-table__header,
.bonus-item-table__row {
  display: grid;
  grid-template-columns: 4.5rem minmax(8rem, 0.9fr) minmax(0, 1.4fr) auto;
  align-items: center;
  gap: 1rem;
  padding: 0.75rem 1rem;
}

.bonus-item-table__header {
  color: var(--color-amber-200);
  font-size: 0.75rem;
  font-weight: 800;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  border-bottom: 1px solid
    color-mix(in oklab, var(--color-amber-500) 30%, transparent);
  background: rgba(8, 13, 24, 0.88);
}

.bonus-item-table__row {
  border-bottom: 1px solid var(--ui-border-muted);
}

.bonus-item-table__row:last-child {
  border-bottom: 0;
}

.bonus-item-table__empty {
  padding: 1.25rem 1rem;
  color: var(--ui-text-muted);
  font-size: 0.9rem;
  text-align: center;
}

.bonus-item-table__image-cell {
  display: flex;
  justify-content: center;
}

.bonus-item-table__image {
  width: 3.25rem;
  height: 3.25rem;
  object-fit: contain;
}

.bonus-item-table__name {
  min-width: 0;
  color: var(--ui-text-highlighted);
  font-size: 0.9rem;
  font-weight: 700;
  overflow-wrap: anywhere;
}

.bonus-item-table__details {
  min-width: 0;
  color: var(--ui-text-muted);
  font-size: 0.85rem;
  overflow-wrap: anywhere;
}

.bonus-item-table__action {
  display: flex;
  justify-content: flex-end;
  gap: 0.25rem;
}

@media (max-width: 36rem) {
  .bonus-item-table__header,
  .bonus-item-table__row {
    grid-template-columns: 3.5rem minmax(0, 1fr) auto;
  }

  .bonus-item-table__header span:nth-child(3),
  .bonus-item-table__details {
    display: none;
  }

  .bonus-item-table__image {
    width: 2.75rem;
    height: 2.75rem;
  }
}
</style>
