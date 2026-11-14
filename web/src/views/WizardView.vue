<script setup lang="ts">
import type { TabsItem } from "@nuxt/ui";
import { computed, reactive, ref, watch, type Component } from "vue";
import { useRouter } from "vue-router";
import ChallengesStep from "../components/wizard/steps/ChallengesStep.vue";
import CharacterSetupStep from "../components/wizard/steps/CharacterSetupStep.vue";
import CheckSetupStep from "../components/wizard/steps/CheckSetupStep.vue";
import DeathLinkStep from "../components/wizard/steps/DeathLinkStep.vue";
import ProgressionStep from "../components/wizard/steps/ProgressionStep.vue";
import ReviewStep from "../components/wizard/steps/ReviewStep.vue";
import RunSettingsStep from "../components/wizard/steps/RunSettingsStep.vue";
import { provideWizardAnswers } from "../components/wizard/core/wizardAnswersContext";
import { optionCatalog } from "../generated/optionCatalog";
import { buildWizardYaml } from "../services/YamlService";
import { builderYamlComments } from "../services/YamlTemplateComments";
import { createDefaultWizardAnswers } from "../wizard/WizardAnswers";
import {
  clearSavedWizardState,
  getBrowserAnswerStorage,
  loadSavedWizardState,
  saveWizardState,
} from "../wizard/answerPersistence";
import { getConfiguredCharacterNames } from "../wizard/CharacterRoster";
import {
  createDefaultFillerAnswers,
  createFillerDisplayItems,
} from "../wizard/FillerItem";
import { selectGuidedOptions } from "../wizard/GuidedOption";
import { compileWizardAnswers } from "../wizard/compiler/compileWizardAnswers";
import { buildWizardReviewSections } from "../wizard/review";
import { wizardSteps, type WizardSectionKey } from "../wizard/WizardStep";

/** Component responsible for each answer section declared by `wizardSteps`. */
const sectionStepComponents: Record<WizardSectionKey, Component> = {
  characters: CharacterSetupStep,
  run: RunSettingsStep,
  checksAndRewards: CheckSetupStep,
  deathLink: DeathLinkStep,
  challenges: ChallengesStep,
  progression: ProgressionStep,
};

const availableCharacters = optionCatalog.options.characters?.valid_keys ?? [];
const fillerItems = createFillerDisplayItems(optionCatalog);
const defaultFillerAnswers = createDefaultFillerAnswers(optionCatalog);

/** Fresh default answers, for first visits and Start over. */
function createFreshAnswers() {
  return createDefaultWizardAnswers(
    availableCharacters,
    defaultFillerAnswers,
    optionCatalog,
  );
}

const answerStorage = getBrowserAnswerStorage();
const savedState = loadSavedWizardState(answerStorage, createFreshAnswers());
const answers = reactive(savedState?.answers ?? createFreshAnswers());

// Share the root model read-only so declarative flow logic avoids prop drilling.
provideWizardAnswers(answers);

/** Index of a step by ID, or -1 when no step has that ID. */
function findStepIndex(stepId: string): number {
  return wizardSteps.findIndex((step) => step.id === stepId);
}

const stepIndex = ref(Math.max(0, findStepIndex(savedState?.stepId ?? "")));
const error = ref("");

/**
 * Checks whether Character Setup currently contains any complete roster entry.
 *
 * @returns Whether navigation may leave the first step for compiler validation.
 * @remarks Empty modded rows do not count until their required internal ID is entered.
 */
function getHasConfiguredCharacters(): boolean {
  // Treat built-in portraits and named modded characters as one player-facing roster.
  return getConfiguredCharacterNames(answers.characters).length > 0;
}

const hasConfiguredCharacters = computed(getHasConfiguredCharacters);

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

/**
 * Selects options owned by every implemented guided section.
 *
 * @returns A record containing canonical values in wizard step order.
 * @throws When in-progress answers cannot be compiled into generated options.
 * @remarks Call only from an explicit validation boundary such as `next` or
 * `buildCurrentYaml`; running strict compilation in an unguarded template computed
 * would turn an incomplete form row into an unhandled Vue render error.
 */
