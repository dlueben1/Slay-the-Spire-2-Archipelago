<script setup lang="ts">
import type { RadioGroupItem, SelectItem } from "@nuxt/ui";
import { computed, ref, watch } from "vue";
import {
  BONUS_CARD_CHARACTERS,
  BONUS_CARD_OTHER_SOURCES,
  BONUS_CARD_RARITIES,
  BONUS_CARD_SOURCE_DESCRIPTIONS,
  BONUS_CARD_TYPES,
  bonusCardPoolTakesRarity,
  formatBonusCardPool,
  parseBonusCardPool,
  type BonusCardPoolParts,
  type BonusCardRarity,
  type BonusCardSource,
  type BonusCardType,
} from "../../../wizard/BonusCardData";
import type { BonusItemAnswer } from "../../../wizard/WizardAnswers";

type CardsMode = "random" | "specific";

/** Select values for "no filter". Selects can't hold an empty value. */
const OWN_CHARACTER = "own";
const RANDOM_SOURCE = "random";
const ANY = "any";

/** Draft state for the dialog; committed to wizard answers only on Add/Save. */
interface CardsDraft {
  mode: CardsMode;
  card: string;
  pools: BonusCardPoolParts[];
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
    label: "Card Pools",
    description: "Each of the 3 cards comes from one of the pools you list, rolled each run.",
    value: "random",
  },
  {
    label: "Specific Card",
    description: "The reward offers one card you name.",
    value: "specific",
  },
];

const sourceItems: SelectItem[] = [
  { label: "Your character", value: OWN_CHARACTER },
  ...BONUS_CARD_CHARACTERS.map((name) => ({ label: name, value: name })),
  ...BONUS_CARD_OTHER_SOURCES.map((name) => ({
    label: name === "Other" ? "Other character" : name,
    value: name,
  })),
  { label: "Random mix", value: RANDOM_SOURCE },
];
const typeItems: SelectItem[] = [
  { label: "Any type", value: ANY },
  ...BONUS_CARD_TYPES.map((name) => ({ label: name, value: name })),
];

const emptyPool = (): BonusCardPoolParts => ({
  random: false,
  source: null,
  type: null,
  rarity: null,
  upgraded: false,
});

const draft = ref<CardsDraft>({ mode: "random", card: "", pools: [emptyPool()] });

/** Each pool as the YAML writes it, or the reason it can't be used. */
const poolChecks = computed(() =>
  draft.value.pools.map((parts) => {
    // A pool needs at least one word; plain cards of your own character are an ordinary reward.
    const text = formatBonusCardPool(parts);
    const checked = text ? parseBonusCardPool(text) : null;
    return { text, error: checked && "error" in checked ? checked.error : null };
  }),
);

const duplicatePool = computed(() => {
  const texts = poolChecks.value.map((check) => check.text);
  return texts.find((text, index) => text && texts.indexOf(text) !== index) ?? null;
});

const isSubmittable = computed(() =>
  draft.value.mode === "specific"
    ? draft.value.card.trim().length > 0
    : draft.value.pools.length > 0 &&
      poolChecks.value.every((check) => check.text && !check.error) &&
      !duplicatePool.value,
);

const dialogTitle = computed(() => `${props.editItem ? "Edit" : "Add"} Card Reward`);
const submitLabel = computed(() => (props.editItem ? "Save" : "Add"));

watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) {
      return;
    }
    const item = props.editItem;
    if (item?.kind === "CARDS" && item.mode === "specific") {
      draft.value = { mode: "specific", card: item.card, pools: [emptyPool()] };
    } else if (item?.kind === "CARDS" && item.mode === "random") {
      const pools = item.pools
        .map(parseBonusCardPool)
        .filter((parts): parts is BonusCardPoolParts => !("error" in parts));
      draft.value = { mode: "random", card: "", pools: pools.length ? pools : [emptyPool()] };
    } else {
      draft.value = {
        mode: "random",
        card: "",
        pools: [{ ...emptyPool(), rarity: "Rare" }],
      };
    }
  },
  { immediate: true },
);

function setMode(value: unknown): void {
  if (value === "specific" || value === "random") {
    draft.value = { ...draft.value, mode: value };
  }
}

function updatePool(index: number, change: Partial<BonusCardPoolParts>): void {
  const pools = [...draft.value.pools];
  const next = { ...pools[index]!, ...change };
  // Drop parts the new source can't take, so the row never shows a pool the APWorld rejects.
  if (next.random) {
    next.source = null;
    next.type = null;
  }
  if (!bonusCardPoolTakesRarity(next) || (next.source === "Colorless" && next.rarity === "Common")) {
    next.rarity = null;
  }
  pools[index] = next;
  draft.value = { ...draft.value, pools };
}

function sourceValue(parts: BonusCardPoolParts): string {
  return parts.random ? RANDOM_SOURCE : (parts.source ?? OWN_CHARACTER);
}

function setSource(index: number, value: unknown): void {
  if (value === RANDOM_SOURCE) {
    updatePool(index, { random: true });
  } else if (value === OWN_CHARACTER) {
    updatePool(index, { random: false, source: null });
  } else {
    updatePool(index, { random: false, source: value as BonusCardSource });
  }
}

function setType(index: number, value: unknown): void {
  updatePool(index, { type: value === ANY ? null : (value as BonusCardType) });
}

function setRarity(index: number, value: unknown): void {
  updatePool(index, { rarity: value === ANY ? null : (value as BonusCardRarity) });
}

