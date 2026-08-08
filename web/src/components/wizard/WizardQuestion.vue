<script setup lang="ts">
/**
 * Shared visual frame for one top-level guided question.
 *
 * New step components should use this wrapper so question spacing, headings, and help
 * text remain consistent. Put the question control in the default slot and optional
 * explanatory copy in the `help` slot. Shared styles live in `wizard.css`.
 */
defineProps<{
  title: string;
}>();
</script>

<template>
  <fieldset class="wizard-question">
    <legend class="wizard-question__title">{{ title }}</legend>

    <div v-if="$slots.help" class="wizard-question__help">
      <slot name="help" />
    </div>

    <slot />
  </fieldset>
</template>

<style scoped src="./wizard.css"></style>
