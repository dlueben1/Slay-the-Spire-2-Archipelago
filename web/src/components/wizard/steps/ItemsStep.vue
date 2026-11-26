<script setup lang="ts">
import { computed, ref } from "vue";
import { optionCatalog } from "../../../generated/optionCatalog";
import { STARTING_ITEM_MAX_COUNT } from "../../../wizard/compiler/applyItemOptions";
import {
  findSharedItem,
  getItemCharacters,
  getStartingItemChoices,
} from "../../../wizard/ItemNames";
import type {
  ItemAnswers,
  StartingItemAnswer,
} from "../../../wizard/WizardAnswers";
import { getQuestionById, itemsSetupStep } from "../../../wizard/WizardStep";
import WizardQuestion from "../core/WizardQuestion.vue";
import { useWizardAnswers } from "../core/wizardAnswersContext";

/**
 * Items & Hints: Archipelago's starting inventory, starting hints, and Local and
 * Non-local Items. It follows Progression on the combined Multiworld step.
 *
 * Starting items are rows of character, item and count. Hints and placement lists offer
 * item groups; item names a hand-written YAML listed stay selected and show alongside.
 *
 * These are advanced settings most players should leave empty, so the questions stay
 * behind a checkbox. The checkbox only changes what is shown: while any setting has a
 * value, such as after importing a YAML, the questions stay open, so nothing that goes
 * into the YAML is hidden.
 */
const props = defineProps<{
  modelValue: ItemAnswers;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: ItemAnswers];
}>();

const answers = useWizardAnswers();

const hasSettings = computed(
  () =>
    props.modelValue.startingInventory.length > 0 ||
    props.modelValue.startHints.length > 0 ||
    props.modelValue.localItems.length > 0 ||
    props.modelValue.nonLocalItems.length > 0,
);

// Leaving the step closes an empty section again.
const wantsToEdit = ref(false);

const isOpen = computed(() => wantsToEdit.value || hasSettings.value);

function setWantsToEdit(value: unknown): void {
  // Nuxt UI's checkbox can also emit its indeterminate state, which this never uses.
  if (typeof value === "boolean") {
    wantsToEdit.value = value;
  }
}

/**
 * Select value for items no character owns. A select can't hold null or an empty
 * string, and no character is named with angle brackets.
 */
const UNIVERSAL = "<any>";

const startingInventoryQuestion = getQuestionById(
  itemsSetupStep,
  "starting-inventory",
);
const startHintsQuestion = getQuestionById(itemsSetupStep, "start-hints");
const localItemsQuestion = getQuestionById(itemsSetupStep, "local-items");
const nonLocalItemsQuestion = getQuestionById(
  itemsSetupStep,
  "non-local-items",
);

/** Characters whose items the roster can start with. */
const characters = computed(() =>
  getItemCharacters(answers.characters, optionCatalog),
);

const ownerItems = computed(() => [
  { label: "Any character", value: UNIVERSAL },
  ...characters.value.map((character) => ({
    label: character,
    value: character,
  })),
]);

/** Group names a hint or placement list offers; Starting Hints leaves out Everything. */
function groupItems(includeEverything: boolean, selected: string[]): string[] {
  const groups = optionCatalog.items.groups.filter(
    (group) => includeEverything || group !== "Everything",
  );
  // Item names from an imported YAML stay visible so they can be removed.
  return [...groups, ...selected.filter((name) => !groups.includes(name))];
}

/** An item both placement lists cover, which generation would fail on. */
const sharedItem = computed(() =>
  findSharedItem(
    props.modelValue.localItems,
    props.modelValue.nonLocalItems,
    characters.value,
    optionCatalog,
  ),
);

/** Emits the section with some fields replaced. */
function update(changes: Partial<ItemAnswers>): void {
  emit("update:modelValue", { ...props.modelValue, ...changes });
}

/** Item select entries for a row's owner. */
function itemChoices(character: string | null) {
  return getStartingItemChoices(character, optionCatalog).map((item) => ({
    label: item.name,
    value: item.name,
  }));
}

function setRow(index: number, changes: Partial<StartingItemAnswer>): void {
  const startingInventory = props.modelValue.startingInventory.map(
    (row, rowIndex) => (rowIndex === index ? { ...row, ...changes } : row),
  );
  update({ startingInventory });
}

