<script setup lang="ts">
import {
  getOrderedRunModifiers,
  RUN_MODIFIER_GROUPS,
  RUN_MODIFIERS,
} from "../../../wizard/RunModifier";

/**
 * Run Modifier checklist styled like the Ascension table.
 *
 * Rows come from `RUN_MODIFIERS`, grouped under `RUN_MODIFIER_GROUPS`, with a badge for
 * the act in which each modifier starts. The component edits only the ordered list of
 * selected canonical names; the Challenges compiler owns the YAML mapping.
 */
const props = defineProps<{
  modelValue: string[];
}>();

const emit = defineEmits<{
  "update:modelValue": [value: string[]];
}>();

const groups = RUN_MODIFIER_GROUPS.map((group) => ({
  ...group,
  modifiers: RUN_MODIFIERS.filter((modifier) => modifier.group === group.id),
}));

/**
 * Turns one Run Modifier on or off.
 *
 * @param optionName - Canonical name of the changed row.
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; emits the selection in catalog order for a binary change.
 */
function setModifierEnabled(optionName: string, value: unknown): void {
  // Binary answers never store Nuxt UI's optional indeterminate state.
  if (typeof value !== "boolean") {
    return;
  }

  const selected = new Set(props.modelValue);

  if (value) {
    selected.add(optionName);
  } else {
    selected.delete(optionName);
  }

  emit("update:modelValue", getOrderedRunModifiers(selected));
}
</script>

<template>
  <div class="modifier-table" role="table" aria-label="Run Modifiers">
    <div class="modifier-table__header" role="row">
      <span role="columnheader">Modifier</span>
      <span role="columnheader">Enabled</span>
    </div>

    <template v-for="group in groups" :key="group.id">
      <div class="modifier-table__group" role="row">
        <span role="rowheader">{{ group.title }}</span>
      </div>

      <div
        v-for="modifier in group.modifiers"
        :key="modifier.optionName"
        class="modifier-table__row"
        role="row"
      >
        <div class="modifier-table__modifier" role="cell">
          <UBadge
            color="primary"
            variant="subtle"
            size="sm"
            class="modifier-table__act"
          >
            Act {{ modifier.startsInAct }}+
          </UBadge>

          <div>
            <strong>{{ modifier.name }}</strong>
            <p>{{ modifier.effect }}</p>
          </div>
        </div>

        <div class="modifier-table__checkbox" role="cell">
          <UCheckbox
            :model-value="modelValue.includes(modifier.optionName)"
            :aria-label="`Enable ${modifier.name}`"
            color="primary"
            class="cursor-pointer"
            @update:model-value="
              setModifierEnabled(modifier.optionName, $event)
            "
          />
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.modifier-table {
  overflow: hidden;
  border: 1px solid var(--ui-border-muted);
  border-radius: 0.75rem;
  background: color-mix(in oklab, var(--ui-bg-elevated) 78%, transparent);
}

.modifier-table__header,
.modifier-table__row {
  display: grid;
  grid-template-columns: minmax(14rem, 1fr) 5.5rem;
  align-items: center;
}

.modifier-table__header {
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

.modifier-table__header span:last-child {
  text-align: center;
}

.modifier-table__group {
  padding: 0.4rem 1rem;
  color: var(--ui-text-muted);
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  border-bottom: 1px solid var(--ui-border-muted);
  background: rgba(8, 13, 24, 0.5);
}

.modifier-table__row {
  min-height: 4.25rem;
  padding: 0.6rem 1rem;
  border-bottom: 1px solid var(--ui-border-muted);
}

.modifier-table__row:last-child {
  border-bottom: 0;
}

.modifier-table__row:hover {
  background: color-mix(in oklab, var(--color-amber-500) 5%, transparent);
}

.modifier-table__modifier {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 0.75rem;
}

.modifier-table__modifier strong {
  color: var(--ui-text-highlighted);
  font-size: 0.9rem;
}

.modifier-table__modifier p {
  margin-top: 0.15rem;
  color: var(--ui-text-muted);
  font-size: 0.78rem;
}

.modifier-table__act {
  min-width: 3.5rem;
  flex: none;
  justify-content: center;
  border-radius: 9999px;
}

.modifier-table__checkbox {
  display: flex;
  justify-content: center;
}

@media (max-width: 42rem) {
  .modifier-table {
    overflow-x: auto;
  }

  .modifier-table__header,
  .modifier-table__row,
  .modifier-table__group {
    min-width: 27rem;
  }
}
</style>
