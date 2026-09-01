<script setup lang="ts">
import type { WizardQuestion as WizardQuestionDefinition } from "../../../wizard/WizardStep";

/**
 * Shared visual frame for one top-level guided question.
 *
 * New step components should use this wrapper so question spacing, headings, and help
 * text remain consistent. Pass the declarative question definition as `question`, put
 * its control in the default slot, and pass declarative help copy (resolved through
 * `resolveQuestionHelp`) as `helpText`. Reserve the optional `help` slot for extra
 * context that cannot live in the shared definition. Shared styles live in `wizard.css`.
 */
defineProps<{
  question: WizardQuestionDefinition;
  /** Resolved `question.help` copy; kept as a prop so this frame stays presentational. */
  helpText?: string | null;
}>();
</script>

<template>
  <fieldset class="wizard-question">
    <legend
      v-if="question.title"
      class="wizard-question__title"
      :class="{ 'pb-4': !question.description }"
    >
      {{ question.title }}
    </legend>

    <div
      v-if="question.description || helpText || $slots.help"
      class="wizard-question__help"
    >
      <p v-if="question.description">{{ question.description }}</p>
      <p v-if="helpText">{{ helpText }}</p>
      <slot name="help" />
    </div>

    <slot />
  </fieldset>
</template>

<style scoped src="./wizard.css" />