function setOwner(index: number, value: string): void {
  const character = value === UNIVERSAL ? null : value;
  const row = props.modelValue.startingInventory[index];
  if (!row) {
    return;
  }
  // Character and universal items differ, so a changed owner may need a new item.
  const choices = getStartingItemChoices(character, optionCatalog);
  const item = choices.some((choice) => choice.name === row.item)
    ? row.item
    : (choices[0]?.name ?? "");
  setRow(index, { character, item });
}

function setCount(index: number, value: number | null | undefined): void {
  const count = Math.min(
    STARTING_ITEM_MAX_COUNT,
    Math.max(1, Math.round(value ?? 1)),
  );
  setRow(index, { count });
}

function addRow(): void {
  const character = characters.value[0] ?? null;
  const item = getStartingItemChoices(character, optionCatalog)[0]?.name ?? "";
  update({
    startingInventory: [
      ...props.modelValue.startingInventory,
      { character, item, count: 1 },
    ],
  });
}

function removeRow(index: number): void {
  update({
    startingInventory: props.modelValue.startingInventory.filter(
      (_, rowIndex) => rowIndex !== index,
    ),
  });
}

const nonBlockingSelectContent = { bodyLock: false };
</script>

<template>
  <section class="wizard-subsection">
    <div class="wizard-subsection__header">
      <h3>Items &amp; Hints</h3>
      <p>
        Choose what you start with, what you know the location of, and which
        items stay in or out of your world.
      </p>
    </div>

    <UCheckbox
      :model-value="isOpen"
      :disabled="hasSettings"
      label="Edit advanced item settings"
      :description="
        hasSettings
          ? 'Some of these settings are in use. Clear them to hide this section again.'
          : 'Most players should leave these empty. They are Archipelago\'s own settings, not part of the Slay the Spire options, and they can make a run too easy or make generation fail.'
      "
      color="warning"
      variant="card"
      class="items-opt-in cursor-pointer"
      :class="{ 'mb-8': isOpen }"
      :ui="{
        label: 'cursor-pointer',
        description: 'cursor-pointer',
      }"
      @update:model-value="setWantsToEdit"
    />

    <div v-if="isOpen" class="space-y-8">
      <WizardQuestion :question="startingInventoryQuestion">
        <div
          class="starting-items"
          role="table"
          aria-label="Starting inventory"
        >
          <div class="starting-items__header" role="row">
            <span role="columnheader">Character</span>
            <span role="columnheader">Item</span>
            <span role="columnheader">Count</span>
            <div class="starting-items__action" role="columnheader">
              <UTooltip text="Add starting item">
                <UButton
                  type="button"
                  icon="i-glyphs-plus-bold"
                  color="primary"
                  variant="soft"
                  size="sm"
                  square
                  class="cursor-pointer"
                  aria-label="Add starting item"
                  @click="addRow"
                />
              </UTooltip>
            </div>
          </div>

          <p
            v-if="!modelValue.startingInventory.length"
            class="starting-items__empty"
          >
            You start with nothing extra.
          </p>

          <div
            v-for="(row, index) in modelValue.startingInventory"
            :key="index"
            class="starting-items__row"
            role="row"
          >
            <div role="cell">
              <USelect
                :model-value="row.character ?? UNIVERSAL"
                :items="ownerItems"
                :content="nonBlockingSelectContent"
                value-key="value"
                label-key="label"
                color="primary"
                variant="outline"
                class="w-full cursor-pointer"
                :aria-label="`Character for starting item ${index + 1}`"
                @update:model-value="setOwner(index, String($event))"
              />
            </div>
            <div role="cell">
              <USelect
                :model-value="row.item"
                :items="itemChoices(row.character)"
                :content="nonBlockingSelectContent"
                value-key="value"
                label-key="label"
                color="primary"
                variant="outline"
                class="w-full cursor-pointer"
                :aria-label="`Starting item ${index + 1}`"
                @update:model-value="setRow(index, { item: String($event) })"
              />
            </div>
            <div role="cell">
              <UInputNumber
                :model-value="row.count"
                :min="1"
                :max="STARTING_ITEM_MAX_COUNT"
                color="primary"
                variant="outline"
                class="wizard-number-input"
                :aria-label="`Count of starting item ${index + 1}`"
                @update:model-value="setCount(index, $event)"
              />
            </div>
            <div class="starting-items__action" role="cell">
              <UTooltip text="Remove starting item">
                <UButton
                  type="button"
                  icon="i-glyphs-minus-bold"
                  color="error"
                  variant="ghost"
                  size="sm"
                  square
                  class="cursor-pointer"
                  :aria-label="`Remove starting item ${index + 1}`"
                  @click="removeRow(index)"
                />
              </UTooltip>
            </div>
          </div>
        </div>
      </WizardQuestion>

      <WizardQuestion :question="startHintsQuestion">
        <USelectMenu
          :model-value="modelValue.startHints"
          :items="groupItems(false, modelValue.startHints)"
          :content="nonBlockingSelectContent"
          multiple
          placeholder="No starting hints"
          color="primary"
          variant="outline"
          class="w-full cursor-pointer"
          aria-label="Starting hints"
          @update:model-value="update({ startHints: [...$event] })"
        />
      </WizardQuestion>

      <WizardQuestion :question="localItemsQuestion">
        <USelectMenu
          :model-value="modelValue.localItems"
          :items="groupItems(true, modelValue.localItems)"
          :content="nonBlockingSelectContent"
          multiple
          placeholder="Anywhere"
          color="primary"
          variant="outline"
          class="w-full cursor-pointer"
          aria-label="Local items"
          @update:model-value="update({ localItems: [...$event] })"
        />
      </WizardQuestion>

      <WizardQuestion :question="nonLocalItemsQuestion">
        <USelectMenu
          :model-value="modelValue.nonLocalItems"
          :items="groupItems(true, modelValue.nonLocalItems)"
          :content="nonBlockingSelectContent"
          multiple
          placeholder="Anywhere"
          color="primary"
          variant="outline"
          class="w-full cursor-pointer"
          aria-label="Non-local items"
          @update:model-value="update({ nonLocalItems: [...$event] })"
        />
        <UAlert
          v-if="sharedItem"
          class="mt-4"
          color="error"
          variant="subtle"
          icon="i-glyphs-exclamation-circle-bold"
          title="An item is in both lists"
          :description="`'${sharedItem}' is in Local Items and Non-local Items, so it could go nowhere and generation would fail. Remove it from one of them.`"
          :ui="{ title: 'font-bold' }"
        />
      </WizardQuestion>
    </div>
  </section>
