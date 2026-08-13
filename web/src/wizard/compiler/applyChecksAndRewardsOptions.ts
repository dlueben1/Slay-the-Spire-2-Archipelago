/**
 * @file Compiles the combined Checks & Rewards wizard section.
 *
 * The visible wizard presents Starting Equipment, Ancient rewards, ordinary checks,
 * conditional Shop Sanity controls, and filler weights as one cohesive step. Their
 * mappings remain in focused helper compilers because each option family has different
 * validation rules. This facade mirrors the UI and answer-model ownership while
 * preserving those readable, independently testable translations.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import type { ChecksAndRewardsAnswers } from "../WizardAnswers";
import type { CompiledOptions } from "./applyCharacterOptions";
import { applyAncientOptions } from "./applyAncientOptions";
import { applyCheckOptions } from "./applyCheckOptions";
import { applyFillerOptions } from "./applyFillerOptions";
import { applyShopOptions } from "./applyShopOptions";
import { applyStartingEquipmentOptions } from "./applyStartingEquipmentOptions";

/**
 * Applies every option owned by the combined Checks & Rewards wizard step.
 *
 * @param target - Mutable complete option snapshot being assembled by the root compiler.
 * @param answers - Starting Equipment, Ancient, check, Shop, and filler intent.
 * @param catalog - Generated schema used by each focused option-family compiler.
 * @returns Nothing; the supplied option snapshot is updated in place.
 * @throws When a generated option is absent or any nested answer cannot be compiled.
 * @remarks Call this only from the root compiler after defaults have been populated.
 * The order intentionally matches the combined step's subsection presentation.
 */
export function applyChecksAndRewardsOptions(
  target: CompiledOptions,
  answers: ChecksAndRewardsAnswers,
  catalog: OptionCatalog,
): void {
  // Compile Starting Equipment first and enforce its dependency on Floor Checks.
  applyStartingEquipmentOptions(
    target,
    answers.startingEquipment,
    answers.checks.includeFloorChecks,
    catalog,
  );

  // Keep Ancient choices above the additional-check controls in the wizard and YAML.
  applyAncientOptions(target, answers.ancients, catalog);

  // Compile the always-visible independent check and reward toggles next.
  applyCheckOptions(target, answers.checks, catalog);

  // Compile the conditional Shop Sanity family as one dependent subsection.
  applyShopOptions(target, answers.shop, catalog);

  // Compile the existing filler table last because it closes the visible section.
  applyFillerOptions(target, answers.filler, catalog);
}
