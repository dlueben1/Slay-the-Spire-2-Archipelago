<script setup lang="ts">
import { ref } from "vue";
import type { WizardReviewSection } from "../../wizard/review";

const props = defineProps<{
  sections: WizardReviewSection[];
  yaml: string;
  playerName: string;
}>();

// Tooltip feedback confirms clipboard success without adding persistent page height.
const copyFeedback = ref("Copy YAML");

/**
 * Copies text through a temporary selection when the Clipboard API is unavailable.
 *
 * @param text - Complete YAML document to place on the operating-system clipboard.
 * @returns Whether the browser reported a successful legacy copy operation.
 * @remarks This fallback supports non-secure local deployments where Clipboard may be absent.
 */
function copyWithLegacySelection(text: string): boolean {
  // Create an off-screen control whose selected value can be copied synchronously.
  const temporaryInput = document.createElement("textarea");
  temporaryInput.value = text;
  temporaryInput.style.position = "fixed";
  temporaryInput.style.opacity = "0";
  document.body.appendChild(temporaryInput);
  temporaryInput.focus();
  temporaryInput.select();

  try {
    // Ask the browser to copy the active selection into the system clipboard.
    return document.execCommand("copy");
  } finally {
    // Always remove the temporary DOM node, including after browser refusal.
    temporaryInput.remove();
  }
}

/**
 * Copies the exact previewed YAML document to the clipboard.
 *
 * @returns A promise that settles after modern or fallback clipboard handling finishes.
 * @remarks Failures are surfaced in the button tooltip without changing review layout.
 */
async function copyYaml(): Promise<void> {
  // Reset stale feedback while the asynchronous browser operation is pending.
  copyFeedback.value = "Copying YAML";

  try {
    if (navigator.clipboard) {
      // Prefer the permission-aware modern clipboard implementation.
      await navigator.clipboard.writeText(props.yaml);
    } else if (!copyWithLegacySelection(props.yaml)) {
      // Treat an explicit fallback refusal as a failed copy operation.
      throw new Error("The browser refused clipboard access.");
    }

    // Confirm success through the existing icon-only control's tooltip.
    copyFeedback.value = "YAML copied";
  } catch {
    // Keep the failure local to the action without replacing validated YAML output.
    copyFeedback.value = "Copy failed";
  }
}

/**
 * Creates a filesystem-safe YAML filename from the current player name.
 *
 * @returns A non-empty filename ending in `.yaml`.
 * @remarks This changes download metadata only; the player name inside YAML is untouched.
 */
function getDownloadFilename(): string {
  // Replace Windows-reserved filename punctuation and compact whitespace for portability.
  const safeBaseName = props.playerName
    .trim()
    .replace(/[<>:"/\\|?*]/g, "-")
    .replace(/\s+/g, "-")
    .replace(/\.+$/g, "");

  // Retain a stable fallback even if a future caller bypasses player-name validation.
  return `${safeBaseName || "slay-the-spire-2-player"}.yaml`;
}

/**
 * Downloads the exact previewed YAML document as a local file.
 *
 * @returns Nothing; creates and immediately cleans up a temporary object URL and link.
 */
function downloadYaml(): void {
  // Create a YAML-typed browser object without mutating the previewed text.
  const yamlBlob = new Blob([props.yaml], {
    type: "application/yaml;charset=utf-8",
  });
  const objectUrl = URL.createObjectURL(yamlBlob);
  const downloadLink = document.createElement("a");
  downloadLink.href = objectUrl;
  downloadLink.download = getDownloadFilename();

  // Mounting the link keeps programmatic downloads reliable across browsers.
  document.body.appendChild(downloadLink);
  downloadLink.click();
  downloadLink.remove();

  // Release the in-memory YAML object after the browser has accepted the click.
  URL.revokeObjectURL(objectUrl);
}
</script>

<template>
  <div class="space-y-6">
    <section>
      <h2 class="review-heading">Summary</h2>

      <dl class="review-sections">
        <div v-for="section in sections" :key="section.title">
          <dt>{{ section.title }}</dt>
          <dd>{{ section.summary }}</dd>
        </div>
      </dl>
    </section>

    <section>
      <div class="review-heading-row">
        <h2 class="review-heading">YAML Preview</h2>

        <div class="review-actions">
          <UTooltip :text="copyFeedback">
            <UButton
              icon="i-glyphs-copy-bold"
              square
              size="xs"
              color="neutral"
              variant="soft"
              class="cursor-pointer"
              aria-label="Copy YAML"
              :disabled="!yaml"
              @click="copyYaml"
            />
          </UTooltip>

          <UTooltip text="Download YAML">
            <UButton
              icon="i-glyphs-save-bold"
              square
              size="xs"
              color="neutral"
              variant="soft"
              class="cursor-pointer"
              aria-label="Download YAML"
              :disabled="!yaml"
              @click="downloadYaml"
            />
          </UTooltip>
        </div>
      </div>

      <p v-if="!yaml" class="yaml-output yaml-output--empty">
        Enter a valid player name to restore the YAML preview.
      </p>
      <pre v-else class="yaml-output"><code>{{ yaml }}</code></pre>
    </section>
  </div>
</template>

<style scoped>
.review-heading {
  margin-bottom: 0.65rem;
  color: white;
  font-size: 1.1rem;
  font-weight: 700;
}

.review-heading-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 0.65rem;
}

.review-heading-row .review-heading {
  margin-bottom: 0;
}

.review-actions {
  display: flex;
  flex: none;
  gap: 0.4rem;
}

.review-sections {
  display: grid;
  gap: 1rem;
}

.review-sections > div {
  padding-left: 0.85rem;
  border-left: 2px solid
    color-mix(in oklab, var(--color-amber-500) 55%, transparent);
}

.review-sections dt {
  color: var(--color-amber-300);
  font-size: 0.8rem;
  font-weight: 800;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}

.review-sections dd {
  margin-top: 0.25rem;
  color: var(--ui-text);
  line-height: 1.6;
}

.yaml-output {
  overflow-x: auto;
  padding: 1rem;
  color: var(--color-amber-200);
  border: 1px solid var(--ui-border);
  border-radius: 0.6rem;
  background: rgba(0, 0, 0, 0.35);
}

.yaml-output--empty {
  color: var(--ui-text-muted);
}
</style>
