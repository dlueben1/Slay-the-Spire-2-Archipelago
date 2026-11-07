<script setup lang="ts">
import { computed } from "vue";
import { getFillerBudget } from "../../../wizard/fillerBudget";
import { useWizardAnswers } from "./wizardAnswersContext";

/**
 * Shows how many floor-check filler slots Bonus Items and Sight items may still use.
 * Reads the root answers, so it stays current wherever it is placed.
 */
const answers = useWizardAnswers();
const budget = computed(() => getFillerBudget(answers));
const isOver = computed(() => budget.value.remaining < 0);
</script>

<template>
  <div
    class="filler-budget"
    :class="{ 'filler-budget--over': isOver }"
    role="status"
  >
    <p class="filler-budget__count">
      <template v-if="!budget.floorChecks">
        No filler slots: Floor Checks are off.
      </template>
      <template v-else-if="isOver">
        {{ -budget.remaining }} too many: {{ budget.used }} of
        {{ budget.available }} filler slots used.
      </template>
      <template v-else>
        {{ budget.remaining }} of {{ budget.available }} filler slots free
      </template>
    </p>
    <p class="filler-budget__help">
      Each Bonus Item and Sight item replaces one filler item from Floor Checks.
      Ascension Downs and progressive starting equipment use slots too.
      <template v-if="budget.isWorstCase">
        With random characters, this counts the pick with the fewest slots.
      </template>
    </p>
  </div>
</template>

<style scoped>
.filler-budget {
  padding: 0.6rem 0.85rem;
  border: 1px solid var(--ui-border-muted);
  border-radius: 0.5rem;
  background: color-mix(in oklab, var(--ui-bg-elevated) 78%, transparent);
}

.filler-budget--over {
  border-color: color-mix(in oklab, var(--ui-error) 60%, transparent);
  background: color-mix(in oklab, var(--ui-error) 10%, transparent);
}

.filler-budget__count {
  color: var(--ui-text-highlighted);
  font-size: 0.9rem;
  font-weight: 600;
}

.filler-budget--over .filler-budget__count {
  color: var(--ui-error);
}

.filler-budget__help {
  margin-top: 0.15rem;
  color: var(--ui-text-muted);
  font-size: 0.78rem;
}
</style>