function getGuidedSettings() {
  // Compile at the strict boundary before selecting the player-facing option subset.
  const compiledOptions = compileCurrentAnswers();

  // Delegate ordered key selection to the registry shared with section compilers.
  return selectGuidedOptions(compiledOptions);
}

/** Comments for the generated YAML, pointing back to this copy of the builder. */
const yamlComments = builderYamlComments(
  new URL(useRouter().resolve({ name: "builder" }).href, window.location.href)
    .href,
);

/**
 * Builds the exact complete YAML required to enter Review.
 *
 * @returns Validated Archipelago YAML containing metadata and guided game settings.
 * @throws When the player name or compiled option snapshot is invalid.
 */
function buildCurrentYaml(): string {
  // Use one service result for every delivery path so metadata cannot diverge.
  return buildWizardYaml(answers.playerName, getGuidedSettings(), yamlComments);
}

interface WizardCompileState {
  ok: boolean;
  yaml: string;
  error: string;
}

/**
 * Compiles one render-safe snapshot for Review navigation and preview.
 *
 * @returns Success, YAML text, and any normalized validation failure.
 * @remarks A player may edit the persistent name field while Review is open. Returning
 * an empty preview prevents a render exception; navigation still uses strict validation.
 */
function getCompileState(): WizardCompileState {
  try {
    return { ok: true, yaml: buildCurrentYaml(), error: "" };
  } catch (cause) {
    return {
      ok: false,
      yaml: "",
      error: cause instanceof Error ? cause.message : String(cause),
    };
  }
}

const compileState = computed(getCompileState);
const yamlPreview = computed(() => compileState.value.yaml);
const canReview = computed(() => compileState.value.ok);

// A restored Review step needs answers that still compile (the options may have
// changed since the save); otherwise resume on the step before it.
if (activeStepIsReview() && !canReview.value) {
  stepIndex.value -= 1;
}

function activeStepIsReview(): boolean {
  return wizardSteps[stepIndex.value]?.id === "review";
}

// Save on every change so a refresh or an accidental Back loses nothing.
watch(
  [answers, stepIndex],
  () => {
    saveWizardState(answerStorage, {
      answers,
      stepId: wizardSteps[stepIndex.value]?.id ?? "",
    });
  },
  { deep: true },
);

const isStartOverOpen = ref(false);

/** Clears every answer, including the player name, and returns to the first step. */
function startOver(): void {
  Object.assign(answers, createFreshAnswers());
  clearSavedWizardState(answerStorage);
  stepIndex.value = 0;
  error.value = "";
  importWarnings.value = [];
  isStartOverOpen.value = false;
  window.scrollTo({ top: 0, behavior: "smooth" });
}

const isImportOpen = ref(false);
const importText = ref("");
const importError = ref("");
/** Notes from the last import about settings it changed or left out. */
const importWarnings = ref<string[]>([]);

/** Opens the import dialog with an empty draft. */
function openImport(): void {
  importText.value = "";
  importError.value = "";
  isImportOpen.value = true;
}

/** Reads a chosen file into the import draft. */
async function readImportFile(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  // Clear the input so choosing the same file again still fires a change.
  input.value = "";
  if (!file) return;

  try {
    importText.value = await file.text();
    importError.value = "";
  } catch {
    importError.value = "The browser couldn't read that file.";
  }
}

/**
 * Replaces every answer with the draft YAML's settings and returns to the first step.
 * Errors stay in the dialog; warnings show above the builder after it closes.
 */
async function loadImport(): Promise<void> {
  let result;
  try {
    // Loaded on demand: the YAML parser is only needed by players who import.
    const { importWizardYaml } =
      await import("../wizard/import/importWizardYaml");
    result = importWizardYaml(importText.value, optionCatalog);
  } catch (cause) {
    importError.value = cause instanceof Error ? cause.message : String(cause);
    return;
  }

  Object.assign(answers, result.answers);
  importWarnings.value = result.warnings;
  stepIndex.value = 0;
  error.value = "";
  isImportOpen.value = false;
  window.scrollTo({ top: 0, behavior: "smooth" });
}

/**
 * Builds player-facing review sections for all implemented guided answers.
 *
 * @returns Titled summaries in wizard navigation order.
 */
