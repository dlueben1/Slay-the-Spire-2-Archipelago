/**
 * @file The Challenges opt-in, three banners the player ticks one after another.
 *
 * Logic accounts for none of the challenges, so turning them on takes three separate
 * confirmations. Each banner appears below the last once that one is ticked, each with
 * a sterner prompt, and challenges turn on with the third. Unticking any banner turns
 * challenges off and hides the banners after it. The tick count is view state only;
 * the answer model stores just whether challenges are on.
 */

/** Copy and colour of one opt-in banner. */
export interface ChallengeOptInBanner {
  label: string;
  description: string;
  color: "primary" | "warning" | "error";
}

/** The banners in the order they appear. */
export const CHALLENGE_OPT_IN_BANNERS: readonly ChallengeOptInBanner[] = [
  {
    label: "Turn on challenges",
    description:
      "I understand that logic doesn't account for challenges and that my seed may get stuck.",
    color: "primary",
  },
  {
    label: "Are you sure?",
    description:
      "Challenges can make runs too hard to win, and logic won't notice. A stuck seed holds up everyone else in the multiworld.",
    color: "warning",
  },
  {
    label: "Are you really, really sure?",
    description: "Last warning. Tick this and the challenges are yours.",
    color: "error",
  },
];

/** How far the player has got through the banners. */
export interface ChallengeOptInState {
  /** Banners ticked, counted from the first. */
  ticked: number;
  /** Whether every banner is ticked, which turns challenges on. */
  enabled: boolean;
}

/**
 * Applies a tick or untick on one banner.
 *
 * @param ticked - Banners ticked before this change.
 * @param index - Position of the changed banner.
 * @param value - Whether the banner is now ticked.
 * @returns The new tick count and whether challenges are on.
 */
export function tickChallengeOptInBanner(
  ticked: number,
  index: number,
  value: boolean,
): ChallengeOptInState {
  // Unticking a banner also clears every banner after it.
  let nextTicked = value ? ticked : Math.min(ticked, index);

  // Only the next unticked banner is shown, so only it can be ticked.
  if (value && index === ticked) {
    nextTicked = ticked + 1;
  }

  return {
    ticked: nextTicked,
    enabled: nextTicked === CHALLENGE_OPT_IN_BANNERS.length,
  };
}
