<script setup lang="ts">
import type { TabsItem } from "@nuxt/ui";
import { computed, reactive, ref } from "vue";
import CharacterSetupStep from "../components/wizard/CharacterSetupStep.vue";
import ReviewStep from "../components/wizard/ReviewStep.vue";
import { optionCatalog } from "../generated/optionCatalog";
import { optionsToYaml } from "../services/YamlService";
import { createDefaultWizardAnswers } from "../wizard/WizardAnswers";
import { compileWizardAnswers } from "../wizard/compiler/compileWizardAnswers";
import { summarizeCharacterAnswers } from "../wizard/review";
import { wizardSteps } from "../wizard/WizardStep";

const availableCharacters = optionCatalog.options.characters?.valid_keys ?? [];
const answers = reactive(createDefaultWizardAnswers(availableCharacters));
const stepIndex = ref(0);
const error = ref("");

const CHARACTER_OPTION_KEYS = [
  "characters",
  "pick_num_characters",
  "num_chars_goal",
  "lock_characters",
  "unlocked_character",
] as const;

/**
 * Compiles the current player-facing answer snapshot for Vue reactivity.
 *
 * @returns A new complete and validated Archipelago option configuration.
 * @throws When current answers cannot be represented by the generated schema.
 * @remarks This function deliberately recompiles from scratch on every dependency change.
 */
function compileCurrentAnswers() {
  // Delegate all option mapping to the pure compiler instead of performing it in Vue.
  return compileWizardAnswers(answers, optionCatalog);
}

const compiled = computed(compileCurrentAnswers);

/**
 * Selects the Character Setup options shown by the first review vertical slice.
 *
 * @returns A record containing only the five canonical character option values.
 * @remarks The full compiled configuration remains available in `compiled`; this subset
 * prevents unrelated default options from overwhelming the initial review screen.
 */
function getCharacterSettings() {
  // Pair each compiler-owned key with its value from the complete compiled snapshot.
  const characterEntries = [];

  for (const key of CHARACTER_OPTION_KEYS) {
    characterEntries.push([key, compiled.value[key]!] as const);
  }

  // Convert ordered key-value pairs into the record expected by the YAML service.
  return Object.fromEntries(characterEntries);
}

const characterSettings = computed(getCharacterSettings);

/**
 * Determines whether current answers can safely open the review step.
 *
 * @returns `true` when compilation and final schema validation both succeed.
 * @remarks Expected invalid form state is converted to `false`; `next` displays details.
 */
function canCompileForReview(): boolean {
  try {
    // Accessing the computed value runs the full compiler and validation pipeline.
    void compiled.value;

    // A successful read means review data can be rendered safely.
    return true;
  } catch {
    // Invalid in-progress answers disable direct review navigation.
    return false;
  }
}

const canReview = computed(canCompileForReview);

/**
 * Builds Nuxt UI tab items for wizard navigation.
 *
 * @returns One pill-tab item per declarative wizard step.
 * @remarks Review remains disabled until the complete compiler pipeline succeeds.
 */
function getStepTabs(): TabsItem[] {
  // Rebuild the items when validation state changes so disabled state stays current.
  const items: TabsItem[] = [];

  for (const step of wizardSteps) {
    items.push({
      label: step.title,
      value: step.id,
      disabled: step.id === "review" && !canReview.value,
    });
  }

  // Return the controlled navigation model consumed by Nuxt UI Tabs.
  return items;
}

const stepTabs = computed(getStepTabs);

/**
 * Returns the stable ID for the active numeric wizard step.
 *
 * @returns The active step ID used as Nuxt UI Tabs' controlled value.
 */
function getActiveStepId(): string {
  // Fall back to Character Setup if navigation state ever exceeds the definition.
  return wizardSteps[stepIndex.value]?.id ?? wizardSteps[0]!.id;
}

const activeStepId = computed(getActiveStepId);

/**
 * Handles a requested navigation change from Nuxt UI Tabs.
 *
 * @param value - Stable step ID emitted by the selected tab.
 * @returns Nothing; updates the numeric step index when the destination is allowed.
 */
function setActiveStep(value: string | number): void {
  // Tabs support numeric values generically, but this wizard uses string IDs.
  if (typeof value !== "string") {
    return;
  }

  // Locate the requested declarative step without coupling IDs to array indexes.
  let requestedIndex = -1;

  for (let index = 0; index < wizardSteps.length; index += 1) {
    if (wizardSteps[index]!.id === value) {
      requestedIndex = index;
      break;
    }
  }

  // Reject unknown IDs and prevent bypassing validation into review.
  if (requestedIndex < 0 || (value === "review" && !canReview.value)) {
    return;
  }

  // Commit the valid navigation request.
  stepIndex.value = requestedIndex;
}

/**
 * Validates current answers and advances from Character Setup to review.
 *
 * @returns Nothing; updates navigation or exposes the compiler error to the player.
 * @remarks This is navigation coordination only. All technical validation remains in
 * the pure compiler and validation modules.
 */
function next(): void {
  try {
    // Force compilation before moving to a step that consumes compiled output.
    void compiled.value;

    // Clear any previous failure and advance only after successful validation.
    error.value = "";
    stepIndex.value = 1;
  } catch (cause) {
    // Normalize unknown thrown values into text suitable for the form footer.
    error.value = cause instanceof Error ? cause.message : String(cause);
  }
}
</script>

<template>
  <UPageBody class="mx-auto w-full max-w-4xl px-4 py-10 sm:px-8">
    <div class="mb-8">
      <p class="text-sm font-semibold uppercase tracking-widest text-amber-500">
        Guided setup
      </p>
      <h1 class="mt-2 text-3xl font-bold text-white">Online YAML Builder</h1>
      <p class="mt-2 text-muted">
        Answer gameplay questions; the builder derives and validates the
        Archipelago options.
      </p>
    </div>
    <UTabs
      :model-value="activeStepId"
      :items="stepTabs"
      :content="false"
      color="primary"
      variant="pill"
      size="lg"
      class="mb-6"
      :ui="{
        list: 'bg-black/45 ring-1 ring-amber-500/20 shadow-lg shadow-black/30',
        trigger: 'cursor-pointer data-[state=active]:font-bold',
      }"
      aria-label="Wizard progress"
      @update:model-value="setActiveStep"
    />
    <UCard variant="subtle">
      <template #header
        ><h2 class="text-xl font-bold text-white">
          {{ wizardSteps[stepIndex]!.title }}
        </h2>
        <p
          v-if="wizardSteps[stepIndex]!.description"
          class="mt-1 text-sm text-muted"
        >
          {{ wizardSteps[stepIndex]!.description }}
        </p></template
      >
      <CharacterSetupStep
        v-if="stepIndex === 0"
        v-model="answers.characters"
        :available-characters="availableCharacters"
      />
      <ReviewStep
        v-else
        :summary="summarizeCharacterAnswers(answers.characters)"
        :yaml="optionsToYaml(characterSettings)"
      />
      <template #footer
        ><div class="flex items-center justify-between gap-4">
          <p class="text-sm text-error">{{ error }}</p>
          <div class="ml-auto flex gap-2">
            <UButton
              v-if="stepIndex > 0"
              color="neutral"
              variant="soft"
              @click="stepIndex--"
              >Back</UButton
            ><UButton
              v-if="stepIndex === 0"
              :disabled="!answers.characters.selectedCharacters.length"
              trailing-icon="i-glyphs-arrow-right-bold"
              @click="next"
              >Review settings</UButton
            >
          </div>
        </div></template
      >
    </UCard>
  </UPageBody>
</template>
