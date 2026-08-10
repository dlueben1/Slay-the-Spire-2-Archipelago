<script setup lang="ts">
import { reactive } from "vue";

const items = reactive([
  {
    title: "What is the Slay the Spire 2 Archipelago Mod?",
    description:
      "The Slay the Spire 2 Archipelago mod connects the game to Archipelago, a multi-game randomizer where players exchange items and progression across different games. As you complete challenges during your runs, you’ll unlock rewards for other players—and they’ll send you the cards, relics, upgrades, and other tools you need to conquer the Spire.\n\nIf you're unfamiliar with Archipelago, check out this link: https://archipelago.gg/",
    icon: "i-glyphs-info-circle-bold",
  },
  {
    title: "Installation",
    description:
      "The simplest way to install the mod is to get it from the Steam Workshop. Make sure you also install RitsuLib. Any mods should be used at your own risk - while we support modded characters in general, some modded characters may have conflicts with ours, and non-character mods are especially risky.\n\nFor more details instructions, see the Setup section.",
    icon: "i-glyphs-arrow-solid-bracket-end-bold",
  },
  {
    title: "Does it work on the Beta Branch?",
    description:
      "The Archipelago mod has experimental/tempermental support for the beta branch of Slay the Spire 2 (generously maintained by Terairk). However, the beta branch updates frequently, so updates may break the mod before we can patch it.\n\nWith that said, our official recommendation to the average player is to use the public branch (the version that comes installed by default) to play with others.",
    icon: "i-glyphs-note-pad-bold",
  },
]);

/**
 * Formats a description string by converting URLs into clickable links.
 * @param text - The description text to format.
 */
function formatDescription(text: string): string {
  const urlRegex = /(https?:\/\/[^\s]+)/g;
  return text.replace(
    urlRegex,
    '<a href="$1" target="_blank" rel="noopener noreferrer">$1</a>',
  );
}
</script>

<template>
  <UPageBody>
    <!-- Logo -->
    <div class="flex flex-col items-center justify-center gap-4">
      <img src="/logo.webp" alt="Slay the Spire 2 Archipelago" class="h-64" />
    </div>

    <!-- Dashboard Cards -->
    <div class="flex flex-col w-full items-center justify-center gap-8 px-12">
      <UCard
        v-for="item in items"
        :key="item.title"
        variant="subtle"
        class="max-w-4xl"
      >
        <template #title>
          <div class="flex items-center gap-2">
            <UIcon :name="item.icon" class="w-6 h-6" />
            <span>{{ item.title }}</span>
          </div>
        </template>
        <p
          class="whitespace-pre-line"
          v-html="formatDescription(item.description)"
        />
      </UCard>
    </div>
  </UPageBody>
</template>
