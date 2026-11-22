<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { Heart, RefreshCw, Save, Download, Search, SlidersHorizontal } from '@lucide/vue'
import { convertFileSrc } from '@tauri-apps/api/core'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import { useDatingInteractionsStore, type DatingScene } from '../../stores/datingInteractions'
import { useModsStore, type BD2Mod, type BD2ModExtended } from '../../stores/mods'
import { useCharactersStore, type Character } from '../../stores/characters'
import { useNotificationStore } from '../../stores/notification'
import { useModSync } from '../../composables/useModSync'
import { getErrorMessage } from '../../utils/errors'
import { formatCharName, useLang } from '../../utils/formatCharName'
import Image from '../../components/common/Image.vue'
import ToggleSwitch from '../../components/common/ToggleSwitch.vue'
import CharacterModal from '../characters/modals/CharacterModal.vue'

const store = useDatingInteractionsStore()
const modsStore = useModsStore()
const charactersStore = useCharactersStore()
const notificationStore = useNotificationStore()
const { syncMods } = useModSync()
const { t, locale } = useI18n()
const lang = useLang()
const { status, settings, busy, message, error, dirty } = storeToRefs(store)
const { load, ensureLoaded, run, save, install } = store
const search = ref('')
const selectedCostume = ref<Character | null>(null)
const showCostumeModal = ref(false)
const modsBusy = ref(false)
const modsError = ref('')
const modControlsBusy = computed(() => modsBusy.value || modsStore.isSyncing)
const modsByScene = computed(() => {
  const grouped = new Map<string, BD2ModExtended[]>()
  for (const mod of modsStore.extendedMods) {
    if (mod.modType?.type !== 'Dating' || !/^\d+$/.test(mod.modType.id)) continue
    const sceneId = `illust_dating${Number(mod.modType.id)}`
    const entries = grouped.get(sceneId) ?? []
    entries.push(mod)
    grouped.set(sceneId, entries)
  }
  for (const entries of grouped.values()) {
    entries.sort((a, b) => (a.displayName || a.name).localeCompare(b.displayName || b.name, locale.value.replace(/_/g, '-')))
  }
  return grouped
})
function sceneMods(sceneId: string) { return modsByScene.value.get(sceneId) ?? [] }
function enabledModsCount(sceneId: string) { return sceneMods(sceneId).filter(mod => mod.enabled).length }
function configureMods(scene: DatingScene) {
  modsError.value = ''
  selectedCostume.value = charactersStore.getCharacterByDatingId(scene.sceneId.replace('illust_dating', ''))
  if (!selectedCostume.value) { modsError.value = t('interactionsTab.characterMissing'); return }
  showCostumeModal.value = true
}
async function runModAction(action: () => Promise<unknown>) {
  if (modControlsBusy.value) return
  modsBusy.value = true; modsError.value = ''
  try { await action() } catch (cause) {
    modsError.value = getErrorMessage(t, cause)
    notificationStore.add({ type: 'error', title: t('interactionsTab.modActionFailed'), message: modsError.value })
  } finally { modsBusy.value = false }
}
async function toggleMod(mod: BD2Mod) {
  const enabled = !mod.enabled
  if (enabled && mod.errors.some(value => value !== 'UnmanagedButInStaging')) return
  await runModAction(() => enabled ? modsStore.enableMods([mod.name]) : modsStore.disableMods([mod.name]))
}
async function syncDatingMods() {
  if (modControlsBusy.value) return
  showCostumeModal.value = false
  // Close the character dialog before the existing sync flow opens conflicts.
  await nextTick()
  await runModAction(() => syncMods())
}
const visibleScenes = computed(() => settings.value?.scenes.filter(s => `${sceneTitle(s)} ${s.title} ${s.sceneId}`.toLowerCase().includes(search.value.trim().toLowerCase())) ?? [])
const nativeCount = computed(() => settings.value?.scenes.filter(s => s.nativeEnabled).length ?? 0)
const pCount = computed(() => settings.value?.scenes.filter(s => s.enabled).length ?? 0)
function sceneTitle(scene: DatingScene) {
  const character = charactersStore.getCharacterByDatingId(scene.sceneId.replace('illust_dating', ''))
  return character ? formatCharName(character, lang.value) : scene.title
}
function exportState(id: string) { return status.value?.available.find(a => a.sceneId === id)?.exportStatus ?? 'missing' }
function portrait(scene: DatingScene) { return convertFileSrc(`standing/${String(scene.costumeId).padStart(6, '0')}`, 'bd2assets') }
function setAll(mode: 'enabled' | 'nativeEnabled', value: boolean) {
  settings.value?.scenes.forEach(scene => { scene[mode] = value }); dirty.value = true
}
onMounted(() => run(ensureLoaded))
</script>

