<script setup lang="ts">
import {
  copyAscensionConfiguration,
  MAX_MODDED_CHARACTERS,
} from "../../wizard/CharacterRoster";
import type { ModdedCharacterAnswers } from "../../wizard/WizardAnswers";
import WizardMarkdownDocument from "./WizardMarkdownDocument.vue";

const props = defineProps<{
  modelValue: ModdedCharacterAnswers[];
}>();

const emit = defineEmits<{
  "update:modelValue": [value: ModdedCharacterAnswers[]];
}>();

/**
 * Adds one blank modded-character row with copied Ascension settings.
 *
 * @returns Nothing; emits a new row when the table has a source row and is below the
 * configured maximum.
 * @remarks The parent mounts this table only after creating its mandatory first row.
 * New rows copy the final existing row so the independent Ascension configuration is
 * never shared by reference between characters.
 */
function addModdedCharacter(): void {
  // Respect the Python world's hard limit before creating another editable row.
  if (props.modelValue.length >= MAX_MODDED_CHARACTERS) {
    return;
  }

  // The mounted table should always have one row; keep corrupted empty input inert.
  const sourceCharacter = props.modelValue.at(-1);

  if (!sourceCharacter) {
    return;
  }

  // Give the new character an independent copy of the last visible Ascension setup.
  const moddedCharacter: ModdedCharacterAnswers = {
    name: "",
    ascensions: copyAscensionConfiguration(sourceCharacter.ascensions),
  };

  // Emit an immutable collection so Character Setup remains the state owner.
  emit("update:modelValue", [...props.modelValue, moddedCharacter]);
}

/**
 * Removes one requested modded-character row without allowing an empty table.
 *
 * @param index - Zero-based row index selected for removal.
 * @returns Nothing; emits the remaining rows unless it would remove the mandatory row.
 */
function removeModdedCharacter(index: number): void {
  // Keep one row available while the Modded Characters portrait remains selected.
  if (props.modelValue.length <= 1) {
    return;
  }

  // Ignore stale control events rather than accidentally altering another row.
  if (index < 0 || index >= props.modelValue.length) {
    return;
  }

  // Rebuild the collection without the chosen row and preserve all other settings.
  const moddedCharacters = props.modelValue.filter(
    (_, characterIndex) => characterIndex !== index,
  );

  // Keep the Character Setup parent as the sole owner of persistent state.
  emit("update:modelValue", moddedCharacters);
}

/**
 * Updates the internal ID stored by one modded character row.
 *
 * @param index - Zero-based table row whose text input changed.
 * @param value - Text-like value emitted by Nuxt UI's input control.
 * @returns Nothing; emits an immutable modded-character collection for text values.
 */
function setModdedCharacterName(index: number, value: unknown): void {
  // Ignore unexpected component values while retaining the last complete text state.
  if (typeof value !== "string") {
    return;
  }

  // Replace only the changed row and preserve its per-character Ascension settings.
  const moddedCharacters = [...props.modelValue];
  const currentCharacter = moddedCharacters[index];

  if (!currentCharacter) {
    return;
  }

  moddedCharacters[index] = {
    ...currentCharacter,
    name: value,
  };

  // Keep the Character Setup parent as the sole owner of persistent state.
  emit("update:modelValue", moddedCharacters);
}
</script>

<template>
  <div class="modded-character-section">
    <div
      class="modded-character-table"
      role="table"
      aria-label="Modded characters"
    >
      <div class="modded-character-table__header" role="row">
        <span role="columnheader">
          <div class="flex flex-row">
            <span>Archipelago Name</span>
            <UTooltip
              :content="{
                align: 'center',
                side: 'bottom',
                sideOffset: 8,
              }"
              :delay-duration="0"
              text="When in-game, items and locations for this character will use this name."
            >
              <UIcon
                name="i-glyphs-question-circle-bold"
                class="size-4 shrink-0"
              />
            </UTooltip>
          </div>
        </span>
        <span role="columnheader">Internal character ID</span>
        <div class="modded-character-table__action" role="columnheader">
          <UTooltip text="Add modded character">
            <UButton
              type="button"
              icon="i-glyphs-plus-bold"
              color="primary"
              variant="soft"
              size="sm"
              square
              class="cursor-pointer"
              :disabled="modelValue.length >= MAX_MODDED_CHARACTERS"
              aria-label="Add modded character"
              @click="addModdedCharacter"
            />
          </UTooltip>
        </div>
      </div>

      <div
        v-for="(character, index) in modelValue"
        :key="index"
        class="modded-character-table__row"
        role="row"
      >
        <span class="modded-character-table__slot" role="cell">
          Custom Character {{ index + 1 }}
        </span>

        <div role="cell">
          <UInput
            :model-value="character.name"
            :placeholder="`Internal ID for modded character ${index + 1}`"
            :aria-label="`Internal ID for modded character ${index + 1}`"
            color="primary"
            variant="outline"
            class="w-full"
            @update:model-value="setModdedCharacterName(index, $event)"
          />
        </div>

        <div class="modded-character-table__action" role="cell">
          <UTooltip
            :text="
              modelValue.length <= 1
                ? 'At least one modded character is required while this section is enabled'
                : 'Remove modded character'
            "
          >
            <UButton
              type="button"
              icon="i-glyphs-minus-bold"
              color="error"
              variant="ghost"
              size="sm"
              square
              class="cursor-pointer"
              :disabled="modelValue.length <= 1"
              :aria-label="`Remove modded character ${index + 1}`"
              @click="removeModdedCharacter(index)"
            />
          </UTooltip>
        </div>
      </div>
    </div>

    <WizardMarkdownDocument
      source="/docs/modded-characters.md"
      fallback-title="Finding a modded character ID"
      startsOpen
    />
  </div>
</template>

<style scoped>
.modded-character-section {
  display: grid;
  gap: 1rem;
}

.modded-character-table {
  overflow: hidden;
  border: 1px solid var(--ui-border-muted);
  border-radius: 0.75rem;
  background: color-mix(in oklab, var(--ui-bg-elevated) 78%, transparent);
}

.modded-character-table__header,
.modded-character-table__row {
  display: grid;
  grid-template-columns: 14rem minmax(0, 1fr) auto;
  align-items: center;
  gap: 1rem;
  padding: 0.75rem 1rem;
}

.modded-character-table__header {
  color: var(--color-amber-200);
  font-size: 0.75rem;
  font-weight: 800;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  border-bottom: 1px solid
    color-mix(in oklab, var(--color-amber-500) 30%, transparent);
  background: rgba(8, 13, 24, 0.88);
}

.modded-character-table__row {
  border-bottom: 1px solid var(--ui-border-muted);
}

.modded-character-table__row:last-child {
  border-bottom: 0;
}

.modded-character-table__slot {
  color: var(--ui-text-highlighted);
  font-size: 0.85rem;
  font-weight: 700;
}

.modded-character-table__action {
  display: flex;
  justify-content: flex-end;
}

@media (max-width: 36rem) {
  .modded-character-table__header,
  .modded-character-table__row {
    grid-template-columns: 1fr;
    gap: 0.45rem;
  }
}
</style>
