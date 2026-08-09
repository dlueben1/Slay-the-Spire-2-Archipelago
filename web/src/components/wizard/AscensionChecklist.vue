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
  if (typeof value !== "boolean" || !props.modelValue.enabled.includes(level)) {
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
    downs: getOrderedLevels(downLevels),
  });
}
</script>

<template>
  <div class="ascension-editor">
    <div class="ascension-editor__help">
      <p>
        Every row is independent; leaving every Ascension unchecked is valid.
      </p>
      <p>
        Ascension Down items disable their matching modifier and enter the pool
        only when Floor checks are enabled.
      </p>
    </div>

    <div class="ascension-table" role="table" aria-label="Ascension settings">
      <div class="ascension-table__header" role="row">
        <span role="columnheader">Modifier</span>
        <span role="columnheader">Active</span>
        <span role="columnheader">Shuffle Down</span>
      </div>

      <div
        v-for="modifier in ASCENSION_MODIFIERS"
        :key="modifier.level"
        class="ascension-table__row"
        role="row"
      >
        <div class="ascension-table__modifier" role="cell">
          <UBadge color="primary" variant="subtle"
            >A{{ modifier.level }}</UBadge
          >

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

        <div class="ascension-table__checkbox" role="cell">
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

.ascension-table__checkbox {
  display: flex;
  justify-content: center;
}

@media (max-width: 42rem) {
  .ascension-table {
    overflow-x: auto;
  }

  .ascension-table__header,
  .ascension-table__row {
    min-width: 35rem;
  }
}
</style>
