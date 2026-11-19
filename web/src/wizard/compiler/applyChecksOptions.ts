/**
 * @file Compiles the Checks step: Starting Equipment and the check-types grid.
 *
 * Starting Equipment lives on this step because its progressive items fill slots that
 * only Floor Checks create; `applyStartingEquipmentOptions` turns both options off
 * while Floor Checks are off.
 */

import type { OptionCatalog } from "../../generated/optionCatalog";
import type { ChecksAnswers } from "../WizardAnswers";
import type { CompiledOptions } from "./applyCharacterOptions";
import { applyCheckOptions } from "./applyCheckOptions";
import { applyStartingEquipmentOptions } from "./applyStartingEquipmentOptions";

/**
 * Applies every option owned by the Checks step.
 *
 * @param target - Mutable complete option snapshot being assembled by the root compiler.
 * @param answers - Starting Equipment and check-type intent.
 * @param catalog - Generated schema used by each focused option-family compiler.
 * @returns Nothing; the supplied option snapshot is updated in place.
 */
export function applyChecksOptions(
  target: CompiledOptions,
  answers: ChecksAnswers,
  catalog: OptionCatalog,
): void {
  applyStartingEquipmentOptions(
    target,
    answers.startingEquipment,
    answers.types.includeFloorChecks,
    catalog,
  );
  applyCheckOptions(target, answers.types, catalog);
}