</template>

<style scoped src="../core/wizard.css"></style>

<style scoped>
.items-opt-in {
  border-color: var(--color-amber-500);
}

.starting-items {
  overflow: hidden;
  border: 1px solid var(--ui-border-muted);
  border-radius: 0.75rem;
  background: color-mix(in oklab, var(--ui-bg-elevated) 78%, transparent);
}

.starting-items__header,
.starting-items__row {
  display: grid;
  grid-template-columns: minmax(0, 12rem) minmax(0, 1fr) 8rem auto;
  align-items: center;
  gap: 1rem;
  padding: 0.75rem 1rem;
}

.starting-items__header {
  color: var(--color-amber-200);
  font-size: 0.75rem;
  font-weight: 800;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  border-bottom: 1px solid
    color-mix(in oklab, var(--color-amber-500) 30%, transparent);
  background: rgba(8, 13, 24, 0.88);
}

.starting-items__row {
  border-bottom: 1px solid var(--ui-border-muted);
}

.starting-items__row:last-child {
  border-bottom: 0;
}

.starting-items__empty {
  padding: 0.75rem 1rem;
  color: var(--ui-text-muted);
  font-size: 0.875rem;
}

.starting-items__action {
  display: flex;
  justify-content: flex-end;
}

@media (max-width: 36rem) {
  /* Column labels don't fit; the header keeps only its add button. */
  .starting-items__header {
    grid-template-columns: 1fr;
  }

  .starting-items__header > span {
    display: none;
  }

  .starting-items__row {
    grid-template-columns: 1fr 1fr;
    gap: 0.5rem;
  }
}
</style>
