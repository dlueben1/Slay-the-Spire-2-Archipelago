<script setup lang="ts">
import MarkdownGuide from "../components/MarkdownGuide.vue";

const markdownPaths = [
  "/docs/setup-intro-host.md",
  "/docs/setup-generate.md",
  "/docs/setup-hosting.md",
  "/docs/setup-outro-host.md",
];
</script>

<template>
  <MarkdownGuide :sources="markdownPaths" />
</template>
