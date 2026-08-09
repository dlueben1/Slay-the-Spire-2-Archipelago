/**
 * @file Builds player-facing review copy from wizard answers.
 *
 * Review text is derived from the same player intent sent to the compiler, but it does
 * not inspect or translate Archipelago options. Add summary builders here when adding
 * guided sections, then compose them in the review UI. This keeps prose generation out
 * of Vue templates and keeps the compiler focused on technical values.
 */

import type { CharacterAnswers, FillerAnswers } from "./WizardAnswers";

/**
 * Formats a list of names as natural English with an Oxford comma.
 *
 * @param names - Ordered display names to join.
 * @returns A readable phrase such as `Ironclad, Silent, and Defect`.
 * @remarks The empty fallback is defensive; valid Character Setup answers are non-empty.
 */
function joinNames(names: readonly string[]): string {
  // Zero and one item do not need a conjunction.
  if (names.length < 2) {
    return names[0] ?? "no characters";
  }

  // Two items use a conjunction without a comma.
  if (names.length === 2) {
    return `${names[0]} and ${names[1]}`;
  }

  // Longer lists use comma separators and an Oxford comma before the last item.
  return `${names.slice(0, -1).join(", ")}, and ${names.at(-1)}`;
}

/**
 * Converts a small count into a word for friendly review prose.
 *
 * @param count - Non-negative count to display.
 * @returns An English word for zero through ten, otherwise the numeric string.
 */
function countWord(count: number): string {
  // Character ranges currently stop at ten, but retain a safe fallback for later data.
  return (
    [
      "zero",
      "one",
      "two",
      "three",
      "four",
      "five",
      "six",
      "seven",
      "eight",
      "nine",
      "ten",
    ][count] ?? String(count)
  );
}

/**
 * Summarizes Character Setup choices in gameplay language.
 *
 * @param answers - Valid current Character Setup answers.
 * @returns A short paragraph covering selection, unlocking, and completion goal.
 * @remarks Compile and validate before displaying this summary so impossible answer
 * combinations cannot be presented as if they were valid.
 */
export function summarizeCharacterAnswers(answers: CharacterAnswers): string {
  // Resolve how many characters actually enter the generated world.
  const count =
    answers.selectionMode === "random"
      ? answers.randomCharacterCount
      : answers.selectedCharacters.length;

  // Describe whether the selected pool is used directly or sampled randomly.
  const selection =
    answers.selectionMode === "random"
      ? `${countWord(count)} of ${joinNames(answers.selectedCharacters)} will be randomly selected.`
      : `You will play ${joinNames(answers.selectedCharacters)}.`;

  // Describe the player's chosen character-unlock experience.
  const availability =
    answers.availability === "all"
      ? "All generated characters will be available from the start."
      : answers.availability === "random"
        ? "You will begin with one random character and unlock the rest through the multiworld."
        : `You will begin with ${answers.startingCharacter ?? "your chosen character"} and unlock the rest through the multiworld.`;

  // Resolve the friendly "all" concept to a count only for sentence construction.
  const goalCount = answers.goal === "all" ? count : answers.goal;

  // Use more natural wording when every generated character must finish.
  const goal =
    goalCount === count
      ? `${count === 1 ? "That character" : "All of them"} must complete a run to reach your goal.`
      : `${countWord(goalCount)} ${goalCount === 1 ? "character" : "characters"} must complete a run to reach your goal.`;

  // Join the independently derived clauses into the final review paragraph.
  return `${selection} ${availability} ${goal}`;
}

/**
 * Summarizes the distribution selected in the Filler Setup step.
 *
 * @param answers - Valid current filler-weight answers.
 * @returns A short sentence describing enabled items and their relative levels.
 * @remarks The summary intentionally describes relative odds rather than percentages.
 */
export function summarizeFillerAnswers(answers: FillerAnswers): string {
  // Count each semantic level without depending on generated option names or raw weights.
  let disabledCount = 0;
  let lowCount = 0;
  let mediumCount = 0;
  let highCount = 0;

  for (const level of Object.values(answers.weights)) {
    if (level === 0) {
      disabledCount += 1;
    } else if (level === 1) {
      lowCount += 1;
    } else if (level === 2) {
      mediumCount += 1;
    } else {
      highCount += 1;
    }
  }

  // Derive the enabled total so the first clause is easy to scan.
  const totalCount = Object.keys(answers.weights).length;
  const enabledCount = totalCount - disabledCount;

  // Explain the relative distribution without promising exact random percentages.
  return `Your filler pool enables ${enabledCount} of ${totalCount} reward types: ${lowCount} at low odds, ${mediumCount} at medium odds, and ${highCount} at high odds.`;
}
