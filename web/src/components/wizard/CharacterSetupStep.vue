<script setup lang="ts">
import type { RadioGroupItem, SelectItem } from "@nuxt/ui";
import { computed, watch } from "vue";
import type {
  CharacterAnswers,
  CharacterAvailability,
  CharacterGoal,
  CharacterSelectionMode,
} from "../../wizard/WizardAnswers";
import { characterSetupStep } from "../../wizard/WizardStep";
import WizardQuestion from "./WizardQuestion.vue";

const props = defineProps<{
  modelValue: CharacterAnswers;
  availableCharacters: string[];
}>();

const emit = defineEmits<{
  "update:modelValue": [value: CharacterAnswers];
}>();

const selectionModeItems: RadioGroupItem[] = [
  {
    label: "Use all selected characters",
    description:
      "Every portrait selected above will be included in your world.",
    value: "all",
  },
  {
    label: "Randomly select some",
    description:
      "The generator will choose a smaller roster from your selected characters.",
    value: "random",
  },
];

const availabilityItems: RadioGroupItem[] = [
  {
    label: "Start with all characters",
    description: "Your entire generated roster is immediately playable.",
    value: "all",
  },
  {
    label: "Start with one random character",
    description:
      "Begin with a random member of the roster and find unlocks for the rest.",
    value: "random",
  },
  {
    label: "Choose a starting character",
    description:
      "Pick who begins unlocked, then find the remaining character unlocks.",
    value: "fixed",
  },
];

// Resolve question copy by ID so the declarative flow remains the copy source of truth.
const questionTitles: Record<string, string> = {};

for (const question of characterSetupStep.questions) {
  questionTitles[question.id] = question.title;
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
  return props.modelValue.selectedCharacters.length;
}

const generatedCharacterCount = computed(getGeneratedCharacterCount);

/**
 * Builds concrete completion-goal choices for the current generated count.
 *
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
 * Builds Nuxt UI select items for the currently selected starting roster.
 *
 * @returns One labeled select item for every selected character.
 */
