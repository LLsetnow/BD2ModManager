<script setup lang="ts">
defineProps<{ modelValue: boolean; label: string; disabled?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>()
</script>

<template>
  <button type="button" role="switch" :aria-checked="modelValue" :aria-label="label" :disabled="disabled"
    class="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full border border-border-default transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
    :class="modelValue ? 'bg-accent' : 'bg-surface-input'" @click="emit('update:modelValue', !modelValue)">
    <span class="block h-4 w-4 rounded-full transition-transform" :class="modelValue ? 'translate-x-6 bg-text-on-accent' : 'translate-x-1 bg-text-secondary'" />
  </button>
</template>
