<script setup lang="ts">
import type { DeathLinkAnswers } from "../../wizard/WizardAnswers";
import type { GeneratedNumberRange } from "../../wizard/WizardOptionKey";
import {
  deathLinkSetupStep,
  type WizardQuestion as WizardQuestionDefinition,
} from "../../wizard/WizardStep";
import WizardQuestion from "./WizardQuestion.vue";

const props = defineProps<{
  modelValue: DeathLinkAnswers;
  damageRange: GeneratedNumberRange;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: DeathLinkAnswers];
}>();

// Keep nonlethal damage inside the 1-100 UX range and generated schema boundary.
const damageMinimum = Math.max(1, props.damageRange.minimum);
const damageMaximum = Math.min(100, props.damageRange.maximum);

// Resolve full question definitions by ID so flow definitions remain the copy source of truth.
const questionsById: Record<string, WizardQuestionDefinition> = {};

for (const question of deathLinkSetupStep.questions) {
  questionsById[question.id] = question;
}

/**
 * Emits a new Death Link answer object with selected fields replaced.
 *
 * @param patch - Death Link answer fields changed by one presentation control.
 * @returns Nothing; emits the complete immutable section answer.
 */
function updateAnswers(patch: Partial<DeathLinkAnswers>): void {
  // Preserve every answer not owned by the control that initiated the update.
  const nextAnswers = {
    ...props.modelValue,
    ...patch,
  };

  // Keep the parent view as the sole owner of persistent wizard state.
  emit("update:modelValue", nextAnswers);
}

/**
 * Updates whether deaths are shared with other Death Link players.
 *
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; ignores the checkbox's unsupported indeterminate state.
 */
function setEnabled(value: unknown): void {
  // This binary answer never stores Nuxt UI's optional indeterminate state.
  if (typeof value !== "boolean") {
    return;
  }

  // Preserve dependent answers while the section is disabled for lossless toggling.
  updateAnswers({ enabled: value });
}

/**
 * Updates whether a received death grants a Death Fragment Curse card.
 *
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; ignores updates while lethal mode disables this control.
 */
function setReceiveFragment(value: unknown): void {
  // Lethal mode is mutually exclusive with both nonlethal received effects.
  if (props.modelValue.beKilled || typeof value !== "boolean") {
    return;
  }

  // Store the semantic Curse-card preference for the Death Link compiler.
  updateAnswers({ receiveFragment: value });
}

/**
 * Updates whether a received death deals configurable maximum-health damage.
 *
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; ignores updates while lethal mode disables this control.
 */
function setReceiveDamage(value: unknown): void {
  // Lethal mode owns the generated damage field and blocks its nonlethal counterpart.
  if (props.modelValue.beKilled || typeof value !== "boolean") {
    return;
  }

  // Preserve the last percentage so toggling damage off and back on is lossless.
  updateAnswers({ receiveDamage: value });
}

/**
 * Updates received Death Link damage as a whole percentage of maximum health.
 *
 * @param value - Scalar or array value emitted by Nuxt UI's generic slider API.
 * @returns Nothing; stores only a supported whole-number scalar.
 */
function setDamagePercent(value: number | number[] | undefined): void {
  // This control uses one thumb and cannot accept arrays or temporary empty state.
  if (
    typeof value !== "number" ||
    !Number.isInteger(value) ||
    value < damageMinimum ||
    value > damageMaximum
  ) {
    return;
  }

  // Store player intent; the compiler maps disabled damage to technical value zero.
  updateAnswers({ damagePercent: value });
}

/**
 * Updates whether an incoming Death Link should kill the player immediately.
 *
 * @param value - Boolean-like value emitted by Nuxt UI's checkbox.
 * @returns Nothing; preserves disabled preferences but makes lethal mode authoritative.
 * @remarks Python represents this semantic choice as 100% maximum-health damage.
 */
function setBeKilled(value: unknown): void {
  // This binary answer never stores Nuxt UI's optional indeterminate state.
  if (typeof value !== "boolean") {
    return;
  }

  // The compiler suppresses the preserved fragment and nonlethal damage preferences.
  updateAnswers({ beKilled: value });
}
</script>

<template>
  <div class="space-y-8">
    <WizardMarkdownDocument
      source="docs/deathlink-faq.md"
      fallback-title="Death Link Help"
    />
    <WizardQuestion :question="questionsById['death-link-enabled']!">
      <UCheckbox
        :model-value="modelValue.enabled"
        label="Enable Death Link"
        description="Your deaths affect other participating players, and their deaths affect you."
        color="primary"
        variant="card"
        class="cursor-pointer"
        :ui="{
          label: 'cursor-pointer',
          description: 'cursor-pointer',
        }"
        @update:model-value="setEnabled"
      />
    </WizardQuestion>

    <template v-if="modelValue.enabled">
      <WizardQuestion :question="questionsById['death-link-effects']!">
        <template #help>
          Select any combination of nonlethal effects, or choose Be killed to
          replace and disable them.
        </template>

        <div class="space-y-3">
          <UCheckbox
            :model-value="modelValue.receiveFragment"
            :disabled="modelValue.beKilled"
            label="Receive a Death Fragment"
            description="Add a Curse card to your run when another linked player dies."
            color="primary"
            variant="card"
            :class="
              modelValue.beKilled ? 'cursor-not-allowed' : 'cursor-pointer'
            "
            :ui="{
              label: modelValue.beKilled
                ? 'cursor-not-allowed'
                : 'cursor-pointer',
              description: modelValue.beKilled
                ? 'cursor-not-allowed'
                : 'cursor-pointer',
            }"
            @update:model-value="setReceiveFragment"
          />

          <UCheckbox
            :model-value="modelValue.receiveDamage"
            :disabled="modelValue.beKilled"
            label="Take Max HP damage"
            description="Lose a configurable percentage of your maximum health."
            color="primary"
            variant="card"
            :class="
              modelValue.beKilled ? 'cursor-not-allowed' : 'cursor-pointer'
            "
            :ui="{
              label: modelValue.beKilled
                ? 'cursor-not-allowed'
                : 'cursor-pointer',
              description: modelValue.beKilled
                ? 'cursor-not-allowed'
                : 'cursor-pointer',
            }"
            @update:model-value="setReceiveDamage"
          />

          <UCheckbox
            :model-value="modelValue.beKilled"
            label="Die"
            description="Die immediately when another linked player dies. (Not Recommended)"
            color="primary"
            variant="card"
            class="cursor-pointer"
            :ui="{
              label: 'cursor-pointer',
              description: 'cursor-pointer',
            }"
            @update:model-value="setBeKilled"
          />
        </div>
      </WizardQuestion>

      <WizardQuestion
        v-if="modelValue.receiveDamage && !modelValue.beKilled"
        :question="questionsById['death-link-damage']!"
      >
        <template #help>
          Choose a whole percentage from 1% through 100%. This damage is not the
          same as the explicit Be killed mode above.
        </template>

        <div class="flex items-center gap-4">
          <USlider
            :model-value="modelValue.damagePercent"
            :min="damageMinimum"
            :max="damageMaximum"
            :step="1"
            :tooltip="true"
            color="primary"
            size="lg"
            class="cursor-pointer"
            aria-label="Received Death Link maximum-health damage percentage"
            @update:model-value="setDamagePercent"
          />

          <output
            class="min-w-16 rounded-md border border-amber-500/30 bg-black/25 px-3 py-1 text-center font-bold text-amber-300"
          >
            {{ modelValue.damagePercent }}%
          </output>
        </div>
      </WizardQuestion>
    </template>
  </div>
</template>

<style scoped src="./wizard.css"></style>
