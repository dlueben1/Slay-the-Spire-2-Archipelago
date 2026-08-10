<script setup lang="ts">
import {
  ASCENSION_MODIFIERS,
  type AscensionLevel,
} from "../../wizard/AscensionModifier";
import type { AscensionConfigurationAnswers } from "../../wizard/WizardAnswers";

const props = defineProps<{
  modelValue: AscensionConfigurationAnswers;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: AscensionConfigurationAnswers];
}>();

/**
 * Orders a set of selected levels for stable state, review, and YAML compilation.
 *
 * @param selectedLevels - Deduplicated Ascension levels from checkbox changes.
 * @returns Selected levels in ascending game order.
 */
function getOrderedLevels(
  selectedLevels: ReadonlySet<AscensionLevel>,
): AscensionLevel[] {
  // Follow the central modifier catalog instead of depending on interaction order.
  const orderedLevels: AscensionLevel[] = [];

  for (const modifier of ASCENSION_MODIFIERS) {
    if (selectedLevels.has(modifier.level)) {
      orderedLevels.push(modifier.level);
    }
  }

  return orderedLevels;
}

/**
 * Builds the ordered A1-through-AN prefix represented by a level button.
 *
 * @param maximumLevel - Highest Ascension that should remain enabled.
 * @returns Every supported level from A1 through the requested maximum.
 */
function getLevelsThrough(maximumLevel: AscensionLevel): AscensionLevel[] {
  // Filter the shared catalog so the result follows canonical game order.
  const levels: AscensionLevel[] = [];

  for (const modifier of ASCENSION_MODIFIERS) {
    if (modifier.level <= maximumLevel) {
      levels.push(modifier.level);
    }
  }

  return levels;
}

/**
 * Shows or hides Ascension Down controls for this configuration.
 *
 * @param value - Boolean-like value emitted by Nuxt UI's card checkbox.
 * @returns Nothing; emits the updated mode and clears Downs when disabling them.
 * @remarks Turning the feature back on starts with no Downs selected, preventing hidden
 * stale choices from unexpectedly re-entering the generated item pool.
 */
function setAscensionDownsEnabled(value: unknown): void {
  // Binary answers never store Nuxt UI's optional indeterminate state.
  if (typeof value !== "boolean") {
    return;
  }

  // Preserve active Ascensions while making the dependent Down state explicit.
  emit("update:modelValue", {
    enabled: [...props.modelValue.enabled],
    ascensionDownsEnabled: value,
    downs: value ? [...props.modelValue.downs] : [],
  });
}

/**
 * Applies the A1-through-AN preset represented by one level button.
 *
 * @param maximumLevel - Clicked badge level that becomes the inclusive upper bound.
 * @returns Nothing; emits the selected prefix and applies it to visible Downs as well.
 */
function setAscensionThreshold(maximumLevel: AscensionLevel): void {
  // A numbered preset enables its complete prefix and disables all later levels.
  const enabled = getLevelsThrough(maximumLevel);
  const downs = props.modelValue.ascensionDownsEnabled ? [...enabled] : [];

  // Keep the two visible columns aligned when Ascension Downs are in use.
  emit("update:modelValue", {
    enabled,
    ascensionDownsEnabled: props.modelValue.ascensionDownsEnabled,
    downs,
  });
}

/**
 * Updates whether one Ascension modifier is active for this configuration.
 *
 * @param level - Ascension row changed by the player.
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; emits an immutable configuration when the value is boolean.
 * @remarks Disabling an Ascension also removes its now-meaningless Ascension Down.
 */
function setAscensionEnabled(level: AscensionLevel, value: unknown): void {
  // Binary answers never store Nuxt UI's optional indeterminate state.
  if (typeof value !== "boolean") {
    return;
  }

  // Clone both selections before applying the row-level relationship.
  const enabledLevels = new Set(props.modelValue.enabled);
  const downLevels = new Set(props.modelValue.downs);

  if (value) {
    enabledLevels.add(level);
  } else {
    enabledLevels.delete(level);
    downLevels.delete(level);
  }

  // Emit canonical order so persistent state does not depend on click sequence.
  emit("update:modelValue", {
    enabled: getOrderedLevels(enabledLevels),
    ascensionDownsEnabled: props.modelValue.ascensionDownsEnabled,
    downs: getOrderedLevels(downLevels),
  });
}

/**
 * Updates whether one enabled Ascension receives a shuffled Ascension Down item.
 *
 * @param level - Ascension row changed by the player.
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; emits an immutable configuration for a valid binary change.
 */
function setAscensionDown(level: AscensionLevel, value: unknown): void {
  // Ignore indeterminate values and disabled rows defensively.
  if (
    typeof value !== "boolean" ||
    !props.modelValue.ascensionDownsEnabled ||
    !props.modelValue.enabled.includes(level)
  ) {
    return;
  }

  // Apply the independent item-pool choice without changing active Ascensions.
  const downLevels = new Set(props.modelValue.downs);

  if (value) {
    downLevels.add(level);
  } else {
    downLevels.delete(level);
  }

  // Preserve the enabled collection while normalizing Down checkbox order.
  emit("update:modelValue", {
    enabled: [...props.modelValue.enabled],
    ascensionDownsEnabled: props.modelValue.ascensionDownsEnabled,
    downs: getOrderedLevels(downLevels),
  });
}
</script>

