<script setup lang="ts">
import { onMounted, ref } from "vue";
import type { MarkdownDocument } from "../models/MarkdownDocument";
import { getMarkdownDocument } from "../services/MarkdownService";

/**
 * The paths to the Markdown Documents we want to load for this file.
 */
const markdownPaths = ["/docs/setup-intro-client.md", "/docs/setup-client.md"];

/**
 * The actual Markdown Documents that we load from the paths above.
 * We store them in a ref so that we can reactively update the UI when they are loaded.
 * We also use `content` as a way to pre-render the Markdown content to HTML so that we can display it in the UI.
 */
const documents = ref<Array<{ document: MarkdownDocument; content: string }>>(
  [],
);

/**
 * Fetches the Markdown Documents when the page mounts
 */
onMounted(async () => {
  documents.value = await Promise.all(
    markdownPaths.map(async (path) => {
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
      :key="markdownPaths[index]"
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
