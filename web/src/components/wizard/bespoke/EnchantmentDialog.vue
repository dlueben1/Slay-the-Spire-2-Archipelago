<script setup lang="ts">
import type { RadioGroupItem } from "@nuxt/ui";
import { computed, ref, watch } from "vue";
import {
  BONUS_ENCHANTMENT_TIER_DESCRIPTIONS,
  BONUS_ENCHANTMENT_TIERS,
  getBonusEnchantments,
} from "../../../wizard/BonusEnchantmentData";
import type {
  BonusItemAnswer,
  EnchantmentBonusKind,
} from "../../../wizard/WizardAnswers";

type EnchantmentMode = "specific" | "random";

/** Draft state for the dialog; committed to wizard answers only on Add/Save. */
interface EnchantmentDraft {
  mode: EnchantmentMode;
  enchantment: string | null;
  tiers: string[];
}

const props = defineProps<{
  open: boolean;
  /** Enchant a card of your choice, or a card reward of enchanted cards. */
  kind: EnchantmentBonusKind;
  /** The existing item when editing, or `null` when adding a new one. */
  editItem: BonusItemAnswer | null;
}>();

const emit = defineEmits<{
  "update:open": [value: boolean];
  /** Emitted with the completed item when the player confirms. */
  submit: [value: BonusItemAnswer];
}>();

const modeItems = computed<RadioGroupItem[]>(() => [
  {
    label: "Specific Enchantment",
    description:
      props.kind === "ENCHANTMENT"
        ? "Always gives the enchantment you choose."
        : "Every card in the reward has the enchantment you choose.",
    value: "specific",
  },
  {
    label: "Random Enchantment",
    description:
      props.kind === "ENCHANTMENT"
        ? "Rolled each run from the tiers you select."
        : "Each card gets a different enchantment from the tiers you select.",
    value: "random",
  },
]);

const enchantments = getBonusEnchantments();
const tierOptions = BONUS_ENCHANTMENT_TIERS.map((tier) => ({
  name: tier,
  description: BONUS_ENCHANTMENT_TIER_DESCRIPTIONS[tier],
  count: enchantments.filter((entry) => entry.tier === tier).length,
}));

const draft = ref<EnchantmentDraft>({
  mode: "random",
  enchantment: null,
  tiers: [],
});

const isSubmittable = computed(() =>
  draft.value.mode === "specific"
    ? draft.value.enchantment !== null
    : draft.value.tiers.length > 0,
);

const kindName = computed(() =>
  props.kind === "ENCHANTMENT" ? "Enchantment" : "Enchanted Cards",
);

const dialogTitle = computed(
  () => `${props.editItem ? "Edit" : "Add"} ${kindName.value}`,
);

const dialogDescription = computed(() =>
  props.kind === "ENCHANTMENT"
    ? "When claimed, choose a card in your deck that has no enchantment yet and enchant it."
    : "A card reward of three cards from your character's pool, each already enchanted.",
);

const submitLabel = computed(() => (props.editItem ? "Save" : "Add"));

/** Resets the draft from the edited item (or defaults) each time the dialog opens. */
watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) {
      return;
    }

    const item = props.editItem;
    if (item && item.kind === props.kind && item.mode === "specific") {
      draft.value = {
        mode: "specific",
        enchantment: item.enchantment,
        tiers: [],
      };
    } else if (item && item.kind === props.kind && item.mode === "random") {
      draft.value = { mode: "random", enchantment: null, tiers: [...item.tiers] };
    } else {
      draft.value = { mode: "random", enchantment: null, tiers: ["Common"] };
    }
  },
  { immediate: true }
);

function setMode(value: unknown): void {
  if (value === "specific" || value === "random") {
    draft.value = { ...draft.value, mode: value };
  }
}

function selectEnchantment(name: string): void {
  draft.value = { ...draft.value, enchantment: name };
}

/** Toggles one tier, keeping Common, Uncommon, Rare order so the YAML is deterministic. */
function toggleTier(tier: string, checked: unknown): void {
  if (typeof checked !== "boolean") {
    return;
  }
  const selected = new Set(draft.value.tiers);
  if (checked) {
    selected.add(tier);
  } else {
    selected.delete(tier);
  }
  draft.value = {
    ...draft.value,
    tiers: BONUS_ENCHANTMENT_TIERS.filter((name) => selected.has(name)),
  };
}