function getStartingCharacterItems(): SelectItem[] {
  // Preserve schema ordering so this dropdown matches the portrait grid.
  const items: SelectItem[] = [];

  for (const character of props.modelValue.selectedCharacters) {
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
 * Adds or removes a character in response to a checkbox change.
 *
 * @param character - Schema-provided character represented by the checkbox.
 * @param checked - Whether that character should be selected after the event.
 * @returns Nothing; emits a new selected-character answer through `updateAnswers`.
 */
function toggleCharacter(character: string, checked: boolean): void {
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
 * Updates whether all selected characters or a random subset will be used.
 *
 * @param value - Selection-mode value emitted by Nuxt UI's radio group.
 * @returns Nothing; emits the changed answer when the value is recognized.
 */
function setSelectionMode(value: unknown): void {
  // Ignore values outside the semantic modes declared by this radio group.
  if (value !== "all" && value !== "random") {
    return;
  }

  // Store the semantic mode without writing `pick_num_characters` directly.
  updateAnswers({ selectionMode: value as CharacterSelectionMode });
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
 * Updates the player's chosen character availability mode.
 *
 * @param value - Availability value emitted by Nuxt UI's radio group.
 * @returns Nothing; emits the changed answer when the value is recognized.
 */
function setAvailability(value: unknown): void {
  // Ignore values outside the three semantic modes declared by this radio group.
  if (value !== "all" && value !== "random" && value !== "fixed") {
    return;
  }

  // Store the semantic answer; the compiler later chooses `lock_characters`.
  updateAnswers({ availability: value as CharacterAvailability });
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
 * Returns the selected-character array observed by the reconciliation watcher.
 *
 * @returns The current selected-character answer array.
 */
function getSelectedCharacters(): string[] {
  // Keep the watcher source focused on changes that can invalidate dependent answers.
  return props.modelValue.selectedCharacters;
}

/**
 * Reconciles dependent answers after the selected-character pool changes.
 *
 * @param selectedCharacters - Newly selected schema-provided characters.
 * @returns Nothing; emits one patch only when a dependent answer became stale.
 * @remarks This improves form ergonomics. The compiler still performs authoritative
 * semantic checks and must not rely on this presentation-layer cleanup.
 */
function reconcileDependentAnswers(selectedCharacters: string[]): void {
  // Accumulate every required correction before emitting, avoiding partial UI states.
  const patch: Partial<CharacterAnswers> = {};

  // Clamp random selection when its previous count exceeds the smaller pool.
  if (props.modelValue.randomCharacterCount > selectedCharacters.length) {
    patch.randomCharacterCount = Math.max(1, selectedCharacters.length);
  }

  // Replace a fixed starting character that the player just deselected.
  if (
    props.modelValue.startingCharacter &&
    !selectedCharacters.includes(props.modelValue.startingCharacter)
  ) {
    patch.startingCharacter = selectedCharacters[0] ?? null;
  }

  // Resolve the post-patch generated count before checking a numeric completion goal.
  const reconciledRandomCount =
    patch.randomCharacterCount ?? props.modelValue.randomCharacterCount;
  const reconciledGeneratedCount =
    props.modelValue.selectionMode === "random"
      ? reconciledRandomCount
      : selectedCharacters.length;

  // Fall back to "all" when a previous numeric goal no longer fits the setup.
  if (
    props.modelValue.goal !== "all" &&
    props.modelValue.goal > reconciledGeneratedCount
  ) {
    patch.goal = "all";
  }

  // Avoid emitting when the user's change did not invalidate any dependent answer.
  if (Object.keys(patch).length > 0) {
    updateAnswers(patch);
  }
}

// Keep conditional answers coherent when a player removes a selected character.
watch(getSelectedCharacters, reconcileDependentAnswers, { deep: true });
</script>

<template>
  <div class="space-y-8">
    <!-- Add new top-level questions with this wrapper for shared layout and styling. -->
    <WizardQuestion :title="questionTitles.characters!">
      <template #help>
        Choose one or more. This list comes from the current generated world
        schema.
      </template>

      <div class="character-portrait-grid">
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
      </div>

      <p v-if="!modelValue.selectedCharacters.length" class="wizard-error">
        Select at least one character.
      </p>
    </WizardQuestion>

    <WizardQuestion :title="questionTitles.selection!">
      <URadioGroup
        :model-value="modelValue.selectionMode"
        :items="selectionModeItems"
        value-key="value"
        label-key="label"
        description-key="description"
        color="primary"
        variant="table"
        :ui="{ item: 'cursor-pointer' }"
        @update:model-value="setSelectionMode"
      />

      <label
        v-if="modelValue.selectionMode === 'random'"
        class="wizard-nested-field"
      >
        {{ questionTitles["random-count"] }}
        <UInputNumber
          :model-value="modelValue.randomCharacterCount"
          color="primary"
          variant="outline"
          :min="1"
          :max="Math.max(1, modelValue.selectedCharacters.length)"
          class="wizard-number-input"
          @update:model-value="setRandomCharacterCount"
        />
      </label>
    </WizardQuestion>

    <WizardQuestion :title="questionTitles.availability!">
      <URadioGroup
        :model-value="modelValue.availability"
        :items="availabilityItems"
        value-key="value"
        label-key="label"
        description-key="description"
        color="primary"
        variant="table"
        :ui="{ item: 'cursor-pointer' }"
        @update:model-value="setAvailability"
      />

      <label
        v-if="modelValue.availability === 'fixed'"
        class="wizard-nested-field"
      >
        {{ questionTitles["starting-character"] }}
        <USelect
          :model-value="modelValue.startingCharacter ?? undefined"
          :items="startingCharacterItems"
          value-key="value"
          label-key="label"
          color="primary"
          variant="outline"
          class="wizard-select-input cursor-pointer"
          @update:model-value="setStartingCharacter"
        />
      </label>
    </WizardQuestion>

    <WizardQuestion :title="questionTitles.goal!">
      <USelect
        :model-value="String(modelValue.goal)"
        :items="goalSelectItems"
        value-key="value"
        label-key="label"
        color="primary"
        variant="outline"
        class="wizard-select-input cursor-pointer"
        @update:model-value="setGoal"
      />
    </WizardQuestion>
  </div>
</template>

<style scoped src="./wizard.css"></style>