<template>
  <div class="ascension-editor">
    <div class="ascension-editor__controls">
      <UCheckbox
        :model-value="modelValue.ascensionDownsEnabled"
        label="Enable Ascension Downs"
        description="Adds items to the item pool that disable selected Ascension modifiers when obtained."
        color="primary"
        variant="card"
        class="cursor-pointer"
        :ui="{
          label: 'cursor-pointer',
          description: 'cursor-pointer',
        }"
        @update:model-value="setAscensionDownsEnabled"
      />
    </div>

    <div class="ascension-editor__help">
      <p>
        You can turn on any combination of Ascension Levels, or you can select
        an A-level button on the left-hand side to enable every modifier up to
        that level. Leaving every Ascension unchecked is valid.
      </p>
      <p>
        We recommend enabling Ascension Level 1, since it provides more
        opportunities to fight Elites and earn Relic Checks. If this Ascension
        is off, you may not be able to obtain all in-logic Relics until you've
        beat the game as your character.
      </p>
      <p v-if="modelValue.ascensionDownsEnabled">
        Ascension Down items disable their matching modifier when obtained.
      </p>
    </div>

    <div
      class="ascension-table"
      :class="{
        'ascension-table--without-downs': !modelValue.ascensionDownsEnabled,
      }"
      role="table"
      aria-label="Ascension settings"
    >
      <div class="ascension-table__header" role="row">
        <span role="columnheader">Modifier</span>
        <span role="columnheader">Enabled</span>
        <span v-if="modelValue.ascensionDownsEnabled" role="columnheader">
          Add Item to Disable
        </span>
      </div>

      <div
        v-for="modifier in ASCENSION_MODIFIERS"
        :key="modifier.level"
        class="ascension-table__row"
        role="row"
      >
        <div class="ascension-table__modifier" role="cell">
          <UButton
            type="button"
            color="primary"
            variant="subtle"
            size="xs"
            class="ascension-table__level-button cursor-pointer"
            :aria-label="`Enable every Ascension through A${modifier.level}`"
            @click="setAscensionThreshold(modifier.level)"
          >
            A{{ modifier.level }}
          </UButton>

          <div>
            <strong>{{ modifier.name }}</strong>
            <p>{{ modifier.effect }}</p>
          </div>
        </div>

        <div class="ascension-table__checkbox" role="cell">
          <UCheckbox
            :model-value="modelValue.enabled.includes(modifier.level)"
            :aria-label="`Enable A${modifier.level} ${modifier.name}`"
            color="primary"
            class="cursor-pointer"
            @update:model-value="setAscensionEnabled(modifier.level, $event)"
          />
        </div>

        <div
          v-if="modelValue.ascensionDownsEnabled"
          class="ascension-table__checkbox"
          role="cell"
        >
          <UCheckbox
            :model-value="modelValue.downs.includes(modifier.level)"
            :disabled="!modelValue.enabled.includes(modifier.level)"
            :aria-label="`Shuffle the A${modifier.level} Ascension Down`"
            color="primary"
            class="cursor-pointer"
            @update:model-value="setAscensionDown(modifier.level, $event)"
          />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.ascension-editor {
  display: grid;
  gap: 1rem;
}

.ascension-editor__help {
  color: var(--ui-text-muted);
  font-size: 0.85rem;
}

.ascension-editor__help p + p {
  margin-top: 0.25rem;
}

.ascension-editor__controls {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  align-items: stretch;
  gap: 1rem;
}

.ascension-editor__bulk-actions {
  display: flex;
  padding: 0.75rem;
  align-items: center;
  justify-content: flex-end;
  gap: 0.75rem;
  border: 1px solid var(--ui-border-muted);
  border-radius: 0.5rem;
  background: color-mix(in oklab, var(--ui-bg-elevated) 78%, transparent);
}

.ascension-table {
  overflow: hidden;
  border: 1px solid var(--ui-border-muted);
  border-radius: 0.75rem;
  background: color-mix(in oklab, var(--ui-bg-elevated) 78%, transparent);
}

.ascension-table__header,
.ascension-table__row {
  display: grid;
  grid-template-columns: minmax(14rem, 1fr) 5.5rem 7.5rem;
  align-items: center;
}

.ascension-table--without-downs .ascension-table__header,
.ascension-table--without-downs .ascension-table__row {
  grid-template-columns: minmax(14rem, 1fr) 5.5rem;
}

.ascension-table__header {
  padding: 0.75rem 1rem;
  color: var(--color-amber-200);
  font-size: 0.75rem;
  font-weight: 800;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  border-bottom: 1px solid
    color-mix(in oklab, var(--color-amber-500) 30%, transparent);
  background: rgba(8, 13, 24, 0.88);
}

.ascension-table__header span:not(:first-child) {
  text-align: center;
}

.ascension-table__row {
  min-height: 4.25rem;
  padding: 0.6rem 1rem;
  border-bottom: 1px solid var(--ui-border-muted);
}

.ascension-table__row:last-child {
  border-bottom: 0;
}

.ascension-table__row:hover {
  background: color-mix(in oklab, var(--color-amber-500) 5%, transparent);
}

.ascension-table__modifier {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 0.75rem;
}

.ascension-table__modifier strong {
  color: var(--ui-text-highlighted);
  font-size: 0.9rem;
}

.ascension-table__modifier p {
  margin-top: 0.15rem;
  color: var(--ui-text-muted);
  font-size: 0.78rem;
}

.ascension-table__level-button {
  min-width: 2.15rem;
  justify-content: center;
  border-radius: 9999px;
}

.ascension-table__checkbox {
  display: flex;
  justify-content: center;
}

@media (max-width: 42rem) {
  .ascension-editor__controls {
    grid-template-columns: 1fr;
  }

  .ascension-table {
    overflow-x: auto;
  }

  .ascension-table__header,
  .ascension-table__row {
    min-width: 35rem;
  }

  .ascension-table--without-downs .ascension-table__header,
  .ascension-table--without-downs .ascension-table__row {
    min-width: 27rem;
  }
}
</style>
