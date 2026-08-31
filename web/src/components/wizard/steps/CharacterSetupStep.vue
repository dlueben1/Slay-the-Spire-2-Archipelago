<script setup lang="ts">
import type { AccordionItem, SelectItem } from "@nuxt/ui";
import { computed, watch } from "vue";
import { reconcileRosterDependents } from "../../../wizard/characterReconciliation";
import {
  canDeselectBuiltInCharacter,
  copyAscensionConfiguration,
  getConfiguredCharacterNames,
} from "../../../wizard/CharacterRoster";
import type {
  AscensionConfigurationAnswers,
  CharacterAnswers,
  CharacterGoal,
  ModdedCharacterAnswers,
} from "../../../wizard/WizardAnswers";
import {
  characterSetupStep,
  getQuestionById,
  getVisibleQuestionIds,
  type WizardQuestion as WizardQuestionDefinition,
} from "../../../wizard/WizardStep";
import AscensionChecklist from "../bespoke/AscensionChecklist.vue";
import ModdedCharacterTable from "../bespoke/ModdedCharacterTable.vue";
import WizardControl from "../core/WizardControl.vue";
import WizardQuestion from "../core/WizardQuestion.vue";
import { useWizardAnswers } from "../core/wizardAnswersContext";

/**
 * Character Setup step: bespoke roster interfaces around declarative radio groups.
 *
 * The portrait grid, modded-character table, Ascension editors, and the roster-derived
 * selects stay custom because no generic control can express them. The three mode
 * radios render through `WizardControl` from their `characterSetupStep` descriptors,
 * and roster-dependent cleanup lives in `wizard/characterReconciliation.ts`.
 */
interface CharacterAscensionAccordionItem extends AccordionItem {
  kind: "built-in" | "modded";
  characterName: string;
  moddedIndex?: number;
  configuration: AscensionConfigurationAnswers;
}

const props = defineProps<{
  modelValue: CharacterAnswers;
  availableCharacters: string[];
}>();

const emit = defineEmits<{
  "update:modelValue": [value: CharacterAnswers];
}>();

// Flow predicates for nested and conditional questions read the complete model.
const answers = useWizardAnswers();

/** Keeps wizard dropdowns open without temporarily locking and hiding page scrolling. */
const nonBlockingSelectContent = { bodyLock: false };

/** Currently visible question IDs; nested fields and editors follow their questions. */
const visibleQuestionIds = computed(
  () => new Set(getVisibleQuestionIds(characterSetupStep, answers)),
);

/** Resolves one declared question for the template without non-null assertions. */
function question(questionId: string): WizardQuestionDefinition {
  return getQuestionById(characterSetupStep, questionId);
}

/**
 * Builds the public asset path for a schema-provided character's selection portrait.
 *
 * @param character - Generated character display name used by the selection control.
 * @returns The matching character-select WebP path in the public icons directory.
 * @remarks Asset names use lowercase words joined by underscores, while the generated
 * catalog remains responsible for the display names shown to players.
 */
function getCharacterPortraitSource(character: string): string {
  // Normalize catalog display text to the repository's public asset naming convention.
  const assetName = character.toLowerCase().replaceAll(" ", "_");

  // Return a root-relative public path so Vite preserves it for development and builds.
  return `/icons/char_select_${assetName}.webp`;
}

/**
 * Builds the complete named roster used by every shared Character Setup question.
 *
 * @returns Selected built-in names followed by complete modded character IDs.
 */
function getConfiguredRoster(): string[] {
  // Delegate the two-source merge to the semantic helper shared with the compiler.
  return getConfiguredCharacterNames(props.modelValue);
}

const configuredRoster = computed(getConfiguredRoster);

/**
 * Calculates how many characters will exist after optional random selection.
 *
 * @returns The current effective generated-character count.
 */
function getGeneratedCharacterCount(): number {
  // Random mode uses the explicit subset size chosen by the player.
  if (props.modelValue.selectionMode === "random") {
    return props.modelValue.randomCharacterCount;
  }

  // All mode generates every character in the selected pool.
  return configuredRoster.value.length;
}

const generatedCharacterCount = computed(getGeneratedCharacterCount);

