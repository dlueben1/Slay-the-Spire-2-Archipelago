<script setup lang="ts">
/**
 * Modal shared by the Bonus Item dialogs: Add or Edit title, Cancel and Add or Save.
 *
 * The dialog supplies the body and decides what Add or Save commits; this shell only
 * emits `submit` when the button is pressed.
 */
defineProps<{
  open: boolean;
  /** Item type named in the title, such as "Wax Relic". */
  itemName: string;
  description: string;
  /** Editing an existing row rather than adding one. */
  editing: boolean;
  submittable: boolean;
  /** A short body: no scrolling and the default modal width. */
  compact?: boolean;
}>();

const emit = defineEmits<{
  "update:open": [value: boolean];
  submit: [];
}>();
</script>

<template>
  <UModal
    :open="open"
    :title="`${editing ? 'Edit' : 'Add'} ${itemName}`"
    :description="description"
    :scrollable="!compact"
    :ui="
      compact
        ? { footer: 'justify-end' }
        : { content: 'sm:max-w-2xl', footer: 'justify-end' }
    "
    @update:open="emit('update:open', $event)"
  >
    <template #body>
      <slot />
    </template>

    <template #footer>
      <UButton
        label="Cancel"
        color="neutral"
        variant="outline"
        class="cursor-pointer"
        @click="emit('update:open', false)"
      />
      <UButton
        :label="editing ? 'Save' : 'Add'"
        color="primary"
        class="cursor-pointer"
        :disabled="!submittable"
        @click="emit('submit')"
      />
    </template>
  </UModal>
</template>
