<script setup lang="ts">
import type { RadioGroupItem } from "@nuxt/ui";
import { computed, ref, watch } from "vue";
import {
  filterRelicsByName,
  getBonusRelicPoolOptions,
  getEligibleSpecificRelics,
} from "../../wizard/BonusRelicData";
import type {
  BonusItemAnswer,
  RandomWaxRelicBonusItem,
  SpecificWaxRelicBonusItem,
} from "../../wizard/WizardAnswers";

type WaxRelicMode = "specific" | "random";

/** Draft state for the dialog; committed to wizard answers only on Add/Save. */
interface WaxRelicDraft {
  mode: WaxRelicMode;
  relicId: string | null;
  pools: string[];
}

const props = defineProps<{
  open: boolean;
  /** The existing item when editing, or `null` when adding a new one. */
  editItem: BonusItemAnswer | null;
}>();

const emit = defineEmits<{
  "update:open": [value: boolean];
  /** Emitted with the completed item when the player confirms. */
  submit: [value: BonusItemAnswer];
}>();

const modeItems: RadioGroupItem[] = [
  {
    label: "Specific Relic",
    description: "Always grants one relic of your choice.",
    value: "specific",
  },
  {
    label: "Random Relic",
    description: "Randomized each run from the pools you select.",
    value: "random",
  },
];

const eligibleRelics = getEligibleSpecificRelics();
const poolOptions = getBonusRelicPoolOptions();

const draft = ref<WaxRelicDraft>({
  mode: "specific",
  relicId: null,
  pools: [],
});
const searchQuery = ref("");

/** Relics matching the current case-insensitive name filter. */
const filteredRelics = computed(() =>
  filterRelicsByName(eligibleRelics, searchQuery.value),
);

/** Whether the draft in the active mode is complete enough to submit. */
const isSubmittable = computed(() =>
  draft.value.mode === "specific"
    ? draft.value.relicId !== null
    : draft.value.pools.length > 0,
);

const dialogTitle = computed(() =>
  props.editItem ? "Edit Wax Relic" : "Add Wax Relic",
);

const submitLabel = computed(() => (props.editItem ? "Save" : "Add"));

/**
 * Resets the draft from the edited item (or defaults) each time the dialog opens.
 *
 * @remarks Keyed off `open` so reopening after a cancelled edit discards leftovers,
 * and switching between Add and Edit re-initializes without sharing references.
 */
watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) {
      return;
    }

    searchQuery.value = "";

    const item = props.editItem;
    if (item && item.kind === "WAX_RELIC" && item.mode === "specific") {
      draft.value = { mode: "specific", relicId: item.relicId, pools: [] };
    } else if (item && item.kind === "WAX_RELIC" && item.mode === "random") {
      draft.value = { mode: "random", relicId: null, pools: [...item.pools] };
    } else {
      draft.value = { mode: "specific", relicId: null, pools: [] };
    }
  },
);

/** Narrows and applies the reward-mode radio selection. */
function setMode(value: unknown): void {
  if (value === "specific" || value === "random") {
    draft.value = { ...draft.value, mode: value };
  }
}

/** Selects exactly one relic in Specific mode. */
function selectRelic(relicId: string): void {
  draft.value = { ...draft.value, relicId };
}

/** Toggles one pool checkbox in Random mode, preserving canonical option order. */
function togglePool(poolName: string, checked: unknown): void {
  if (typeof checked !== "boolean") {
    return;
  }

  const selected = new Set(draft.value.pools);
  if (checked) {
    selected.add(poolName);
  } else {
    selected.delete(poolName);
  }

  // Emit pools in the source-defined order so YAML output is deterministic.
  const ordered = poolOptions
    .map((option) => option.name)
    .filter((name) => selected.has(name));
  draft.value = { ...draft.value, pools: ordered };
}

/** Closes the dialog without committing the draft. */
function cancel(): void {
  emit("update:open", false);
}

/** Commits the draft as a semantic Bonus Item answer and closes the dialog. */
function submit(): void {
  if (!isSubmittable.value) {
    return;
  }

  let item: BonusItemAnswer;
  if (draft.value.mode === "specific") {
    const specific: SpecificWaxRelicBonusItem = {
      kind: "WAX_RELIC",
      mode: "specific",
      relicId: draft.value.relicId!,
    };
    item = specific;
  } else {
    const random: RandomWaxRelicBonusItem = {
      kind: "WAX_RELIC",
      mode: "random",
      pools: [...draft.value.pools],
    };
    item = random;
  }

  emit("submit", item);
  emit("update:open", false);
}
</script>