function getReviewSections() {
  // Keep prose composition in the review layer rather than the Vue template.
  return buildWizardReviewSections(answers);
}

const reviewSections = computed(getReviewSections);

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

/** Active declarative step, including its answer-section ownership. */
const activeStep = computed(
  () => wizardSteps[stepIndex.value] ?? wizardSteps[0]!,
);

/** Component registered for the active answer section; Review is rendered separately. */
const activeSectionComponent = computed(() => {
  const sectionKey = activeStep.value.sectionKey;
  return sectionKey ? sectionStepComponents[sectionKey] : null;
});

/** Reactive answer object owned by the active section step. */
const activeSectionAnswers = computed(() => {
  const sectionKey = activeStep.value.sectionKey;
  return sectionKey ? answers[sectionKey] : null;
});

/** Extra presentation data required by the two bespoke section components. */
const activeSectionProps = computed<Record<string, unknown>>(() => {
  if (activeStep.value.sectionKey === "characters") {
    return { availableCharacters };
  }

  if (activeStep.value.sectionKey === "checksAndRewards") {
    return { fillerItems };
  }

  return {};
});

/**
 * Applies a complete section value emitted by the active registered component.
 *
 * @param value - Immutable section answer object emitted through component `v-model`.
 * @returns Nothing; mutates only the root reactive model owned by this view.
 */
function setActiveSectionAnswers(value: object): void {
  const sectionKey = activeStep.value.sectionKey;

  if (!sectionKey) {
    return;
  }

  // Preserve the provided section reference while applying the child's immutable value.
  Object.assign(answers[sectionKey], value);
}

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
 * Validates current answers and advances to the next wizard step.
 *
 * @returns Nothing; updates navigation or exposes the compiler error to the player.
 * @remarks This is navigation coordination only. All technical validation remains in
 * the pure compiler and validation modules.
 */
function next(): void {
  window.scrollTo({
    top: 0,
    behavior: "smooth",
  });

  // Review additionally requires valid document metadata; other steps need options only.
  const nextStep = wizardSteps[stepIndex.value + 1];

  if (nextStep?.id === "review") {
    if (!compileState.value.ok) {
      error.value = compileState.value.error;
      return;
    }
  } else {
    try {
      void compileCurrentAnswers();
    } catch (cause) {
      // Normalize unknown thrown values into text suitable for the form footer.
      error.value = cause instanceof Error ? cause.message : String(cause);
      return;
    }
  }

  // Clear any previous failure and advance only after successful validation.
  error.value = "";
  stepIndex.value = Math.min(stepIndex.value + 1, wizardSteps.length - 1);
}
</script>

