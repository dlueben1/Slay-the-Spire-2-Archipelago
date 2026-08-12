<script setup lang="ts">
import type { TabsItem } from "@nuxt/ui";
import { computed, reactive, ref } from "vue";
import CharacterSetupStep from "../components/wizard/CharacterSetupStep.vue";
import CheckSetupStep from "../components/wizard/CheckSetupStep.vue";
import DeathLinkStep from "../components/wizard/DeathLinkStep.vue";
import ProgressionStep from "../components/wizard/ProgressionStep.vue";
import ReviewStep from "../components/wizard/ReviewStep.vue";
import RunSettingsStep from "../components/wizard/RunSettingsStep.vue";
import { optionCatalog } from "../generated/optionCatalog";
import { buildWizardYaml } from "../services/YamlService";
import { createDefaultWizardAnswers } from "../wizard/WizardAnswers";
import { getConfiguredCharacterNames } from "../wizard/CharacterRoster";
import {
  createDefaultFillerAnswers,
  createFillerDisplayItems,
} from "../wizard/FillerItem";
import { selectGuidedOptions } from "../wizard/GuidedOption";
import {
  DEATH_LINK_OPTION_KEYS,
  getGeneratedNumberRange,
  PROGRESSION_OPTION_KEYS,
  RUN_OPTION_KEYS,
  SHOP_OPTION_KEYS,
} from "../wizard/WizardOptionKey";
import { compileWizardAnswers } from "../wizard/compiler/compileWizardAnswers";
import { buildWizardReviewSections } from "../wizard/review";
import { wizardSteps } from "../wizard/WizardStep";

const availableCharacters = optionCatalog.options.characters?.valid_keys ?? [];
const fillerItems = createFillerDisplayItems(optionCatalog);
const defaultFillerAnswers = createDefaultFillerAnswers(optionCatalog);
const answers = reactive(
  createDefaultWizardAnswers(
    availableCharacters,
    defaultFillerAnswers,
    optionCatalog,
  ),
);
const stepIndex = ref(0);
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

const relicChoiceRange = getGeneratedNumberRange(
  optionCatalog,
  RUN_OPTION_KEYS.relicChoiceCount,
);
const progressionBalancingRange = getGeneratedNumberRange(
  optionCatalog,
  PROGRESSION_OPTION_KEYS.progressionBalancing,
);
const shopSlotRanges = {
  cardSlots: getGeneratedNumberRange(optionCatalog, SHOP_OPTION_KEYS.cardSlots),
  neutralCardSlots: getGeneratedNumberRange(
    optionCatalog,
    SHOP_OPTION_KEYS.neutralCardSlots,
  ),
  relicSlots: getGeneratedNumberRange(
    optionCatalog,
    SHOP_OPTION_KEYS.relicSlots,
  ),
  potionSlots: getGeneratedNumberRange(
    optionCatalog,
    SHOP_OPTION_KEYS.potionSlots,
  ),
};
const deathLinkDamageRange = getGeneratedNumberRange(
  optionCatalog,
  DEATH_LINK_OPTION_KEYS.damagePercent,
);

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

/**
 * Builds the exact complete YAML required to enter Review.
 *
 * @returns Validated Archipelago YAML containing metadata and guided game settings.
 * @throws When the player name or compiled option snapshot is invalid.
 */
function buildCurrentYaml(): string {
  // Use one service result for every delivery path so metadata cannot diverge.
  return buildWizardYaml(answers.playerName, getGuidedSettings());
}

/**
 * Gets render-safe YAML for an already-open Review step.
 *
 * @returns The complete current YAML, or an empty string while input is invalid.
 * @remarks A player may edit the persistent name field while Review is open. Returning
 * an empty preview prevents a render exception; navigation still uses strict validation.
 */
function getYamlPreview(): string {
  try {
    // Reuse the strict builder so a valid preview is always production-ready.
    return buildCurrentYaml();
  } catch {
    // Let Review show its inline invalid-state prompt until the name is corrected.
    return "";
  }
}

const yamlPreview = computed(getYamlPreview);

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
 * Determines whether current answers can safely open the review step.
 *
 * @returns `true` when compilation and final schema validation both succeed.
 * @remarks Expected invalid form state is converted to `false`; `next` displays details.
 */
function canCompileForReview(): boolean {
  try {
    // The strict builder runs option compilation plus player-metadata validation.
    void buildCurrentYaml();

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

  try {
    // Review additionally requires valid document metadata; other steps need options only.
    const nextStep = wizardSteps[stepIndex.value + 1];

    if (nextStep?.id === "review") {
      void buildCurrentYaml();
    } else {
      void compileCurrentAnswers();
    }

    // Clear any previous failure and advance only after successful validation.
    error.value = "";
    stepIndex.value = Math.min(stepIndex.value + 1, wizardSteps.length - 1);
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
      <p class="mt-2 mb-6 text-muted">
        Answer gameplay questions to generate the YAML file required by
        Archipelago.
      </p>

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
        </p></template
      >
      <CharacterSetupStep
        v-if="activeStepId === 'characters'"
        v-model="answers.characters"
        :available-characters="availableCharacters"
      />
      <RunSettingsStep
        v-else-if="activeStepId === 'run'"
        v-model="answers.run"
        :relic-choice-range="relicChoiceRange"
      />
      <CheckSetupStep
        v-else-if="activeStepId === 'checks'"
        v-model="answers.checksAndRewards"
        :filler-items="fillerItems"
        :shop-slot-ranges="shopSlotRanges"
      />
      <DeathLinkStep
        v-else-if="activeStepId === 'death-link'"
        v-model="answers.deathLink"
        :damage-range="deathLinkDamageRange"
      />
      <ProgressionStep
        v-else-if="activeStepId === 'progression'"
        v-model="answers.progression"
        :progression-balancing-range="progressionBalancingRange"
      />
      <ReviewStep
        v-else
        :sections="reviewSections"
        :yaml="yamlPreview"
        :player-name="answers.playerName"
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
