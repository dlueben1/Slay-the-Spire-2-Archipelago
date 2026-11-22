<script setup lang="ts" generic="Group extends string">
import type {
  ModifierTableGroup,
  ModifierTableRow,
} from "../../../wizard/ModifierTableRow";

/**
 * Checklist of run modifiers styled like the Ascension table, used for Run Modifiers
 * and Boons.
 *
 * Rows are listed under their group headings, each with a short badge (such as the act
 * a modifier starts in). The component edits only the list of selected canonical
 * names, which it emits in row order; each step's compiler owns the YAML mapping.
 */
const props = defineProps<{
  modelValue: string[];
  rows: readonly ModifierTableRow<Group>[];
  groups: readonly ModifierTableGroup<Group>[];
  /** Accessible table name. */
  label: string;
  /** Heading of the name column. */
  columnLabel: string;
  /** Width every badge shares, so the names line up; fits `Act 2+` by default. */
  badgeWidth?: string;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: string[]];
}>();

const groupedRows = props.groups.map((group) => ({
  ...group,
  rows: props.rows.filter((row) => row.group === group.id),
}));

/**
 * Turns one row on or off.
 *
 * @param optionName - Canonical name of the changed row.
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; emits the selection in row order for a binary change.
 */
function setRowEnabled(optionName: string, value: unknown): void {
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

  emit(
    "update:modelValue",
    props.rows
      .filter((row) => selected.has(row.optionName))
      .map((row) => row.optionName),
  );
}
</script>

<template>
  <div
    class="modifier-table"
    role="table"
    :aria-label="label"
    :style="{ '--modifier-badge-width': badgeWidth ?? '3.5rem' }"
  >
    <div class="modifier-table__header" role="row">
      <span role="columnheader">{{ columnLabel }}</span>
      <span role="columnheader">Enabled</span>
    </div>

    <template v-for="group in groupedRows" :key="group.id">
      <div class="modifier-table__group" role="row">
        <span role="rowheader">{{ group.title }}</span>
      </div>

      <div
        v-for="row in group.rows"
        :key="row.optionName"
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
            {{ row.badge }}
          </UBadge>

          <div>
            <strong>{{ row.name }}</strong>
            <p>{{ row.effect }}</p>
          </div>
        </div>

        <div class="modifier-table__checkbox" role="cell">
          <UCheckbox
            :model-value="modelValue.includes(row.optionName)"
            :aria-label="`Enable ${row.name}`"
            color="primary"
            class="cursor-pointer"
            @update:model-value="setRowEnabled(row.optionName, $event)"
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
  min-width: var(--modifier-badge-width);
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