<template>
  <div class="h-full overflow-y-auto text-text-primary p-6 space-y-5">
    <div class="flex items-center justify-between gap-4">
      <div><h1 class="text-xl font-semibold flex items-center gap-2"><Heart class="w-5 h-5" />{{ t('interactionsTab.title') }}</h1><p class="text-sm text-text-secondary mt-1">{{ t('interactionsTab.description') }}</p></div>
      <button class="action" :disabled="busy || dirty" @click="run(load)"><RefreshCw class="w-4 h-4" />{{ t('common.actions.refresh') }}</button>
    </div>
    <p v-if="error" role="alert" class="rounded-lg border border-red-400/40 bg-red-500/10 text-red-400 p-3 text-sm">{{ error }}</p>
    <p v-if="modsError" role="alert" class="rounded-lg border border-red-400/40 bg-red-500/10 text-red-400 p-3 text-sm">{{ modsError }}</p>
    <p v-if="message" role="status" class="rounded-lg border border-border-subtle bg-accent-muted p-3 text-sm">{{ t(message) }}</p>
    <template v-if="settings && status">
      <section class="panel flex items-center justify-between gap-4">
        <div><h2 class="font-semibold">{{ t('interactionsTab.management') }}</h2><p class="text-sm text-text-secondary mt-1">{{ t('interactionsTab.managementHelp') }}</p></div>
        <ToggleSwitch v-model="settings.enabled" :label="t('interactionsTab.managementToggle')" :disabled="busy" @update:model-value="dirty = true" />
      </section>
      <div class="grid grid-cols-1 xl:grid-cols-2 gap-4">
        <section class="panel space-y-3">
          <div class="flex items-start justify-between gap-3"><div><h2 class="font-semibold">{{ t('interactionsTab.native') }}</h2><p class="text-xs text-text-secondary mt-1">{{ t('interactionsTab.configured', { count: nativeCount, total: settings.scenes.length }) }}</p></div><ToggleSwitch v-model="settings.nativeEntriesEnabled" :label="t('interactionsTab.nativeToggle')" :disabled="busy" @update:model-value="dirty = true" /></div>
          <p class="text-sm text-text-secondary">{{ t('interactionsTab.nativeHelp') }}</p>
          <div class="flex gap-2"><button class="small-action" :disabled="busy" @click="setAll('nativeEnabled', true)">{{ t('common.actions.enableAll') }}</button><button class="small-action" :disabled="busy" @click="setAll('nativeEnabled', false)">{{ t('common.actions.disableAll') }}</button></div>
        </section>
        <section class="panel space-y-3">
          <div class="flex items-start justify-between gap-3"><div><h2 class="font-semibold">{{ t('interactionsTab.extra') }}</h2><p class="text-xs text-text-secondary mt-1">{{ t('interactionsTab.configured', { count: pCount, total: settings.scenes.length }) }}</p></div><ToggleSwitch v-model="settings.pEntriesEnabled" :label="t('interactionsTab.extraToggle')" :disabled="busy" @update:model-value="dirty = true" /></div>
          <p class="text-sm text-text-secondary">{{ t('interactionsTab.extraHelp') }}</p>
          <div class="flex gap-2"><button class="small-action" :disabled="busy" @click="setAll('enabled', true)">{{ t('common.actions.enableAll') }}</button><button class="small-action" :disabled="busy" @click="setAll('enabled', false)">{{ t('common.actions.disableAll') }}</button></div>
        </section>
      </div>
      <section class="panel flex items-center justify-between gap-4">
        <div><p class="font-medium">{{ t('interactionsTab.plugin', { version: status.pluginVersion }) }}</p><p class="text-sm text-text-secondary mt-1">{{ t(status.pluginCurrent ? 'common.plugin.current' : status.installed ? 'interactionsTab.pluginUpdate' : 'common.plugin.notInstalled') }} · {{ t('interactionsTab.exitGame') }}</p></div>
        <button class="action" :disabled="busy" @click="run(install)"><Download class="w-4 h-4" />{{ t(status.installed ? 'common.plugin.reinstall' : 'common.plugin.install') }}</button>
      </section>
      <section class="panel space-y-4">
        <div class="flex flex-wrap items-center justify-between gap-3"><h2 class="font-semibold">{{ t('interactionsTab.characters', { count: settings.scenes.length }) }}</h2><label class="flex items-center gap-2"><Search class="w-4 h-4 text-text-secondary" /><input v-model="search" class="field w-56 max-w-full" :placeholder="t('interactionsTab.search')" :aria-label="t('interactionsTab.searchLabel')" /></label></div>
        <p v-if="!settings.enabled" class="text-sm text-text-secondary">{{ t('interactionsTab.disabledHelp') }}</p>
        <div class="interaction-grid">
          <article v-for="scene in visibleScenes" :key="scene.sceneId" class="min-w-0 rounded-lg border border-border-subtle p-3 flex flex-col gap-3">
            <div class="flex items-start gap-3 min-w-0"><Image :src="portrait(scene)" :alt="sceneTitle(scene)" class="w-20 h-24 shrink-0 object-cover rounded-md bg-surface-panel" error-src="characters/standing/placeholder_character.png" /><div class="min-w-0 flex-1"><h3 class="text-sm font-medium break-words">{{ sceneTitle(scene) }}</h3><p class="text-xs mt-2" :class="exportState(scene.sceneId) === 'ready' ? 'text-green-400' : 'text-amber-400'">{{ t(`interactionsTab.exportStatus.${exportState(scene.sceneId)}`) }}</p></div></div>
            <div class="flex items-center justify-between gap-3 border-t border-border-subtle pt-3"><span class="text-sm">{{ t('interactionsTab.native') }}</span><ToggleSwitch v-model="scene.nativeEnabled" :label="t('interactionsTab.nativeFor', { name: sceneTitle(scene) })" :disabled="busy" @update:model-value="dirty = true" /></div>
            <div class="flex items-center justify-between gap-3"><span class="text-sm">{{ t('interactionsTab.extra') }}</span><ToggleSwitch v-model="scene.enabled" :label="t('interactionsTab.extraFor', { name: sceneTitle(scene) })" :disabled="busy" @update:model-value="dirty = true" /></div>
            <button class="action mt-auto justify-between!" :aria-label="t('interactionsTab.configureModsFor', { name: sceneTitle(scene) })" @click="configureMods(scene)"><span class="inline-flex items-center gap-2"><SlidersHorizontal class="w-4 h-4" />{{ t('interactionsTab.configureMods') }}</span><span class="text-xs text-text-secondary">{{ t('interactionsTab.modsEnabled', { count: enabledModsCount(scene.sceneId), total: sceneMods(scene.sceneId).length }) }}</span></button>
          </article>
        </div>
        <p v-if="!visibleScenes.length" class="py-5 text-center text-sm text-text-secondary">{{ t('interactionsTab.empty') }}</p>
      </section>
      <div class="sticky bottom-0 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border-subtle bg-surface-panel p-4"><p class="text-xs text-text-secondary">{{ t('interactionsTab.saveHelp') }}</p><div class="flex flex-wrap gap-2"><button class="action" :disabled="modControlsBusy || !modsStore.isSyncNeeded" @click="syncDatingMods"><RefreshCw class="w-4 h-4" :class="{ 'animate-spin': modsStore.isSyncing }" />{{ t(modsStore.isSyncing ? 'interactionsTab.syncing' : modsStore.isSyncNeeded ? 'interactionsTab.syncMods' : 'interactionsTab.synced') }}</button><button class="action bg-accent! text-text-on-accent!" :disabled="busy || !dirty" @click="run(save)"><Save class="w-4 h-4" />{{ t('common.actions.saveSettings') }}{{ dirty ? ' *' : '' }}</button></div></div>
    </template>
    <CharacterModal v-model:show="showCostumeModal" :selected-costume="selectedCostume" :toggle-mod="toggleMod" initial-tab="Dating" :busy="modsBusy" />
  </div>
</template>

<style scoped>
@reference '../../styles/main.css';
.panel { @apply rounded-xl border border-border-subtle bg-surface-panel p-4; }
.action { @apply inline-flex shrink-0 items-center justify-center gap-2 rounded-md border border-border-subtle px-3 py-2 text-sm cursor-pointer hover:bg-state-hover disabled:opacity-40 disabled:cursor-default; }
.small-action { @apply rounded-md border border-border-subtle px-2.5 py-1.5 text-xs cursor-pointer hover:bg-state-hover disabled:opacity-40; }
.field { @apply rounded-md border border-border-subtle bg-surface-panel px-3 py-2 text-sm; }
.interaction-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 19rem), 1fr)); gap: 0.75rem; }
</style>
