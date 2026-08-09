<script setup lang="ts">
import { onMounted, ref } from "vue";
import type { MarkdownDocument } from "../../models/MarkdownDocument";
import { getMarkdownDocument } from "../../services/MarkdownService";

const props = defineProps<{
  source: string;
  fallbackTitle: string;
}>();

const document = ref<MarkdownDocument | null>(null);
const renderedContent = ref("");
const loadError = ref("");

/**
 * Fetches, sanitizes, and stores the configured public Markdown document.
 *
 * @returns A promise that resolves after rendered content or an error is stored.
 * @remarks Rendering delegates to the same MarkdownDocument pipeline used by the
 * setup guides, including marked-alert support and DOMPurify sanitization.
 */
async function loadMarkdownDocument(): Promise<void> {
  try {
    // Load the public document through the application's shared Markdown service.
    const loadedDocument = await getMarkdownDocument(props.source);

    // Render without the first heading because the card displays it as its title.
    document.value = loadedDocument;
    renderedContent.value = await loadedDocument.render(true);
    loadError.value = "";
  } catch (cause) {
    // Normalize an unknown fetch or rendering failure for an inline help message.
    loadError.value =
      cause instanceof Error
        ? cause.message
        : "The help document could not be loaded.";
  }
}

// Load static help only after the browser mounts and `fetch` is available.
onMounted(loadMarkdownDocument);
</script>

<template>
  <UCard
    :ui="{
      root: 'bg-black/20 ring-1 ring-amber-500/15',
      header: 'py-3',
      body: 'py-3',
    }"
  >
    <template #header>
      <div class="flex items-center gap-2 text-sm font-bold text-amber-200">
        <UIcon name="i-glyphs-info-circle-bold" class="size-5" />
        <span>{{ document?.header ?? fallbackTitle }}</span>
      </div>
    </template>

    <p v-if="loadError" class="wizard-error">{{ loadError }}</p>
    <div
      v-else-if="renderedContent"
      class="markdown-content block text-sm text-muted"
      v-html="renderedContent"
    />
    <p v-else class="text-sm text-muted">Loading instructions…</p>
  </UCard>
</template>

<style scoped src="./wizard.css"></style>
