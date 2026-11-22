<script setup lang="ts">
import MarkdownGuide from "../components/MarkdownGuide.vue";

const markdownPaths = ["/docs/setup-intro-client.md", "/docs/setup-client.md"];
</script>

<template>
  <MarkdownGuide :sources="markdownPaths" />
</template>
