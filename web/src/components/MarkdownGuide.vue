<script setup lang="ts">
import { onMounted, ref } from "vue";
import type { MarkdownDocument } from "../models/MarkdownDocument";
import { getMarkdownDocument } from "../services/MarkdownService";

/**
 * A guide page made of Markdown documents, shown as one card per document.
 *
 * Each document's first heading becomes its card title. A document without a
 * heading is shown without a card around it.
 */
const props = defineProps<{
  /** Paths of the documents to show, in order, relative to the public directory. */
  sources: string[];
}>();

/**
 * The loaded documents with their Markdown already rendered to HTML, so the
 * template can show them as soon as they arrive.
 */
const documents = ref<Array<{ document: MarkdownDocument; content: string }>>(
  [],
);

// Load after mounting, when the browser's `fetch` is available.
onMounted(async () => {
  documents.value = await Promise.all(
    props.sources.map(async (path) => {
      const document = await getMarkdownDocument(path);

      return {
        document,
        content: await document.render(true),
      };
    }),
  );
});
</script>

<template>
  <div
    class="flex flex-col w-full items-center justify-center gap-8 px-12 pt-10"
  >
    <UPageCard
      v-for="({ document, content }, index) in documents"
      :key="sources[index]"
      :title="document.header ?? ``"
      class="max-w-4xl"
      :variant="document.header === null ? 'naked' : 'subtle'"
    >
      <template #description>
        <div class="markdown-content block prose" v-html="content" />
      </template>
    </UPageCard>
  </div>
</template>
