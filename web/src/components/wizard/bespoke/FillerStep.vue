<script setup lang="ts">
import type { FillerDisplayItem } from "../../../wizard/FillerItem";
import type {
  FillerAnswers,
  FillerItemId,
  FillerWeightLevel,
} from "../../../wizard/WizardAnswers";
import type { WizardQuestion as WizardQuestionDefinition } from "../../../wizard/WizardStep";
import WizardQuestion from "../core/WizardQuestion.vue";

const props = defineProps<{
  modelValue: FillerAnswers;
  items: FillerDisplayItem[];
  question: WizardQuestionDefinition;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: FillerAnswers];
}>();

const FILLER_WEIGHT_LABELS = ["Exempt", "Rare", "Uncommon", "Common"] as const;

/** The four discrete positions rendered on each filler slider. */
const FILLER_WEIGHT_LEVELS = [0, 1, 2, 3] as const;

type FillerBadgeColor = "neutral" | "info" | "warning" | "success";

/**
 * Checks whether a Nuxt UI slider value is a supported filler level.
 *
 * @param value - Slider value to inspect after excluding array and empty states.
 * @returns Whether the value is an integer from zero through three.
 */
function isFillerWeightLevel(value: number): value is FillerWeightLevel {
  // Guard component state even though the slider itself is configured with these bounds.
  return Number.isInteger(value) && value >= 0 && value <= 3;
}

/**
 * Emits an immutable filler answer after one slider changes.
 *
 * @param itemId - Stable player-facing ID for the changed filler item.
 * @param value - Scalar or array value emitted by Nuxt UI's generic slider API.
 * @returns Nothing; emits only when a complete supported scalar level is provided.
 */
function updateWeight(
  itemId: FillerItemId,
  value: number | number[] | undefined,
): void {
  // This step uses one-thumb sliders, so array and temporarily empty values are invalid.
  if (typeof value !== "number" || !isFillerWeightLevel(value)) {
    return;
  }

  // Clone the record so the parent remains the sole owner of reactive answer state.
  const weights = {
    ...props.modelValue.weights,
    [itemId]: value,
  };

  // Emit the semantic 0-3 level without touching generated option keys.
  emit("update:modelValue", { weights });
}

/**
 * Gets the player-facing label for a filler's current weight level.
 *
 * @param itemId - Stable player-facing filler ID whose level should be described.
 * @returns The matching notch label used for accessibility and visible status text.
 */
function getWeightLabel(itemId: FillerItemId): string {
  // Slider levels and labels share the same deliberate zero-based ordering.
  return FILLER_WEIGHT_LABELS[props.modelValue.weights[itemId]];
}

/**
 * Gets the Nuxt UI color associated with a filler's current odds level.
 *
 * @param itemId - Stable player-facing filler ID whose current level is styled.
 * @returns Gray, blue, yellow, or green semantic badge color for levels zero through three.
 */
function getWeightBadgeColor(itemId: FillerItemId): FillerBadgeColor {
  // Keep the visual meaning aligned with the ordered semantic slider levels.
  const colors: readonly FillerBadgeColor[] = [
    "neutral",
    "info",
    "warning",
    "success",
  ];

  // The answer model guarantees this index stays within the four defined colors.
  return colors[props.modelValue.weights[itemId]]!;
}
</script>

<template>
  <WizardQuestion :question="question">
    <template #help>
      Weights are relative to one another. Raising one filler makes it more
      likely than fillers with lower settings; it does not guarantee an exact
      percentage.
    </template>

    <div class="filler-table">
      <div class="filler-table__header">
        <span class="filler-table__buff-heading">Buff</span>
        <span aria-hidden="true" />
        <span class="filler-table__current-heading">Likelihood</span>
      </div>

      <div v-for="item in items" :key="item.id" class="filler-row">
        <div class="filler-row__icon-column">
          <UTooltip :text="`${item.name}: ${item.description}`">
            <div class="filler-row__icon-frame">
              <img
                :src="item.imageSource"
                :alt="`${item.name} icon`"
                class="filler-row__icon ml-0.5"
                :class="{
                  'filler-row__icon--disabled':
                    modelValue.weights[item.id] === 0,
                }"
              />
            </div>
          </UTooltip>

          <span class="filler-row__name">{{ item.name }}</span>
        </div>

        <UTooltip
          :content="{ side: 'bottom', sideOffset: 9 }"
          class="filler-row__slider-tooltip"
        >
          <div class="filler-row__slider-wrap">
            <USlider
              :model-value="modelValue.weights[item.id]"
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
                v-for="level in FILLER_WEIGHT_LEVELS"
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
                v-for="label in FILLER_WEIGHT_LABELS"
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
  </WizardQuestion>
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