/**
 * Builds concrete completion-goal choices for the current generated count.
 * @returns Consecutive one-based counts that can be displayed beside the `all` option.
 */
function getGoalOptions(): number[] {
  // Start empty and add each legal concrete count in ascending order.
  const options: number[] = [];

  for (let count = 1; count <= generatedCharacterCount.value; count += 1) {
    options.push(count);
  }

  // Return a fresh list so the template cannot mutate derived state.
  return options;
}

const goalOptions = computed(getGoalOptions);

/**
 * Builds Nuxt UI select items for the complete built-in and modded roster.
 *
 * @returns One labeled select item for every selected character.
 */
function getStartingCharacterItems(): SelectItem[] {
  // Preserve portrait and modded-table ordering in the starting-character dropdown.
  const items: SelectItem[] = [];

  for (const character of configuredRoster.value) {
    items.push({ label: character, value: character });
  }

  // Return a fresh array for Nuxt UI's controlled select.
  return items;
}

const startingCharacterItems = computed(getStartingCharacterItems);

/**
 * Builds Nuxt UI select items for friendly and concrete completion goals.
 *
 * @returns An `all` item followed by every valid numeric goal count.
 */
function getGoalSelectItems(): SelectItem[] {
  // Keep the player-facing "all" concept first and visually explicit.
  const items: SelectItem[] = [
    {
      label: "All generated characters",
      value: "all",
    },
  ];

  // Nuxt UI values use strings here so the controlled select has one stable type.
  for (const count of goalOptions.value) {
    items.push({ label: String(count), value: String(count) });
  }

  // Return the complete menu model for the goal select.
  return items;
}

const goalSelectItems = computed(getGoalSelectItems);

/**
 * Builds Nuxt UI accordion items for every selected character's advanced settings.
 *
 * @returns Built-in entries followed by modded rows in visible roster order.
 * @remarks Empty modded rows remain visible with a placeholder label so Ascension
 * choices can be configured before the required internal ID is entered.
 */
function getIndividualAscensionItems(): CharacterAscensionAccordionItem[] {
  // Build stable built-in entries from their persistent name-keyed configurations.
  const items: CharacterAscensionAccordionItem[] = [];

  for (const character of props.modelValue.selectedCharacters) {
    const configuration =
      props.modelValue.individualAscensions[character] ??
      props.modelValue.sharedAscensions;

    items.push({
      label: character,
      value: `built-in:${character}`,
      kind: "built-in",
      characterName: character,
      configuration,
    });
  }

  // Modded rows own their configuration so editing a name cannot discard checkboxes.
  for (
    let index = 0;
    index < props.modelValue.moddedCharacters.length;
    index += 1
  ) {
    const moddedCharacter = props.modelValue.moddedCharacters[index]!;
    const characterName = moddedCharacter.name.trim();

    items.push({
      label: characterName || `Modded character ${index + 1} — name required`,
      value: `modded:${index}`,
      kind: "modded",
      characterName,
      moddedIndex: index,
      configuration: moddedCharacter.ascensions,
    });
  }

  return items;
}

const individualAscensionItems = computed(getIndividualAscensionItems);

/**
 * Emits a new Character Setup answer object with selected fields replaced.
 *
 * @param patch - Answer fields changed by one presentation control.
 * @returns Nothing; the updated object is emitted through the component's `v-model`.
 * @remarks Always use this helper instead of mutating nested props in place.
 */
function updateAnswers(patch: Partial<CharacterAnswers>): void {
  // Preserve every answer not owned by the control that initiated the update.
  const nextAnswers = {
    ...props.modelValue,
    ...patch,
  };

  // Keep the parent view as the sole owner of persistent wizard state.
  emit("update:modelValue", nextAnswers);
}

/**
 * Forwards a generically rendered section update to the owning parent view.
 *
 * @param value - Complete immutable section emitted by the control renderer.
 * @returns Nothing; keeps the parent view the sole owner of persistent state.
 */
function forwardUpdate(value: object): void {
  // The renderer edits this step's bound section, so the cast restores its type.
  emit("update:modelValue", value as CharacterAnswers);
}

