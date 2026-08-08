<script setup lang="ts">
/** Displays already-derived review prose and YAML without performing compilation. */
defineProps<{
  summary: string;
  yaml: string;
}>();
</script>

<template>
  <div class="space-y-6">
    <section>
      <h2 class="review-heading">Your setup</h2>
      <p class="leading-relaxed">{{ summary }}</p>
    </section>

    <section>
      <h2 class="review-heading">Generated character settings</h2>
      <pre class="yaml-output"><code>{{ yaml }}</code></pre>
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

.yaml-output {
  overflow-x: auto;
  padding: 1rem;
  color: var(--color-amber-200);
  border: 1px solid var(--ui-border);
  border-radius: 0.6rem;
  background: rgba(0, 0, 0, 0.35);
}
</style>
