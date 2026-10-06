<template>
  <button type="button" class="ghost task-icon-button" :title="actionLabel" :aria-label="actionLabel" :aria-pressed="maximized">
    <svg viewBox="0 0 16 16" width="12" height="12" fill="none" stroke="currentColor" stroke-width="1" aria-hidden="true">
      <path v-if="maximized" d="M2 7h7v7H2z M6 7V2h8v8H9" />
      <path v-else d="M2 6V2h4 M10 2h4v4 M14 10v4h-4 M6 14H2v-4" />
    </svg>
  </button>
</template>

<script setup>
import { computed } from 'vue';
const props = defineProps({ label: { type: String, required: true }, maximized: Boolean });
const actionLabel = computed(() => props.maximized ? `Restore ${props.label} column` : `Maximize ${props.label} column`);
</script>
