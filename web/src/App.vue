<script setup lang="ts">
import AppBar from "./components/AppBar.vue";
</script>

<template>
  <UApp>
    <!-- Nav Bar / App Bar -->
    <AppBar />

    <!-- Content -->
    <RouterView class="mb-4" />
  </UApp>
</template>