/**
 * Adds or removes a character in response to a checkbox change.
 *
 * @param character - Schema-provided character represented by the checkbox.
 * @param checked - Whether that character should be selected after the event.
 * @returns Nothing; emits a new selected-character answer through `updateAnswers`.
 */
function toggleCharacter(character: string, checked: boolean): void {
  // Keep at least one character slot, while treating modded rows as valid replacements.
  if (!checked && !canDeselectBuiltInCharacter(props.modelValue, character)) {
    return;
  }

  // Build a new array so Vue and the parent answer model receive an immutable update.
  const selectedCharacters = [...props.modelValue.selectedCharacters];

  if (checked) {
    selectedCharacters.push(character);
  } else {
    const characterIndex = selectedCharacters.indexOf(character);

    if (characterIndex >= 0) {
      selectedCharacters.splice(characterIndex, 1);
    }
  }

  // Persist only the player-facing character selection.
  updateAnswers({ selectedCharacters });
}

/**
 * Toggles the modded-character section represented by the sixth portrait card.
 *
 * @returns Nothing; enabling seeds the required first row from shared Ascension
 * settings, while disabling clears every row maintained by the table.
 * @remarks Additional rows are managed inside `ModdedCharacterTable` after the
 * section is enabled.
 */
function toggleModdedCharacters(): void {
  // Turning the selected portrait off must remove its associated configuration.
  if (props.modelValue.moddedCharacters.length) {
    updateAnswers({ moddedCharacters: [] });
    return;
  }

  // Seed the initially enabled slot from the shared settings for predictable output.
  const moddedCharacter: ModdedCharacterAnswers = {
    name: "",
    ascensions: copyAscensionConfiguration(props.modelValue.sharedAscensions),
  };

  // Persist one new row without mutating the persistent array owned by the parent view.
  updateAnswers({
    moddedCharacters: [moddedCharacter],
  });
}

/**
 * Accepts edited internal IDs from the focused modded-character table.
 *
 * @param moddedCharacters - Complete immutable collection emitted by the table.
 * @returns Nothing; replaces only the modded-character portion of the answer model.
 */
function setModdedCharacters(moddedCharacters: ModdedCharacterAnswers[]): void {
  // Keep internal-name editing separate from generated YAML fields.
  updateAnswers({ moddedCharacters });
}

/**
 * Replaces the Ascension setup shared by every character in standard mode.
 *
 * @param sharedAscensions - Complete enabled and Ascension Down checkbox state.
 * @returns Nothing; emits the updated shared configuration immutably.
 */
function setSharedAscensions(
  sharedAscensions: AscensionConfigurationAnswers,
): void {
  // Preserve per-character settings for players who switch modes and return later.
  updateAnswers({ sharedAscensions });
}

/**
 * Replaces one built-in or modded character's advanced Ascension configuration.
 *
 * @param item - Accordion entry identifying the persistent configuration owner.
 * @param configuration - Complete updated enabled and Ascension Down selections.
 * @returns Nothing; updates only the matching character entry.
 */
function setIndividualAscensions(
  item: CharacterAscensionAccordionItem,
  configuration: AscensionConfigurationAnswers,
): void {
  // Built-in configurations are keyed by generated display name for stable toggling.
  if (item.kind === "built-in") {
    updateAnswers({
      individualAscensions: {
        ...props.modelValue.individualAscensions,
        [item.characterName]: configuration,
      },
    });
    return;
  }

  // Modded rows retain their settings by slot while their internal ID is edited.
  const moddedIndex = item.moddedIndex;

  if (moddedIndex === undefined) {
    return;
  }

  const moddedCharacters = [...props.modelValue.moddedCharacters];
  const moddedCharacter = moddedCharacters[moddedIndex];

  if (!moddedCharacter) {
    return;
  }

  moddedCharacters[moddedIndex] = {
    ...moddedCharacter,
    ascensions: configuration,
  };

  updateAnswers({ moddedCharacters });
}

/**
 * Updates the requested size of a randomly selected character subset.
 *
 * @param value - Numeric value emitted by Nuxt UI's input-number control.
 * @returns Nothing; emits the numeric player-facing answer.
 */