<template>
  <UModal
    :open="open"
    :title="dialogTitle"
    :description="'Wax Relics are pulled from the normal relic pool and melt away after enough combats.'"
    scrollable
    :ui="{ content: 'sm:max-w-2xl', footer: 'justify-end' }"
    @update:open="emit('update:open', $event)"
  >
    <template #body>
      <div class="wax-dialog">
        <URadioGroup
          :model-value="draft.mode"
          :items="modeItems"
          value-key="value"
          label-key="label"
          description-key="description"
          color="primary"
          variant="table"
          :ui="{ item: 'cursor-pointer' }"
          @update:model-value="setMode"
        />

        <div v-if="draft.mode === 'specific'" class="wax-dialog__specific">
          <UInput
            v-model="searchQuery"
            icon="i-glyphs-search-bold"
            placeholder="Filter by relic name..."
            aria-label="Filter relics by name"
            color="primary"
            variant="outline"
            class="w-full"
          />

          <div
            class="wax-relic-list"
            role="listbox"
            aria-label="Eligible relics"
            aria-multiselectable="false"
          >
            <p v-if="!filteredRelics.length" class="wax-relic-list__empty">
              No relics match this filter.
            </p>

            <button
              v-for="relic in filteredRelics"
              :key="relic.id"
              type="button"
              role="option"
              class="wax-relic-option"
              :class="{
                'wax-relic-option--selected': draft.relicId === relic.id,
              }"
              :aria-selected="draft.relicId === relic.id"
              @click="selectRelic(relic.id)"
            >
              <img
                :src="relic.imageUrl"
                :alt="`${relic.name} image`"
                class="wax-relic-option__image"
                loading="lazy"
              />
              <span class="wax-relic-option__text">
                <span class="wax-relic-option__name">{{ relic.name }}</span>
                <span class="wax-relic-option__description">
                  {{ relic.description }}
                </span>
              </span>
              <UIcon
                v-if="draft.relicId === relic.id"
                name="i-glyphs-check-circle-bold"
                class="wax-relic-option__check"
              />
            </button>
          </div>
        </div>

        <div v-else class="wizard-toggle-grid">
          <UCheckbox
            v-for="option in poolOptions"
            :key="option.name"
            :model-value="draft.pools.includes(option.name)"
            :label="`${option.name} (${option.relicCount})`"
            :description="option.description"
            color="primary"
            variant="card"
            class="cursor-pointer"
            :ui="{ label: 'cursor-pointer', description: 'cursor-pointer' }"
            @update:model-value="togglePool(option.name, $event)"
          />
        </div>

        <p v-if="!isSubmittable" class="wizard-error" role="status">
          {{
            draft.mode === "specific"
              ? "Select a relic to continue."
              : "Select at least one pool to continue."
          }}
        </p>
      </div>
    </template>

    <template #footer>
      <UButton
        label="Cancel"
        color="neutral"
        variant="outline"
        class="cursor-pointer"
        @click="cancel"
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

<style scoped src="./wizard.css" />

<style scoped>
.wax-dialog {
  display: grid;
  gap: 1.25rem;
}

.wax-dialog__specific {
  display: grid;
  gap: 0.75rem;
}

.wax-relic-list {
  display: grid;
  max-height: 22rem;
  overflow-y: auto;
  border: 1px solid var(--ui-border-muted);
  border-radius: 0.6rem;
  background: rgba(0, 0, 0, 0.25);
}

.wax-relic-list__empty {
  padding: 1.5rem 1rem;
  color: var(--ui-text-muted);
  font-size: 0.9rem;
  text-align: center;
}

.wax-relic-option {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: 0.85rem;
  padding: 0.6rem 0.85rem;
  color: var(--ui-text);
  text-align: left;
  border: 0;
  border-bottom: 1px solid var(--ui-border-muted);
  background: transparent;
  cursor: pointer;
}

.wax-relic-option:last-child {
  border-bottom: 0;
}

.wax-relic-option:hover {
  background: color-mix(in oklab, var(--color-amber-500) 7%, transparent);
}

.wax-relic-option--selected {
  background: color-mix(in oklab, var(--color-amber-500) 14%, transparent);
  box-shadow: inset 0 0 0 1px
    color-mix(in oklab, var(--color-amber-500) 60%, transparent);
}

.wax-relic-option__image {
  width: 2.75rem;
  height: 2.75rem;
  object-fit: contain;
}

.wax-relic-option__text {
  display: grid;
  min-width: 0;
  gap: 0.1rem;
}

.wax-relic-option__name {
  color: var(--ui-text-highlighted);
  font-size: 0.9rem;
  font-weight: 700;
}

.wax-relic-option__description {
  color: var(--ui-text-muted);
  font-size: 0.8rem;
  line-height: 1.35;
}

.wax-relic-option__check {
  width: 1.25rem;
  height: 1.25rem;
  color: var(--color-green-400);
}
</style>
