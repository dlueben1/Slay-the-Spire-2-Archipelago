/**
 * @file Reads the per-option comments from the hand-written YAML template.
 *
 * `world/spire2/docs/Spire2-template.yaml` is the release template players edit by
 * hand. Its comments are shorter than the option docstrings, so the builder copies
 * them into its own output instead of writing a second set of explanations. The
 * template stays the only place that text is written.
 */

import spire2Template from "../../../world/spire2/docs/Spire2-template.yaml?raw";

/** Comments the template attaches to one top-level game option. */
export interface OptionComment {
  /** Comment lines directly above the option, without their `# ` prefix. */
  above: string[];
  /** Comment after the option's value on the same line, if any. */
  trailing?: string;
}

/** Comments written into a builder-generated YAML document. */
export interface YamlComments {
  /** Lines printed above the document, without their `# ` prefix. */
  header: string[];
  /** Comments for each top-level game option, keyed by option name. */
  options: ReadonlyMap<string, OptionComment>;
}

/** Leaves the generated document without any comments. */
export const NO_YAML_COMMENTS: YamlComments = {
  header: [],
  options: new Map(),
};

/** A top-level game option line, with an optional trailing comment. */
const OPTION_LINE = /^ {2}([a-z0-9_]+):(.*?)(?:\s+#\s?(.*))?$/;

/** A comment line inside the game mapping. */
const COMMENT_LINE = /^ {2}# ?(.*)$/;

/**
 * Collects the comments the template places on each option of one game.
 *
 * @param template - Hand-written template text.
 * @param gameName - Game mapping whose options should be read.
 * @returns Comments keyed by option name. An option keeps only the comment block
 * that touches it; a block separated by a blank line (such as a section banner or a
 * section introduction) belongs to no option and is skipped.
 */
export function parseTemplateComments(
  template: string,
  gameName: string,
): Map<string, OptionComment> {
  const comments = new Map<string, OptionComment>();
  let insideGame = false;
  let pendingBlock: string[] = [];

  for (const line of template.split(/\r?\n/)) {
    if (!insideGame) {
      insideGame = line === `${gameName}:`;
      continue;
    }

    // Another top-level key ends the game mapping.
    if (/^\S/.test(line)) {
      break;
    }

    const comment = COMMENT_LINE.exec(line);
    if (comment) {
      pendingBlock.push(comment[1]!.trimEnd());
      continue;
    }

    const option = OPTION_LINE.exec(line);
    if (option) {
      const [, key, , trailing] = option;
      if (pendingBlock.length || trailing) {
        comments.set(key!, {
          above: pendingBlock,
          ...(trailing ? { trailing: trailing.trimEnd() } : {}),
        });
      }
    }

    // Blank lines, list entries and options all end the current comment block.
    pendingBlock = [];
  }

  return comments;
}

/** Comments read from the template bundled with this build of the site. */
const templateOptionComments = parseTemplateComments(
  spire2Template,
  "Slay the Spire II",
);

/**
 * Builds the comments for a document made by the YAML Builder.
 *
 * @param builderUrl - Address of the builder that made the document, so players
 * can come back to the same site to make a new one.
 * @returns A short header plus the template's per-option comments.
 */
export function builderYamlComments(builderUrl: string): YamlComments {
  return {
    header: [
      "Made with the Slay the Spire II YAML Builder:",
      `  ${builderUrl}`,
      "Make a new file there, or edit this one by hand; the comments explain each option.",
      "",
      "Any option can be randomised by giving weights instead of a single value, e.g.",
      "  ancient_relic_pool: {balanced: 1, chaos: 2}",
      "picks chaos twice as often as balanced.",
    ],
    options: templateOptionComments,
  };
}
