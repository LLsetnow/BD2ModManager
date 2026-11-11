<script setup lang="ts">
import { computed, ref, useTemplateRef } from 'vue'
import { Combobox, ComboboxButton, ComboboxInput, ComboboxOption, ComboboxOptions } from '@headlessui/vue'
import { Check, ChevronDown } from '@lucide/vue'
import { autoUpdate, flip, offset, shift, size, useFloating } from '@floating-ui/vue'

type Option = { value: string; label: string }
const props = defineProps<{
  id: string
  modelValue: string
  options: Option[]
  label: string
  disabled?: boolean
}>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
const query = ref('')
const reference = useTemplateRef<HTMLElement>('reference')
const floating = useTemplateRef<HTMLElement>('floating')
const { floatingStyles } = useFloating(reference, floating, {
  placement: 'bottom-start', strategy: 'fixed', whileElementsMounted: autoUpdate,
  middleware: [offset(4), flip(), shift({ padding: 8 }), size({ padding: 8,
    apply({ rects, availableHeight, elements }) {
      Object.assign(elements.floating.style, { width: `${rects.reference.width}px`, maxHeight: `${Math.min(240, availableHeight)}px` })
    },
  })],
})
const filtered = computed(() => {
  const text = query.value.trim().toLocaleLowerCase()
  return props.options.filter(o => `${o.value} ${o.label}`.toLocaleLowerCase().includes(text))
})
function display(value: unknown) {
  if (typeof value !== 'string') return ''
  return props.options.find(o => o.value === value)?.label ?? value
}
function input(event: Event) {
  query.value = (event.target as HTMLInputElement).value
  const text = query.value.trim()
  const match = props.options.find(o => o.value === text || o.label === text)
  emit('update:modelValue', match?.value ?? text)
}
function select(value: unknown) {
  query.value = ''
  emit('update:modelValue', typeof value === 'string' ? value : '')
}
</script>

<template>
  <Combobox :model-value="modelValue" :disabled="disabled" nullable @update:model-value="select">
    <div ref="reference" class="relative">
      <ComboboxInput :id="id" :display-value="display" autocomplete="off"
        :placeholder="`输入${label} ID / 名称，或从列表选择`"
        class="w-full rounded-md border border-border-subtle bg-surface-panel px-3 py-2 pr-9 text-sm text-text-primary disabled:opacity-40"
        @change="input" />
      <ComboboxButton class="absolute inset-y-0 right-0 flex w-9 items-center justify-center text-text-secondary cursor-pointer disabled:cursor-not-allowed"
        :aria-label="`选择${label}`" @click="query = ''">
        <ChevronDown class="w-4 h-4" />
      </ComboboxButton>
    </div>
    <Teleport to="body">
      <ComboboxOptions as="template">
        <ul ref="floating" :style="floatingStyles"
          class="z-9998 overflow-y-auto rounded-md border border-border-default bg-surface-popover py-1 text-sm text-text-primary shadow-lg focus:outline-none">
          <ComboboxOption v-for="option in filtered" :key="option.value" :value="option.value" as="template" v-slot="{ active, selected }">
            <li class="relative cursor-pointer select-none py-2 pl-8 pr-3" :class="{ 'bg-state-hover': active }">
              <Check v-if="selected" class="absolute left-2 top-2.5 w-4 h-4 text-accent" />
              {{ option.label }}
            </li>
          </ComboboxOption>
          <li v-if="!filtered.length" class="px-3 py-2 text-text-secondary">未找到匹配项，请输入列表中的 ID 或名称。</li>
        </ul>
      </ComboboxOptions>
    </Teleport>
  </Combobox>
</template>