function setRandomCharacterCount(value: number | undefined): void {
  // Ignore a temporarily empty control and retain the last complete answer.
  if (value === undefined) {
    return;
  }

  // Leave range enforcement to reconciliation and compiler validation.
  updateAnswers({ randomCharacterCount: value });
}

/**
 * Updates the fixed character that starts unlocked.
 *
 * @param value - Schema-provided display name emitted by Nuxt UI's select.
 * @returns Nothing; emits the selected starting character.
 */
function setStartingCharacter(value: unknown): void {
  // Nuxt UI may emit an empty value while options change; ignore non-string values.
  if (typeof value !== "string") {
    return;
  }

  // Preserve the player-facing name; the compiler resolves its canonical choice name.
  updateAnswers({ startingCharacter: value });
}

/**
 * Updates the completion goal from the goal select.
 *
 * @param value - `all` or a numeric string emitted by Nuxt UI's select.
 * @returns Nothing; emits the normalized player-facing goal value.
 */
function setGoal(value: unknown): void {
  // Ignore temporarily empty or unexpected select values.
  if (typeof value !== "string") {
    return;
  }

  // Preserve `all` as a friendly concept and normalize concrete options to numbers.
  const goal: CharacterGoal = value === "all" ? "all" : Number(value);

  // Emit the normalized answer without exposing `num_chars_goal` to the component.
  updateAnswers({ goal });
}

/**
 * Returns the unified roster observed by the reconciliation watcher.
 *
 * @returns Current built-in names and complete modded character IDs.
 */
function getRosterForReconciliation(): string[] {
  // Keep the watcher focused on names that can invalidate dependent roster answers.
  return getConfiguredCharacterNames(props.modelValue);
}

/**
 * Applies pure roster reconciliation whenever the observed roster changes.
 *
 * @returns Nothing; emits one patch only when a dependent answer became stale.
 */
function applyRosterReconciliation(): void {
  // The rules live in `characterReconciliation.ts`; this watcher stays a thin caller.
  const patch = reconcileRosterDependents(props.modelValue);

  if (patch) {
    updateAnswers(patch);
  }
}

// Listens for changes to the roster and reconciles dependent answers to avoid stale selections.
watch(getRosterForReconciliation, applyRosterReconciliation, { deep: true });
</script>

