<script setup lang="ts">
import type {
  CharacterStarterOverrides,
  StartingEquipmentAnswers,
} from "../../../wizard/WizardAnswers";

const props = defineProps<{
  modelValue: CharacterStarterOverrides;
  globalSettings: StartingEquipmentAnswers;
  disabled: boolean;
}>();
const emit = defineEmits<{
  "update:modelValue": [value: CharacterStarterOverrides];
}>();
const fields = [
  { key: "progressiveStarterCard", label: "Progressive Starter Card" },
  { key: "progressiveStarterRelic", label: "Progressive Starter Relic" },
] as const;

function update(field: keyof CharacterStarterOverrides, value: unknown): void {
  if (value !== "inherit" && value !== "enabled" && value !== "disabled")
    return;
  emit("update:modelValue", { ...props.modelValue, [field]: value });
}
</script>

<template>
  <fieldset class="mt-4" :disabled="disabled">
    <legend class="font-bold text-highlighted">
      Per-character starter overrides
    </legend>
    <p class="mt-2 text-sm text-muted">
      Use global setting follows Checks &amp; Rewards. Enabled or Disabled
      applies only to this character.
    </p>
    <p v-if="disabled" class="mt-2 text-sm text-muted">
      Enable Floor Checks under Checks &amp; Rewards to use progressive
      starters. Saved overrides have no effect while Floor Checks are off.
    </p>
    <div class="mt-3 grid gap-3 sm:grid-cols-2">
      <label
        v-for="field in fields"
        :key="field.key"
        class="grid gap-2 text-sm"
      >
        {{ field.label }}
        <USelect
          :model-value="modelValue[field.key] ?? 'inherit'"
          :items="[
            {
              label: `Use global setting (${globalSettings[field.key] ? 'Enabled' : 'Disabled'})`,
              value: 'inherit',
            },
            { label: 'Enabled', value: 'enabled' },
            { label: 'Disabled', value: 'disabled' },
          ]"
          :disabled="disabled"
          :aria-label="field.label"
          :content="{ bodyLock: false }"
          class="w-full cursor-pointer"
          @update:model-value="update(field.key, $event)"
        />
      </label>
    </div>
  </fieldset>
</template>
