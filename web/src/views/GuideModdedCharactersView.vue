<script setup lang="ts">
import MarkdownGuide from "../components/MarkdownGuide.vue";

// The predefined list is a long reference table, so it goes last.
const markdownPaths = [
  "/docs/modded-characters-intro.md",
  "/docs/modded-characters.md",
  "/docs/modded-characters-troubleshooting.md",
  "/docs/predefined-characters.md",
];
</script>

<template>
  <MarkdownGuide :sources="markdownPaths" />
</template>