<template>
  <UPageBody class="mx-auto w-full max-w-4xl px-4 py-10 sm:px-8">
    <div class="mb-8">
      <p class="text-sm font-semibold uppercase tracking-widest text-amber-500">
        Guided setup
      </p>
      <h1 class="mt-2 text-3xl font-bold text-white">Online YAML Builder</h1>
      <p class="mt-2 mb-6 text-muted">
        Answer gameplay questions to generate the YAML file required by
        Archipelago.
      </p>

      <div class="mb-3 flex flex-wrap items-center justify-end gap-x-4 gap-y-2">
        <p class="mr-auto text-sm text-muted">
          Your answers are saved in this browser.
        </p>
        <UButton
          icon="i-glyphs-folder-open-bold"
          color="neutral"
          variant="soft"
          size="sm"
          class="cursor-pointer"
          @click="openImport"
          >Import YAML</UButton
        >
        <UButton
          icon="i-glyphs-arrows-round-bold"
          color="neutral"
          variant="soft"
          size="sm"
          class="cursor-pointer"
          @click="isStartOverOpen = true"
          >Start over</UButton
        >
      </div>

      <UModal
        v-model:open="isStartOverOpen"
        title="Start over?"
        description="This clears every answer, including your player name, and goes back to the first step. It can't be undone."
        :ui="{ footer: 'justify-end' }"
      >
        <template #footer>
          <UButton
            label="Cancel"
            color="neutral"
            variant="outline"
            class="cursor-pointer"
            @click="isStartOverOpen = false"
          />
          <UButton
            label="Start over"
            color="error"
            class="cursor-pointer"
            @click="startOver"
          />
        </template>
      </UModal>

      <UModal
        v-model:open="isImportOpen"
        title="Import a YAML"
        description="Load a Slay the Spire II YAML, from this builder or edited by hand, to change it here. This replaces your current answers."
        :ui="{ content: 'sm:max-w-2xl', footer: 'justify-end' }"
      >
        <template #body>
          <div class="space-y-3">
            <label
              class="inline-flex cursor-pointer items-center gap-2 rounded-md bg-elevated px-3 py-1.5 text-sm font-medium ring-1 ring-default hover:bg-accented"
            >
              <UIcon name="i-glyphs-folder-open-bold" />
              Choose a file
              <input
                type="file"
                accept=".yaml,.yml,.txt,text/yaml"
                class="sr-only"
                @change="readImportFile"
              />
            </label>
            <UTextarea
              v-model="importText"
              placeholder="Or paste the YAML here"
              :rows="12"
              autoresize
              :maxrows="20"
              class="w-full font-mono"
              aria-label="YAML to import"
            />
            <p v-if="importError" class="text-sm text-error">
              {{ importError }}
            </p>
          </div>
        </template>
        <template #footer>
          <UButton
            label="Cancel"
            color="neutral"
            variant="outline"
            class="cursor-pointer"
            @click="isImportOpen = false"
          />
          <UButton
            label="Import"
            color="primary"
            class="cursor-pointer"
            :disabled="!importText.trim()"
            @click="loadImport"
          />
        </template>
      </UModal>

      <UAlert
        v-if="importWarnings.length"
        color="warning"
        variant="subtle"
        icon="i-glyphs-exclamation-circle-bold"
        title="Imported, with some changes"
        close
        class="mb-3"
        :ui="{ title: 'font-bold' }"
        @update:open="importWarnings = []"
      >
        <template #description>
          <ul class="list-disc space-y-1 pl-4">
            <li v-for="(warning, index) in importWarnings" :key="index">
              {{ warning }}
            </li>
          </ul>
        </template>
      </UAlert>

      <UPageCard variant="subtle">
        <UFormField
          label="Player Name"
          description="This is the name you'll be referred to as when sending/receiving items in Archipelago, and it's the Slot name you'll connect to your Archipelago session with."
          required
        >
          <UInput
            v-model="answers.playerName"
            placeholder="Enter your Archipelago player name"
            autocomplete="name"
            class="w-full"
          />
        </UFormField>
      </UPageCard>
    </div>
    <UTabs
      :model-value="activeStepId"
      :items="stepTabs"
      :content="false"
      color="primary"
      variant="pill"
      size="lg"
      class="sticky top-(--ui-header-height) z-40 mb-6"
      :ui="{
        list: 'overflow-x-auto bg-gray-900/90 ring-1 ring-amber-500/20 shadow-lg shadow-black/30',
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
        </p>
        <UAlert
          v-if="wizardSteps[stepIndex]!.warning"
          color="warning"
          variant="subtle"
          icon="i-glyphs-exclamation-circle-bold"
          :title="wizardSteps[stepIndex]!.warning!.title"
          :description="wizardSteps[stepIndex]!.warning!.description"
          class="mt-4"
          :ui="{ title: 'font-bold' }"
      /></template>
      <ReviewStep
        v-if="activeStepId === 'review'"
        :sections="reviewSections"
        :yaml="yamlPreview"
        :player-name="answers.playerName"
      />
      <component
        :is="activeSectionComponent"
        v-else-if="activeSectionComponent && activeSectionAnswers"
        :model-value="activeSectionAnswers"
        v-bind="activeSectionProps"
        @update:model-value="setActiveSectionAnswers"
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
              v-if="stepIndex < wizardSteps.length - 1"
              :disabled="!hasConfiguredCharacters"
              class="cursor-pointer"
              @click="next"
              >{{
                activeStepId === "progression"
                  ? "Review & Download"
                  : "Continue"
              }}</UButton
            >
          </div>
        </div></template
      >
    </UCard>
  </UPageBody>
</template>
