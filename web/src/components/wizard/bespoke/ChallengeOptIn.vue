<script setup lang="ts">
import { computed, ref } from "vue";
import {
  CHALLENGE_OPT_IN_BANNERS,
  tickChallengeOptInBanner,
} from "../../../wizard/challengeOptIn";

/**
 * The Challenges switch: three banners from `CHALLENGE_OPT_IN_BANNERS`, each shown
 * below the last once that one is ticked. Ticking all three turns challenges on, and
 * unticking any of them turns challenges off.
 */
const props = defineProps<{
  modelValue: boolean;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: boolean];
}>();

// Leaving the step forgets unfinished confirmations, so the player starts over.
const partlyTicked = ref(0);

const ticked = computed(() =>
  props.modelValue ? CHALLENGE_OPT_IN_BANNERS.length : partlyTicked.value,
);

// Every ticked banner stays on screen, plus the next one to tick.
const visibleBanners = computed(() =>
  CHALLENGE_OPT_IN_BANNERS.slice(0, ticked.value + 1),
);

/**
 * Handles a tick or untick on one banner.
 *
 * @param index - Position of the changed banner.
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; emits only when challenges turn on or off.
 */
function setTicked(index: number, value: unknown): void {
  // Binary answers never store Nuxt UI's optional indeterminate state.
  if (typeof value !== "boolean") {
    return;
  }

  const next = tickChallengeOptInBanner(ticked.value, index, value);
  partlyTicked.value = next.enabled ? 0 : next.ticked;

  if (next.enabled !== props.modelValue) {
    emit("update:modelValue", next.enabled);
  }
}
</script>

<template>
  <div class="space-y-6">
    <UCheckbox
      v-for="(banner, index) in visibleBanners"
      :key="banner.label"
      :model-value="index < ticked"
      :label="banner.label"
      :description="banner.description"
      :color="banner.color"
      variant="card"
      class="cursor-pointer"
      :class="`challenge-opt-in--${banner.color}`"
      :ui="{
        label: 'cursor-pointer',
        description: 'cursor-pointer',
      }"
      @update:model-value="setTicked(index, $event)"
    />
  </div>
</template>

<style scoped>
.challenge-opt-in--warning {
  border-color: var(--color-amber-500);
}

.challenge-opt-in--error {
  border-color: var(--color-red-500);
}
</style>
