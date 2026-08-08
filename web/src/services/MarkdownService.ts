import { MarkdownDocument } from "../models/MarkdownDocument";

/**
 * Fetches and builds a Markdown Document from a given path.
 * @param loc - The location of the Markdown file, as a relative path from the public directory.
 * @returns A MarkdownDocument object
 */
export async function getMarkdownDocument(
  loc: string,
): Promise<MarkdownDocument> {
  // Fetch the Markdown file from the public directory
  const response = await fetch(loc);

  // Grab the raw text content
  const text = await response.text();

  // Build and return the MarkdownDocument
  return new MarkdownDocument(text);
}
