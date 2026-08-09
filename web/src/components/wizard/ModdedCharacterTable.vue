<script setup lang="ts">
import type { ModdedCharacterAnswers } from "../../wizard/WizardAnswers";
import WizardMarkdownDocument from "./WizardMarkdownDocument.vue";

const props = defineProps<{
  modelValue: ModdedCharacterAnswers[];
}>();

const emit = defineEmits<{
  "update:modelValue": [value: ModdedCharacterAnswers[]];
}>();

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
        <span role="columnheader">Slot</span>
        <span role="columnheader">Internal character ID</span>
      </div>

      <div
        v-for="(character, index) in modelValue"
        :key="index"
        class="modded-character-table__row"
        role="row"
      >
        <span class="modded-character-table__slot" role="cell">
          Modded {{ index + 1 }}
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
      </div>
    </div>

    <WizardMarkdownDocument
      source="/docs/modded-characters.md"
      fallback-title="Finding a modded character ID"
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
  grid-template-columns: 8rem minmax(0, 1fr);
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

@media (max-width: 36rem) {
  .modded-character-table__header,
  .modded-character-table__row {
    grid-template-columns: 1fr;
    gap: 0.45rem;
  }
}
</style>
