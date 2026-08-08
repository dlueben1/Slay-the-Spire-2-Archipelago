<script setup lang="ts">
import DOMPurify from "dompurify";
import { Marked } from "marked";
import markedAlert from "marked-alert";
import { onMounted, ref } from "vue";

const html = ref("");

onMounted(async () => {
  const response = await fetch("/install_easy.md");
  const testMd = await response.text();
  const parsedHtml = await new Marked().use(markedAlert()).parse(testMd);
  html.value = DOMPurify.sanitize(parsedHtml);
});
</script>

<template>
  <div
    class="flex flex-col w-full items-center justify-center gap-8 px-12 pt-10"
  >
    <h1>
      Future Doug: Add an "Are you Hosting?" or "Are you playing?" option,
      display settings as necessary
    </h1>
    <UPageCard
      title="Easy Installation"
      icon="i-simple-icons-steam"
      class="max-w-4xl"
      variant="subtle"
    >
      <template #description>
        <div class="markdown-content block prose" v-html="html" />
      </template>
    </UPageCard>

    <UPageCard
      title="Manual Installation"
      description="Follow the instructions in the Setup section to get started with the Archipelago mod."
      icon="i-glyphs-arrow-solid-bracket-end-bold"
      class="max-w-4xl"
      variant="subtle"
    />
  </div>
</template>