function rarityItems(parts: BonusCardPoolParts): SelectItem[] {
  const rarities = parts.source === "Colorless"
    ? BONUS_CARD_RARITIES.filter((name) => name !== "Common")
    : BONUS_CARD_RARITIES;
  return [
    { label: "Any rarity", value: ANY },
    ...rarities.map((name) => ({ label: name, value: name })),
  ];
}

function sourceDescription(parts: BonusCardPoolParts): string {
  if (parts.random) {
    return "Each card picks its own source: your character, another character, Colorless or Event.";
  }
  return parts.source
    ? BONUS_CARD_SOURCE_DESCRIPTIONS[parts.source]
    : "The character you're playing that run.";
}

function addPool(): void {
  draft.value = { ...draft.value, pools: [...draft.value.pools, emptyPool()] };
}

function removePool(index: number): void {
  draft.value = {
    ...draft.value,
    pools: draft.value.pools.filter((_, poolIndex) => poolIndex !== index),
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
      ? { kind: "CARDS", mode: "specific", card: draft.value.card.trim() }
      : { kind: "CARDS", mode: "random", pools: poolChecks.value.map((check) => check.text) };
  emit("submit", item);
  emit("update:open", false);
}
</script>

<template>
  <UModal
    :open="open"
    :title="dialogTitle"
    description="A card reward: choose 1 of 3 cards, or skip it."
    scrollable
    :ui="{ content: 'sm:max-w-2xl', footer: 'justify-end' }"
    @update:open="emit('update:open', $event)"
  >
    <template #body>
      <div class="cards-dialog">
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

        <div v-if="draft.mode === 'specific'" class="cards-specific">
          <UInput
            :model-value="draft.card"
            placeholder="Card name, such as Apotheosis"
            aria-label="Card name"
            color="primary"
            variant="outline"
            class="w-full"
            @update:model-value="draft = { ...draft, card: String($event) }"
          />
          <p class="cards-hint">
            Use the card's English name. Spaces, capitals and punctuation don't matter.
          </p>
        </div>

        <div v-else class="cards-pools">
          <div
            v-for="(pool, index) in draft.pools"
            :key="index"
            class="cards-pool"
          >
            <div class="cards-pool__fields">
              <USelect
                :model-value="sourceValue(pool)"
                :items="sourceItems"
                value-key="value"
                label-key="label"
                color="primary"
                variant="outline"
                class="cursor-pointer"
                :aria-label="`Source of pool ${index + 1}`"
                @update:model-value="setSource(index, $event)"
              />
              <USelect
                :model-value="pool.type ?? 'any'"
                :items="typeItems"
                :disabled="pool.random"
                value-key="value"
                label-key="label"
                color="primary"
                variant="outline"
                class="cursor-pointer"
                :aria-label="`Card type of pool ${index + 1}`"
                @update:model-value="setType(index, $event)"
              />
              <USelect
                :model-value="pool.rarity ?? 'any'"
                :items="rarityItems(pool)"
                :disabled="!bonusCardPoolTakesRarity(pool)"
                value-key="value"
                label-key="label"
                color="primary"
                variant="outline"
                class="cursor-pointer"
                :aria-label="`Rarity of pool ${index + 1}`"
                @update:model-value="setRarity(index, $event)"
              />
              <UCheckbox
                :model-value="pool.upgraded"
                label="Upgraded"
                color="primary"
                class="cursor-pointer"
                @update:model-value="updatePool(index, { upgraded: $event === true })"
              />
              <UButton
                type="button"
                icon="i-glyphs-minus-bold"
                color="error"
                variant="ghost"
                size="sm"
                square
                class="cursor-pointer"
                :disabled="draft.pools.length === 1"
                :aria-label="`Remove pool ${index + 1}`"
                @click="removePool(index)"
              />
            </div>
            <p class="cards-hint">
              {{ sourceDescription(pool) }}
              <span v-if="poolChecks[index]?.text" class="cards-pool__yaml">
                YAML: {{ poolChecks[index]?.text }}
              </span>
            </p>
          </div>

          <UButton
            type="button"
            label="Add pool"
            icon="i-glyphs-plus-bold"
            color="primary"
            variant="soft"
            size="sm"
            class="cursor-pointer justify-self-start"
            @click="addPool"
          />
        </div>

        <p v-if="!isSubmittable" class="wizard-error" role="status">
          {{
            draft.mode === "specific"
              ? "Name a card to continue."
              : duplicatePool
                ? `The pool '${duplicatePool}' is listed twice.`
                : "Give every pool at least a source, type, rarity or Upgraded."
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
.cards-dialog,
.cards-pools,
.cards-specific {
  display: grid;
  gap: 1rem;
}

.cards-pool {
  display: grid;
  gap: 0.35rem;
  padding: 0.75rem;
  border: 1px solid var(--ui-border-muted);
  border-radius: 0.6rem;
  background: rgba(0, 0, 0, 0.2);
}

.cards-pool__fields {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr)) auto auto;
  align-items: center;
  gap: 0.5rem;
}

.cards-hint {
  color: var(--ui-text-muted);
  font-size: 0.8rem;
  line-height: 1.35;
}

.cards-pool__yaml {
  margin-left: 0.4rem;
  color: var(--ui-text-dimmed);
}

@media (max-width: 36rem) {
  .cards-pool__fields {
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  }
}
</style>