<template>
  <div class="space-y-8">
    <!-- Character Selection -->
    <WizardQuestion :question="question('characters')">
      <div class="character-portrait-grid">
        <!-- Vanilla Characters -->
        <UButton
          v-for="character in availableCharacters"
          :key="character"
          type="button"
          color="neutral"
          variant="outline"
          class="character-portrait-button cursor-pointer"
          :class="{
            'character-portrait-button--selected':
              modelValue.selectedCharacters.includes(character),
          }"
          :aria-pressed="modelValue.selectedCharacters.includes(character)"
          :aria-label="`Toggle ${character}`"
          @click="
            toggleCharacter(
              character,
              !modelValue.selectedCharacters.includes(character),
            )
          "
        >
          <img
            :src="getCharacterPortraitSource(character)"
            alt=""
            class="character-portrait-image"
          />

          <span class="character-portrait-label">{{ character }}</span>

          <UIcon
            v-if="modelValue.selectedCharacters.includes(character)"
            name="i-glyphs-check-circle-bold"
            class="character-portrait-check"
          />
        </UButton>

        <!-- Toggle for Modded Characters -->
        <UButton
          type="button"
          color="neutral"
          variant="outline"
          class="character-portrait-button cursor-pointer"
          :class="{
            'character-portrait-button--selected':
              modelValue.moddedCharacters.length > 0,
          }"
          :aria-pressed="modelValue.moddedCharacters.length > 0"
          aria-label="Toggle Modded Characters"
          @click="toggleModdedCharacters"
        >
          <img
            src="/icons/char_select_random.webp"
            alt=""
            class="character-portrait-image"
          />

          <span class="character-portrait-label">Modded Characters</span>

          <UIcon
            v-if="modelValue.moddedCharacters.length"
            name="i-glyphs-check-circle-bold"
            class="character-portrait-check"
          />
        </UButton>
      </div>

      <!-- Validate: Need 1+ Characters -->
      <p v-if="!configuredRoster.length" class="wizard-error">
        Select at least one character.
      </p>
    </WizardQuestion>

    <!-- Modded Character Setup -->
    <WizardQuestion
      v-if="visibleQuestionIds.has('modded-characters')"
      :question="question('modded-characters')"
    >
      <ModdedCharacterTable
        :model-value="modelValue.moddedCharacters"
        @update:model-value="setModdedCharacters"
      />
    </WizardQuestion>

    <!-- Ascension Per Character (Toggles `advanced_characters`) -->
    <WizardQuestion :question="question('ascension-mode')">
      <WizardControl
        :question="question('ascension-mode')"
        :model-value="modelValue"
        @update:model-value="forwardUpdate"
      />
    </WizardQuestion>

    <!-- Ascensions for All -->
    <WizardQuestion
      v-if="visibleQuestionIds.has('shared-ascensions')"
      :question="question('shared-ascensions')"
    >
      <AscensionChecklist
        :model-value="modelValue.sharedAscensions"
        @update:model-value="setSharedAscensions"
      />
    </WizardQuestion>

    <!-- Ascensions for each Character -->
    <WizardQuestion v-else :question="question('individual-ascensions')">
      <UAccordion
        type="multiple"
        :items="individualAscensionItems"
        :unmount-on-hide="false"
        :default-value="
          individualAscensionItems[0]?.value
            ? [individualAscensionItems[0].value]
            : []
        "
        :ui="{
          item: 'border border-default rounded-lg mb-3 overflow-hidden bg-black/15',
          trigger: 'cursor-pointer px-4 font-bold text-highlighted',
          body: 'px-4 pb-4',
        }"
      >
        <template #body="{ item }">
          <AscensionChecklist
            :model-value="item.configuration"
            @update:model-value="setIndividualAscensions(item, $event)"
          />
        </template>
      </UAccordion>

      <p v-if="!individualAscensionItems.length" class="wizard-error">
        Select or name at least one character before configuring Ascensions.
      </p>
    </WizardQuestion>

    <!-- Controls selection mode, whether characters are all included or only a random subset -->
    <WizardQuestion :question="question('selection')">
      <WizardControl
        :question="question('selection')"
        :model-value="modelValue"
        @update:model-value="forwardUpdate"
      />

      <label
        v-if="visibleQuestionIds.has('random-count')"
        class="wizard-nested-field"
      >
        {{ question("random-count").title }}
        <UInputNumber
          :model-value="modelValue.randomCharacterCount"
          color="primary"
          variant="outline"
          :min="1"
          :max="Math.max(1, configuredRoster.length)"
          class="wizard-number-input"
          @update:model-value="setRandomCharacterCount"
        />
      </label>
    </WizardQuestion>

    <!-- Character Unlock Configuration -->
    <WizardQuestion :question="question('availability')">
      <WizardControl
        :question="question('availability')"
        :model-value="modelValue"
        @update:model-value="forwardUpdate"
      />

      <label
        v-if="visibleQuestionIds.has('starting-character')"
        class="wizard-nested-field"
      >
        {{ question("starting-character").title }}
        <USelect
          :model-value="modelValue.startingCharacter ?? undefined"
          :items="startingCharacterItems"
          :content="nonBlockingSelectContent"
          value-key="value"
          label-key="label"
          color="primary"
          variant="outline"
          class="wizard-select-input cursor-pointer min-w-64"
          @update:model-value="setStartingCharacter"
        />
      </label>
    </WizardQuestion>

    <!-- Goal Selection: May move in the future if more goals exist -->
    <WizardQuestion :question="question('goal')">
      <USelect
        :model-value="String(modelValue.goal)"
        :items="goalSelectItems"
        :content="nonBlockingSelectContent"
        value-key="value"
        label-key="label"
        color="primary"
        variant="outline"
        class="wizard-select-input cursor-pointer min-w-64"
        @update:model-value="setGoal"
      />
    </WizardQuestion>
  </div>
</template>

<style scoped src="../core/wizard.css" />
