<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getMarkdownDocument } from "../services/MarkdownService";

const hostOnly = ref("");

onMounted(async () => {
  const doc = await getMarkdownDocument("/docs/setup-host.md");
  hostOnly.value = await doc.render();
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
      title="Hosting Only"
      icon="i-simple-icons-steam"
      class="max-w-4xl"
      variant="subtle"
    >
      <template #description>
        <div class="markdown-content block prose" v-html="hostOnly" />
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
