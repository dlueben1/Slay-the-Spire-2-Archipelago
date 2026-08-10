<script setup lang="ts">
import { onMounted, ref } from "vue";
import type { MarkdownDocument } from "../../models/MarkdownDocument";
import { getMarkdownDocument } from "../../services/MarkdownService";

const props = withDefaults(
  defineProps<{
    source: string;
    fallbackTitle: string;
    startsOpen?: boolean;
  }>(),
  {
    startsOpen: false,
  },
);

const document = ref<MarkdownDocument | null>(null);
const renderedContent = ref("");
const loadError = ref("");
const isOpen = ref(props.startsOpen);

/**
 * Toggles visibility of the document's rendered Markdown content.
 *
 * @returns Nothing; reverses only the local disclosure state.
 * @remarks `startsOpen` supplies the initial state only. Later prop changes do not
 * overwrite a reader's deliberate expand or collapse action.
 */
function toggleDocument(): void {
  // Retain fetched content while changing only the presentation state.
  isOpen.value = !isOpen.value;
}

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
      root: 'bg-black/20 ring-1 ring-amber-500/15 divide-amber-500',
      header: 'p-0!',
      body: 'p-0!',
    }"
  >
    <template #header>
      <UButton
        type="button"
        color="neutral"
        variant="ghost"
        class="w-full cursor-pointer justify-between gap-3 rounded-none px-4 py-4 text-left transition-colors hover:bg-amber-500/10 focus-visible:bg-amber-500/10 sm:px-6"
        :aria-expanded="isOpen"
        :aria-label="isOpen ? 'Hide instructions' : 'Show instructions'"
        @click="toggleDocument"
      >
        <div
          class="flex min-w-0 items-center gap-2 text-sm font-bold text-amber-200"
        >
          <UIcon name="i-glyphs-info-circle-bold" class="size-5 shrink-0" />
          <span>{{ document?.header ?? fallbackTitle }}</span>
        </div>

        <UIcon
          name="i-glyphs-arrow-bold"
          class="size-4 shrink-0 transition-transform duration-200"
          :class="isOpen ? '' : 'rotate-180'"
        />
      </UButton>
    </template>

    <Transition name="wizard-markdown-document">
      <div v-if="isOpen" class="wizard-markdown-document__content">
        <p v-if="loadError" class="wizard-error">{{ loadError }}</p>
        <div
          v-else-if="renderedContent"
          class="markdown-content block text-sm text-muted"
          v-html="renderedContent"
        />
        <p v-else class="text-sm text-muted">Loading instructions…</p>
      </div>
    </Transition>
  </UCard>
</template>

<style scoped>
.wizard-markdown-document__content {
  overflow: hidden;
  padding: 1rem;
}

.wizard-markdown-document-enter-active,
.wizard-markdown-document-leave-active {
  overflow: hidden;
  transition:
    max-height 0.22s ease,
    padding 0.22s ease,
    opacity 0.16s ease,
    transform 0.22s ease;
}

.wizard-markdown-document-enter-from,
.wizard-markdown-document-leave-to {
  max-height: 0;
  padding-top: 0;
  padding-bottom: 0;
  opacity: 0;
  transform: translateY(-0.35rem);
}

.wizard-markdown-document-enter-to,
.wizard-markdown-document-leave-from {
  max-height: 80rem;
  opacity: 1;
  transform: translateY(0);
}

@media (prefers-reduced-motion: reduce) {
  .wizard-markdown-document-enter-active,
  .wizard-markdown-document-leave-active {
    transition: none;
  }
}
</style>
