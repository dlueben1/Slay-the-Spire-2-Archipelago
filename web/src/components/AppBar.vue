<script setup lang="ts">
import { computed } from "vue";
import { useRoute } from "vue-router";
import type { NavigationMenuItem } from "@nuxt/ui";

const route = useRoute();

/**
 * Builds the canonical navigation model shared by desktop and mobile menus.
 *
 * @returns Navigation links with active state derived from the current route.
 * @remarks Add, remove, or rename application links only in this function. Both
 * responsive presentations render this same model, so they cannot drift apart.
 */
function getNavigationItems(): NavigationMenuItem[] {
  // Recompute route-dependent active and expanded state after every navigation.
  return [
    {
      label: "Home",
      to: "/",
      active: route.path === "/",
    },
    {
      label: "Setup",
      active: route.path.startsWith("/setup"),
      defaultOpen: route.path.startsWith("/setup"),
      class: "text-amber-500",
      children: [
        {
          icon: "i-glyphs-signal-tower-bold",
          label: "Hosting a Game",
          description:
            "Guide for hosting a game where one or more players are playing Slay the Spire II.",
          to: "/setup/host",
          active: route.path === "/setup/host",
        },
        {
          icon: "i-glyphs-gamepad-bold",
          label: "Joining a Game",
          description:
            "Guide for connecting Slay the Spire II to an Archipelago Multiworld game.",
          to: "/setup/client",
          active: route.path === "/setup/client",
        },
      ],
    },
    {
      label: "YAML Builder",
      to: "/builder",
      active: route.path.startsWith("/builder"),
    },
    // {
    //   label: "Help",
    //   to: "/help",
    //   active: route.path.startsWith("/help"),
    // },
  ];
}

const navigationItems = computed(getNavigationItems);
</script>

<template>
  <UHeader>
    <template #title>
      <img src="/logo.webp" alt="Slay the Spire 2 Archipelago" class="h-14" />
    </template>

    <UNavigationMenu
      :items="navigationItems"
      color="primary"
      content-orientation="vertical"
      :ui="{
        childLinkLabel: 'font-medium',
        childLinkDescription: 'text-xs text-muted',
      }"
    />

    <template #right>
      <UTooltip text="Join our Discord Thread">
        <UButton
          color="neutral"
          variant="ghost"
          to="https://discord.com/channels/731205301247803413/1524232611654799540/1524238637980713062"
          target="_blank"
          icon="i-simple-icons-discord"
          aria-label="Discord"
        />
      </UTooltip>

      <UTooltip text="Visit our GitHub">
        <UButton
          color="neutral"
          variant="ghost"
          to="https://github.com/dlueben1/Slay-the-Spire-2-Archipelago"
          target="_blank"
          icon="i-simple-icons-github"
          aria-label="GitHub"
        />
      </UTooltip>

      <UTooltip text="Get it from Steam Workshop">
        <UButton
          color="neutral"
          variant="ghost"
          to="https://steamcommunity.com/sharedfiles/filedetails/?id=3748826296"
          target="_blank"
          icon="i-simple-icons-steam"
          aria-label="Steam Workshop"
        />
      </UTooltip>
    </template>

    <template #body>
      <UNavigationMenu
        :items="navigationItems"
        orientation="vertical"
        color="primary"
        class="w-full"
        :ui="{
          link: 'cursor-pointer',
          childLink: 'cursor-pointer',
          childLinkLabel: 'font-medium',
          childLinkDescription: 'text-xs text-muted',
        }"
      />
    </template>
  </UHeader>
</template>
