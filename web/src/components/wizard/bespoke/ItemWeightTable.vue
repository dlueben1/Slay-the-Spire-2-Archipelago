<script setup lang="ts" generic="ItemId extends string">
const props = defineProps<{
  heading: string;
  items: readonly {
    id: ItemId;
    name: string;
    description: string;
    imageSource?: string;
  }[];
  weights: Record<ItemId, number>;
}>();
const emit = defineEmits<{
  "update:weight": [id: ItemId, level: number];
}>();

const WEIGHT_LABELS = ["Exempt", "Rare", "Uncommon", "Common"] as const;
const WEIGHT_LEVELS = [0, 1, 2, 3] as const;
const BADGE_COLORS = ["neutral", "info", "warning", "success"] as const;

function updateWeight(id: ItemId, value: number | number[] | undefined): void {
  if (
    typeof value !== "number" ||
    !Number.isInteger(value) ||
    value < 0 ||
    value > 3
  )
    return;
  emit("update:weight", id, value);
}

function getWeightLabel(id: ItemId) {
  return WEIGHT_LABELS[props.weights[id]];
}

function getWeightBadgeColor(id: ItemId) {
  return BADGE_COLORS[props.weights[id]];
}
</script>

<template>
  <div class="filler-table">
    <div class="filler-table__header">
      <span class="filler-table__buff-heading">{{ heading }}</span>
      <span aria-hidden="true" />
      <span class="filler-table__current-heading">Likelihood</span>
    </div>

    <div v-for="item in items" :key="item.id" class="filler-row">
      <div class="filler-row__icon-column">
        <UTooltip :text="`${item.name}: ${item.description}`">
          <div v-if="item.imageSource" class="filler-row__icon-frame">
            <img
              :src="item.imageSource"
              :alt="`${item.name} icon`"
              class="filler-row__icon ml-0.5"
              :class="{
                'filler-row__icon--disabled': weights[item.id] === 0,
              }"
            />
          </div>
        </UTooltip>

        <UTooltip :text="item.description">
          <span class="filler-row__name">{{ item.name }}</span>
        </UTooltip>
      </div>

      <UTooltip
        :content="{ side: 'bottom', sideOffset: 9 }"
        class="filler-row__slider-tooltip"
      >
        <div class="filler-row__slider-wrap">
          <USlider
            :model-value="weights[item.id]"
            :min="0"
            :max="3"
            :step="1"
            color="primary"
            size="lg"
            class="filler-row__slider cursor-pointer"
            :aria-label="`${item.name} weight: ${getWeightLabel(item.id)}`"
            @update:model-value="updateWeight(item.id, $event)"
          />

          <div class="filler-slider-notches" aria-hidden="true">
            <span
              v-for="level in WEIGHT_LEVELS"
              :key="level"
              class="filler-slider-notch"
              :style="{ left: `${(level / 3) * 100}%` }"
            />
          </div>
        </div>

        <template #content>
          <div
            class="filler-slider-labels"
            :aria-label="`${item.name} weight levels`"
          >
            <span
              v-for="label in WEIGHT_LABELS"
              :key="label"
              class="filler-slider-label"
            >
              {{ label }}
            </span>
          </div>
        </template>
      </UTooltip>

      <UBadge
        :color="getWeightBadgeColor(item.id)"
        variant="subtle"
        class="filler-row__value"
      >
        {{ getWeightLabel(item.id) }}
      </UBadge>
    </div>
  </div>
</template>

<style scoped>
.filler-table {
  overflow: hidden;
  border: 1px solid color-mix(in oklab, var(--color-amber-500) 25%, transparent);
  border-radius: 0.75rem;
  background: color-mix(in oklab, var(--ui-bg-elevated) 86%, black);
}

.filler-table__header,
.filler-row {
  display: grid;
  grid-template-columns: 9rem minmax(12rem, 1fr) 8.5rem;
  gap: 1rem;
  align-items: center;
}

.filler-table__header {
  min-height: 3.25rem;
  padding: 0.55rem 0.9rem;
  border-bottom: 1px solid
    color-mix(in oklab, var(--color-amber-500) 25%, transparent);
  background: rgba(0, 0, 0, 0.28);
}

.filler-table__buff-heading,
.filler-table__current-heading {
  color: var(--ui-text-muted);
  font-size: 0.7rem;
  font-weight: 700;
  text-align: center;
}

.filler-row {
  min-height: 4.75rem;
  padding: 0.55rem 0.9rem;
  border-bottom: 1px solid
    color-mix(in oklab, var(--ui-border) 85%, transparent);
}

.filler-row:last-child {
  border-bottom: 0;
}

.filler-row:hover {
  background: color-mix(in oklab, var(--color-amber-500) 5%, transparent);
}

.filler-row__icon-frame {
  width: 3.75rem;
  height: 3.75rem;
}

.filler-row__icon {
  width: 100%;
  height: 100%;
  object-fit: contain;
  transition:
    filter 180ms ease,
    opacity 180ms ease;
}

.filler-row__icon--disabled {
  filter: grayscale(1);
  opacity: 0.42;
}

.filler-row__icon-column {
  display: flex;
  min-width: 0;
  flex-direction: column;
  align-items: center;
  gap: 0.15rem;
}

.filler-row__name {
  max-width: 9rem;
  color: var(--ui-text-muted);
  font-size: 0.65rem;
  font-weight: 700;
  line-height: 1.1;
  text-align: center;
  white-space: normal;
  overflow-wrap: anywhere;
}

.filler-row__slider-wrap {
  position: relative;
  min-width: 0;
}

.filler-row__slider-tooltip {
  min-width: 0;
}

.filler-row__slider {
  width: 100%;
}

.filler-slider-notches {
  position: absolute;
  top: calc(50% + 0.7rem);
  right: 0.5625rem;
  left: 0.5625rem;
  height: 0.3rem;
  pointer-events: none;
}

.filler-slider-notch {
  position: absolute;
  top: 0;
  width: 0.15rem;
  height: 100%;
  border-radius: 9999px;
  background: var(--ui-text-muted);
  opacity: 0.85;
  transform: translateX(-50%);
}

.filler-slider-labels {
  display: grid;
  width: 18rem;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 0.35rem;
  color: var(--ui-text-muted);
  font-size: 0.65rem;
  font-weight: 700;
  line-height: 1.1;
  text-align: center;
}

.filler-slider-label {
  min-width: 0;
}

.filler-row__value {
  justify-self: center;
  min-width: 6.5rem;
  justify-content: center;
}

@media (max-width: 640px) {
  .filler-table {
    overflow-x: auto;
  }

  .filler-table__header,
  .filler-row {
    min-width: 34rem;
  }
}
</style>
