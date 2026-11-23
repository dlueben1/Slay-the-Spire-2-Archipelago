<script lang="ts">
/** One row of the list. */
export interface PickerOption {
  id: string;
  name: string;
  imageUrl: string;
  imageAlt: string;
  description: string;
  /** Small label shown after the name, such as an enchantment's tier. */
  tag?: string;
}
</script>

<script setup lang="ts">
/**
 * Scrolling single-choice list of pictured options, used to pick a specific relic or
 * enchantment in the Bonus Item dialogs.
 */
withDefaults(
  defineProps<{
    options: readonly PickerOption[];
    selectedId: string | null;
    /** Accessible name of the list. */
    label: string;
    /** Shown inside the list when there are no options. */
    emptyText?: string;
    imageSize?: string;
  }>(),
  { emptyText: undefined, imageSize: "2.75rem" },
);

const emit = defineEmits<{
  select: [id: string];
}>();
</script>

<template>
  <div
    class="picker-list"
    role="listbox"
    :aria-label="label"
    aria-multiselectable="false"
    :style="{ '--picker-image-size': imageSize }"
  >
    <p v-if="emptyText && !options.length" class="picker-list__empty">
      {{ emptyText }}
    </p>

    <button
      v-for="option in options"
      :key="option.id"
      type="button"
      role="option"
      class="picker-option"
      :class="{ 'picker-option--selected': selectedId === option.id }"
      :aria-selected="selectedId === option.id"
      @click="emit('select', option.id)"
    >
      <img
        :src="option.imageUrl"
        :alt="option.imageAlt"
        class="picker-option__image"
        loading="lazy"
      />
      <span class="picker-option__text">
        <span class="picker-option__name">
          {{ option.name }}
          <span v-if="option.tag" class="picker-option__tag">
            {{ option.tag }}
          </span>
        </span>
        <span class="picker-option__description">
          {{ option.description }}
        </span>
      </span>
      <UIcon
        v-if="selectedId === option.id"
        name="i-glyphs-check-circle-bold"
        class="picker-option__check"
      />
    </button>
  </div>
</template>

<style scoped>
.picker-list {
  display: grid;
  max-height: 22rem;
  overflow-y: auto;
  border: 1px solid var(--ui-border-muted);
  border-radius: 0.6rem;
  background: rgba(0, 0, 0, 0.25);
}

.picker-list__empty {
  padding: 1.5rem 1rem;
  color: var(--ui-text-muted);
  font-size: 0.9rem;
  text-align: center;
}

.picker-option {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: 0.85rem;
  padding: 0.6rem 0.85rem;
  color: var(--ui-text);
  text-align: left;
  border: 0;
  border-bottom: 1px solid var(--ui-border-muted);
  background: transparent;
  cursor: pointer;
}

.picker-option:last-child {
  border-bottom: 0;
}

.picker-option:hover {
  background: color-mix(in oklab, var(--color-amber-500) 7%, transparent);
}

.picker-option--selected {
  background: color-mix(in oklab, var(--color-amber-500) 14%, transparent);
  box-shadow: inset 0 0 0 1px
    color-mix(in oklab, var(--color-amber-500) 60%, transparent);
}

.picker-option__image {
  width: var(--picker-image-size);
  height: var(--picker-image-size);
  object-fit: contain;
}

.picker-option__text {
  display: grid;
  min-width: 0;
  gap: 0.1rem;
}

.picker-option__name {
  color: var(--ui-text-highlighted);
  font-size: 0.9rem;
  font-weight: 700;
}

.picker-option__tag {
  margin-left: 0.4rem;
  color: var(--ui-text-dimmed);
  font-size: 0.75rem;
  font-weight: 500;
}

.picker-option__description {
  color: var(--ui-text-muted);
  font-size: 0.8rem;
  line-height: 1.35;
}

.picker-option__check {
  width: 1.25rem;
  height: 1.25rem;
  color: var(--color-green-400);
}
</style>