function cancel(): void {
  emit("update:open", false);
}

function submit(): void {
  if (!isSubmittable.value) {
    return;
  }

  const item: BonusItemAnswer =
    draft.value.mode === "specific"
      ? {
          kind: props.kind,
          mode: "specific",
          enchantment: draft.value.enchantment!,
        }
      : { kind: props.kind, mode: "random", tiers: [...draft.value.tiers] };

  emit("submit", item);
  emit("update:open", false);
}
</script>

<template>
  <UModal
    :open="open"
    :title="dialogTitle"
    :description="dialogDescription"
    scrollable
    :ui="{ content: 'sm:max-w-2xl', footer: 'justify-end' }"
    @update:open="emit('update:open', $event)"
  >
    <template #body>
      <div class="enchant-dialog">
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

        <div
          v-if="draft.mode === 'specific'"
          class="enchant-list"
          role="listbox"
          aria-label="Enchantments"
          aria-multiselectable="false"
        >
          <button
            v-for="entry in enchantments"
            :key="entry.name"
            type="button"
            role="option"
            class="enchant-option"
            :class="{
              'enchant-option--selected': draft.enchantment === entry.name,
            }"
            :aria-selected="draft.enchantment === entry.name"
            @click="selectEnchantment(entry.name)"
          >
            <img
              :src="entry.imageUrl"
              :alt="`${entry.displayName} icon`"
              class="enchant-option__image"
              loading="lazy"
            />
            <span class="enchant-option__text">
              <span class="enchant-option__name">
                {{ entry.displayName }}
                <span class="enchant-option__tier">
                  {{ entry.tier ?? "Only by name" }}
                </span>
              </span>
              <span class="enchant-option__description">
                {{ entry.description }}
              </span>
            </span>
            <UIcon
              v-if="draft.enchantment === entry.name"
              name="i-glyphs-check-circle-bold"
              class="enchant-option__check"
            />
          </button>
        </div>

        <div v-else class="wizard-toggle-grid">
          <UCheckbox
            v-for="option in tierOptions"
            :key="option.name"
            :model-value="draft.tiers.includes(option.name)"
            :label="`${option.name} (${option.count})`"
            :description="option.description"
            color="primary"
            variant="card"
            class="cursor-pointer"
            :ui="{ label: 'cursor-pointer', description: 'cursor-pointer' }"
            @update:model-value="toggleTier(option.name, $event)"
          />
        </div>

        <p v-if="!isSubmittable" class="wizard-error" role="status">
          {{
            draft.mode === "specific"
              ? "Select an enchantment to continue."
              : "Select at least one tier to continue."
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

<style scoped src="../core/wizard.css" />

<style scoped>
.enchant-dialog {
  display: grid;
  gap: 1.25rem;
}

.enchant-list {
  display: grid;
  max-height: 22rem;
  overflow-y: auto;
  border: 1px solid var(--ui-border-muted);
  border-radius: 0.6rem;
  background: rgba(0, 0, 0, 0.25);
}

.enchant-option {
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

.enchant-option:last-child {
  border-bottom: 0;
}

.enchant-option:hover {
  background: color-mix(in oklab, var(--color-amber-500) 7%, transparent);
}

.enchant-option--selected {
  background: color-mix(in oklab, var(--color-amber-500) 14%, transparent);
  box-shadow: inset 0 0 0 1px
    color-mix(in oklab, var(--color-amber-500) 60%, transparent);
}

.enchant-option__image {
  width: 2.5rem;
  height: 2.5rem;
  object-fit: contain;
}

.enchant-option__text {
  display: grid;
  min-width: 0;
  gap: 0.1rem;
}

.enchant-option__name {
  color: var(--ui-text-highlighted);
  font-size: 0.9rem;
  font-weight: 700;
}

.enchant-option__tier {
  margin-left: 0.4rem;
  color: var(--ui-text-dimmed);
  font-size: 0.75rem;
  font-weight: 500;
}

.enchant-option__description {
  color: var(--ui-text-muted);
  font-size: 0.8rem;
  line-height: 1.35;
}

.enchant-option__check {
  width: 1.25rem;
  height: 1.25rem;
  color: var(--color-green-400);
}
</style>
