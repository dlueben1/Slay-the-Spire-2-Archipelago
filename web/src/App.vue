<script setup lang="ts">
import AppBar from "./components/AppBar.vue";
import AppFooter from "./components/AppFooter.vue";
</script>

<template>
  <UApp>
    <!-- Nav Bar / App Bar -->
    <AppBar />

    <!-- Content -->
    <RouterView class="mb-4" />

    <AppFooter />
  </UApp>
</template>
