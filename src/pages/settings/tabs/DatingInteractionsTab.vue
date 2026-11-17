<script setup lang="ts">
import { TabPanel } from '@headlessui/vue'
import { RefreshCw, Save } from '@lucide/vue'
import { storeToRefs } from 'pinia'
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useDatingInteractionsStore } from '../../../stores/datingInteractions'

const { t } = useI18n()
const store = useDatingInteractionsStore()
const { settings, busy, dirty, error, message } = storeToRefs(store)
onMounted(() => store.run(store.ensureLoaded))
</script>

<template>
  <TabPanel class="space-y-5 text-text-primary">
    <div class="flex items-center justify-between gap-4">
      <div><h2 class="text-lg font-semibold">{{ t('settingsTab.dating.title') }}</h2><p class="text-sm text-text-secondary mt-1">{{ t('settingsTab.dating.description') }}</p></div>
      <button class="action" :disabled="busy || dirty" @click="store.run(store.load)"><RefreshCw class="w-4 h-4" />{{ t('common.actions.refresh') }}</button>
    </div>
    <p v-if="error" role="alert" class="rounded-lg border border-red-400/40 bg-red-500/10 text-red-400 p-3 text-sm">{{ error }}</p>
    <p v-if="message" role="status" class="rounded-lg border border-border-subtle bg-accent-muted p-3 text-sm">{{ t(message) }}</p>
    <template v-if="settings">
      <section class="panel flex flex-wrap gap-8 text-sm">
        <label class="flex gap-2 items-center">{{ t('settingsTab.dating.dragHold') }}<input v-model.number="settings.dragHoldSeconds" class="field w-20" type="number" min="0.15" max="1.5" step="0.05" :disabled="busy" @input="dirty = true" />{{ t('common.seconds') }}</label>
        <label class="flex gap-2 items-center">{{ t('settingsTab.dating.opacity') }}<input v-model.number="settings.markerOpacity" type="range" min="0.08" max="0.6" step="0.01" :disabled="busy" @input="dirty = true" />{{ Math.round(settings.markerOpacity * 100) }}%</label>
      </section>
      <div class="flex flex-wrap items-center justify-between gap-3"><p class="text-xs text-text-secondary">{{ t('settingsTab.dating.draftHelp') }}</p><button class="action bg-accent! text-text-on-accent!" :disabled="busy || !dirty" @click="store.run(store.save)"><Save class="w-4 h-4" />{{ t('common.actions.saveSettings') }}{{ dirty ? ' *' : '' }}</button></div>
    </template>
  </TabPanel>
</template>

<style scoped>
@reference '../../../styles/main.css';
.panel { @apply rounded-xl border border-border-subtle bg-surface-panel p-4; }
.action { @apply inline-flex shrink-0 items-center justify-center gap-2 rounded-md border border-border-subtle px-3 py-2 text-sm cursor-pointer hover:bg-state-hover disabled:opacity-40 disabled:cursor-default; }
.field { @apply rounded-md border border-border-subtle bg-surface-panel px-3 py-2 text-sm; }
</style>
