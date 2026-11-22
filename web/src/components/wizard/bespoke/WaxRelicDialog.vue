<script setup lang="ts">
import type { RadioGroupItem } from "@nuxt/ui";
import { computed, ref, watch } from "vue";
import {
  filterRelicsByName,
  getBonusRelicPoolOptions,
  getEligibleSpecificRelics,
} from "../../../wizard/BonusRelicData";
import type {
  BonusItemAnswer,
  RandomRelicBonusItem,
  RelicBonusKind,
  SpecificRelicBonusItem,
} from "../../../wizard/WizardAnswers";
import BonusItemDialogShell from "./BonusItemDialogShell.vue";
import PickerList, { type PickerOption } from "./PickerList.vue";

type WaxRelicMode = "specific" | "random";

/** Draft state for the dialog; committed to wizard answers only on Add/Save. */
interface WaxRelicDraft {
  mode: WaxRelicMode;
  relicId: string | null;
  pools: string[];
}

const props = defineProps<{
  open: boolean;
  /** Wax Relics melt after a few combats; Relics stay for the whole run. */
  kind: RelicBonusKind;
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
    description:
      "Randomized each run from shared relics in the pools you select.",
    value: "random",
  },
];

// Wax Relics can't be relics with a pickup effect, so each kind has its own lists.
const eligibleRelics = computed(() => getEligibleSpecificRelics(props.kind));
const poolOptions = computed(() => getBonusRelicPoolOptions(props.kind));

const draft = ref<WaxRelicDraft>({
  mode: "specific",
  relicId: null,
  pools: [],
});
const searchQuery = ref("");

/** Relics matching the current case-insensitive name filter. */
const filteredRelics = computed<PickerOption[]>(() =>
  filterRelicsByName(eligibleRelics.value, searchQuery.value).map((relic) => ({
    id: relic.id,
    name: relic.name,
    imageUrl: relic.imageUrl,
    imageAlt: `${relic.name} image`,
    description: relic.description,
  })),
);

/** Whether the draft in the active mode is complete enough to submit. */
const isSubmittable = computed(() =>
  draft.value.mode === "specific"
    ? draft.value.relicId !== null
    : draft.value.pools.length > 0,
);

const kindName = computed(() =>
  props.kind === "WAX_RELIC" ? "Wax Relic" : "Relic",
);

const dialogDescription = computed(() =>
  props.kind === "WAX_RELIC"
    ? "Wax Relics are pulled from the normal relic pool and melt away after enough combats. Relics with a pickup effect can't be wax."
    : "Relics are pulled from the normal relic pool and stay for the whole run.",
);

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
    if (item && item.kind === props.kind && item.mode === "specific") {
      draft.value = { mode: "specific", relicId: item.relicId, pools: [] };
    } else if (item && item.kind === props.kind && item.mode === "random") {
      draft.value = { mode: "random", relicId: null, pools: [...item.pools] };
    } else {
      draft.value = { mode: "specific", relicId: null, pools: [] };
    }
  },
  { immediate: true },
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
  const ordered = poolOptions.value
    .map((option) => option.name)
    .filter((name) => selected.has(name));
  draft.value = { ...draft.value, pools: ordered };
}

/** Commits the draft as a semantic Bonus Item answer and closes the dialog. */
function submit(): void {
  if (!isSubmittable.value) {
    return;
  }

  let item: BonusItemAnswer;
  if (draft.value.mode === "specific") {
    const specific: SpecificRelicBonusItem = {
      kind: props.kind,
      mode: "specific",
      relicId: draft.value.relicId!,
    };
    item = specific;
  } else {
    const random: RandomRelicBonusItem = {
      kind: props.kind,
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
  <BonusItemDialogShell
    :open="open"
    :item-name="kindName"
    :description="dialogDescription"
    :editing="editItem !== null"
    :submittable="isSubmittable"
    @update:open="emit('update:open', $event)"
    @submit="submit"
  >
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

        <PickerList
          :options="filteredRelics"
          :selected-id="draft.relicId"
          label="Eligible relics"
          empty-text="No relics match this filter."
          @select="selectRelic"
        />
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
  </BonusItemDialogShell>
</template>

<style scoped src="../core/wizard.css" />

<style scoped>
.wax-dialog {
  display: grid;
  gap: 1.25rem;
}

.wax-dialog__specific {
  display: grid;
  gap: 0.75rem;
}
</style>
