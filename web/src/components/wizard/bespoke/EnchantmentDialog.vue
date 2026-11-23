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
import BonusItemDialogShell from "./BonusItemDialogShell.vue";
import PickerList, { type PickerOption } from "./PickerList.vue";

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
const enchantmentOptions: PickerOption[] = enchantments.map((entry) => ({
  id: entry.name,
  name: entry.displayName,
  imageUrl: entry.imageUrl,
  imageAlt: `${entry.displayName} icon`,
  description: entry.description,
  tag: entry.tier ?? "Only by name",
}));
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

const dialogDescription = computed(() =>
  props.kind === "ENCHANTMENT"
    ? "When claimed, choose a card in your deck that has no enchantment yet and enchant it."
    : "A card reward of three cards from your character's pool, each already enchanted.",
);

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
      draft.value = {
        mode: "random",
        enchantment: null,
        tiers: [...item.tiers],
      };
    } else {
      draft.value = { mode: "random", enchantment: null, tiers: ["Common"] };
    }
  },
  { immediate: true },
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
  <BonusItemDialogShell
    :open="open"
    :item-name="kindName"
    :description="dialogDescription"
    :editing="editItem !== null"
    :submittable="isSubmittable"
    @update:open="emit('update:open', $event)"
    @submit="submit"
  >
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

      <PickerList
        v-if="draft.mode === 'specific'"
        :options="enchantmentOptions"
        :selected-id="draft.enchantment"
        label="Enchantments"
        image-size="2.5rem"
        @select="selectEnchantment"
      />

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
  </BonusItemDialogShell>
</template>

<style scoped src="../core/wizard.css" />

<style scoped>
.enchant-dialog {
  display: grid;
  gap: 1.25rem;
}
</style>
