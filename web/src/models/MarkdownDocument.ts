import { Marked } from "marked";
import markedAlert from "marked-alert";
import DOMPurify from "dompurify";

/**
 * Represents a Markdown Document
 */
export class MarkdownDocument {
  /**
   * The title of the Markdown Document, extracted from the first heading
   */
  private _header: string | null;

  /**
   * Returns the title of the Markdown Document, or null if no title is found.
   */
  public get header(): string | null {
    return this._header;
  }

  // #region Initialization

  /**
   * Builds the Markdown Document from the raw text
   * @param content - The raw content of the Markdown Document
   */
  constructor(private readonly content: string) {
    this._header = this.extractMarkdownTitle();
  }

  // #endregion

  // #region Header Helpers

  /**
   * Returns the title of a Markdown Document, based on it's first heading.
   * @returns The title of the Markdown document, or null if no title is found.
   */
  private extractMarkdownTitle(): string | null {
    // Look for the first line that starts with a single '#' followed by a space
    const lines = this.content.split("\n");
    for (const line of lines) {
      if (line.startsWith("# ")) {
        return line.slice(2).trim();
      }
    }
    return null;
  }

  /**
   * Removes the first heading from a Markdown document, if it exists.
   * @returns The Markdown document without the first heading.
   */
  private removeMarkdownTitle(): string {
    const lines = this.content.split("\n");

    const headerIndex = lines.findIndex((line) => line.startsWith("# "));

    if (headerIndex !== -1) {
      lines.splice(headerIndex, 1);
    }

    return lines.join("\n");
  }

  // #endregion

  // #region Rendering

  /**
   * Returns the HTML to render the Markdown Document, sanitized for safe display in a browser.
   */
  async render(excludeHeader: boolean = false): Promise<string> {
    let content = this.content;

    if (excludeHeader) {
      content = this.removeMarkdownTitle();
    }

    const parsedHtml = await new Marked()
      .use(markedAlert())
      .parse(content, { breaks: true, gfm: true });

    return DOMPurify.sanitize(parsedHtml);
  }

  // #endregion
}
