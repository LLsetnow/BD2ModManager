<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useCharactersStore } from '../../stores/characters'
import { formatCharName, useLang } from '../../utils/formatCharName'
import { FolderOpen, Heart, Plus, RefreshCw, Save, Trash2, Download } from '@lucide/vue'
import { convertFileSrc, invoke } from '@tauri-apps/api/core'
import { open } from '@tauri-apps/plugin-dialog'
import Image from '../../components/common/Image.vue'
import ToggleSwitch from '../../components/common/ToggleSwitch.vue'

const { t, locale } = useI18n()
const charactersStore = useCharactersStore()
const lang = useLang()
function sceneTitle(scene: Scene) {
  const character = charactersStore.getCharacterByDatingId(scene.sceneId.replace('illust_dating', ''))
  return character ? formatCharName(character, lang.value) : scene.title
}

type Scene = { sceneId: string; costumeId: number; avatarAddress: string; title: string; enabled: boolean; useMods: boolean }
type Settings = { schemaVersion: number; enabled: boolean; exportDirectory: string; audioDirectory: string; assetsDirectory: string; dragHoldSeconds: number; markerOpacity: number; scenes: Scene[] }
type Available = Scene & { exportStatus: 'ready' | 'missing' | 'expired' | 'invalid'; expiresAtMs: number | null }
type Status = { settings: Settings; available: Available[]; installed: boolean; pluginCurrent: boolean; pluginVersion: string; settingsPath: string }
const status = ref<Status>()
const settings = ref<Settings>()
const busy = ref(false)
const message = ref('')
const error = ref('')
const dirty = ref(false)
const addId = ref('')
const adding = computed(() => status.value?.available.filter(s => !settings.value?.scenes.some(e => e.sceneId === s.sceneId)) ?? [])
const paths = [
  { key: 'exportDirectory' },
  { key: 'audioDirectory' },
  { key: 'assetsDirectory' },
] as const

async function load() {
  const result = await invoke<Status>('get_local_interactions')
  status.value = result
  settings.value = structuredClone(result.settings)
  dirty.value = false
  addId.value = ''
}
async function run(action: () => Promise<void>) {
  busy.value = true; error.value = ''; message.value = ''
  try { await action() } catch (cause) { error.value = String(cause) } finally { busy.value = false }
}
function exportState(id: string) { return status.value?.available.find(a => a.sceneId === id)?.exportStatus ?? 'missing' }
function portrait(scene: Scene) { return convertFileSrc(`standing/${String(scene.costumeId).padStart(6, '0')}`, 'bd2assets') }
function sceneMode(scene: Scene): string {
  if (!scene.enabled) return 'disabled'
  return scene.useMods ? 'modded' : 'official'
}
function setSceneMode(scene: Scene, event: Event) {
  const mode = (event.target as HTMLSelectElement).value
  scene.enabled = mode !== 'disabled'
  scene.useMods = mode === 'modded'
  dirty.value = true
}
function expiry(id: string) {
  const time = status.value?.available.find(a => a.sceneId === id)?.expiresAtMs
  return time ? t('interactionsTab.expires', { time: new Date(time).toLocaleString(locale.value.replace(/_/g, '-')) }) : ''
}
async function choose(key: typeof paths[number]['key']) {
  const value = await open({ directory: true, multiple: false, defaultPath: settings.value?.[key] })
  if (typeof value === 'string' && settings.value) { settings.value[key] = value; dirty.value = true }
}
function add() {
  const scene = adding.value.find(s => s.sceneId === addId.value)
  if (!scene || !settings.value) return
  const { exportStatus: _status, expiresAtMs: _expiry, ...entry } = scene
  settings.value.scenes.push({ ...entry, enabled: false })
  addId.value = ''; dirty.value = true
}
async function save() {
  await invoke('save_local_interactions', { settings: settings.value })
  await load()
  message.value = 'interactionsTab.saved'
}
async function install() {
  // Save the visible choices before installing so installation cannot revert edits.
  if (dirty.value) await invoke('save_local_interactions', { settings: settings.value })
  await invoke('install_local_interactions')
  await load()
  message.value = 'interactionsTab.installed'
}
onMounted(() => run(load))
</script>

<template>
  <div class="h-full overflow-y-auto text-text-primary p-6 space-y-5">
    <div class="flex items-center justify-between gap-4">
      <div>
        <h1 class="text-xl font-semibold flex items-center gap-2"><Heart class="w-5 h-5" />{{ t('interactionsTab.title') }}</h1>
        <p class="text-sm text-text-secondary mt-1">{{ t('interactionsTab.legacy.description') }}</p>
      </div>
      <button class="action" :disabled="busy || dirty" @click="run(load)"><RefreshCw class="w-4 h-4" />{{ t('common.actions.refresh') }}</button>
    </div>
    <p v-if="error" role="alert" class="rounded-lg border border-red-400/40 bg-red-500/10 text-red-400 p-3 text-sm">{{ error }}</p>
    <p v-if="message" role="status" class="rounded-lg border border-border-subtle bg-accent-muted p-3 text-sm">{{ t(message) }}</p>
    <template v-if="settings && status">
      <section class="panel flex items-center justify-between gap-4">
        <div>
          <h2 class="font-semibold">{{ t('interactionsTab.legacy.toggle') }}</h2>
          <p class="text-sm text-text-secondary mt-1">{{ t('interactionsTab.legacy.toggleHelp') }}</p>
        </div>
        <div class="flex items-center gap-3 shrink-0">
          <span class="text-sm text-text-secondary">{{ t(settings.enabled ? 'common.status.enabled' : 'common.status.disabled') }}</span>
          <ToggleSwitch v-model="settings.enabled" :label="t('interactionsTab.legacy.toggle')" :disabled="busy" @update:model-value="dirty = true" />
        </div>
      </section>
      <section class="panel flex items-center justify-between gap-4">
        <div>
          <p class="font-medium">{{ t('interactionsTab.plugin', { version: status.pluginVersion }) }}</p>
          <p class="text-sm text-text-secondary mt-1">{{ t(status.pluginCurrent ? 'common.plugin.current' : status.installed ? 'common.plugin.updateAvailable' : 'common.plugin.notInstalled') }} · {{ t('interactionsTab.exitGame') }}</p>
        </div>
        <button class="action" :disabled="busy" @click="run(install)"><Download class="w-4 h-4" />{{ t(status.installed ? 'common.plugin.reinstall' : 'common.plugin.install') }}</button>
      </section>
      <section class="panel space-y-4">
        <div v-for="path in paths" :key="path.key">
          <label class="text-sm font-medium" :for="path.key">{{ t(`settingsTab.dating.paths.${path.key}.label`) }}</label>
          <div class="flex gap-2 mt-1.5">
            <input :id="path.key" v-model="settings[path.key]" class="field flex-1 min-w-0" :disabled="busy" @input="dirty = true" />
            <button class="action" :disabled="busy" :aria-label="t('common.actions.choose', { label: t(`settingsTab.dating.paths.${path.key}.label`) })" @click="choose(path.key)"><FolderOpen class="w-4 h-4" /></button>
          </div>
          <p v-if="path.key === 'audioDirectory'" class="text-xs text-text-secondary mt-1">{{ t('settingsTab.dating.paths.audioDirectory.hint') }}</p>
        </div>
        <div class="flex flex-wrap gap-8 text-sm pt-1">
          <label class="flex gap-2 items-center">{{ t('settingsTab.dating.dragHold') }}
            <input v-model.number="settings.dragHoldSeconds" class="field w-20" type="number" min="0.15" max="1.5" step="0.05" :disabled="busy" @input="dirty = true" />{{ t('common.seconds') }}
          </label>
          <label class="flex gap-2 items-center">{{ t('settingsTab.dating.opacity') }}
            <input v-model.number="settings.markerOpacity" type="range" min="0.08" max="0.6" step="0.01" :disabled="busy" @input="dirty = true" />
            {{ Math.round(settings.markerOpacity * 100) }}%
          </label>
        </div>
      </section>
      <section class="panel space-y-3">
        <div class="flex justify-between items-center"><h2 class="font-semibold">{{ t('interactionsTab.legacy.listTitle') }}</h2><span class="text-xs text-text-secondary">{{ settings.enabled ? t('interactionsTab.legacy.enabledCount', { count: settings.scenes.filter(s => s.enabled).length }) : t('interactionsTab.legacy.disabled') }}</span></div>
        <p class="text-sm text-text-secondary">{{ t('interactionsTab.legacy.entriesHelp') }}</p>
        <div class="interaction-grid">
          <article v-for="(scene, index) in settings.scenes" :key="scene.sceneId" class="min-w-0 rounded-lg border border-border-subtle p-3 flex flex-col gap-3">
            <div class="flex items-start gap-3 min-w-0">
              <Image :src="portrait(scene)" :alt="sceneTitle(scene)" class="w-20 h-24 shrink-0 object-cover rounded-md bg-surface-panel" error-src="characters/standing/placeholder_character.png" />
              <div class="min-w-0 flex-1">
                <h3 class="text-sm font-medium break-words">{{ sceneTitle(scene) }}</h3>
                <p class="text-xs text-text-secondary mt-2 break-words">{{ scene.sceneId }}</p>
                <p class="text-xs mt-1" :title="expiry(scene.sceneId)" :class="exportState(scene.sceneId) === 'ready' ? 'text-green-400' : 'text-amber-400'">{{ t(`interactionsTab.exportStatus.${exportState(scene.sceneId)}`) }}</p>
              </div>
            </div>
            <div class="flex items-center gap-2 mt-auto">
              <select :value="sceneMode(scene)" class="field min-w-0 flex-1" :disabled="busy" :aria-label="t('interactionsTab.legacy.modeFor', { name: sceneTitle(scene) })" @change="setSceneMode(scene, $event)">
                <option value="disabled">{{ t('common.status.disabled') }}</option>
                <option value="official">{{ t('interactionsTab.legacy.original') }}</option>
                <option value="modded">{{ t('interactionsTab.legacy.modded') }}</option>
              </select>
              <button class="action" :disabled="busy" :aria-label="t('skinsTab.remove', { name: sceneTitle(scene) })" @click="settings.scenes.splice(index, 1); dirty = true"><Trash2 class="w-4 h-4" /></button>
            </div>
          </article>
        </div>
        <p v-if="!settings.scenes.length" class="text-sm text-text-secondary py-3">{{ t('interactionsTab.legacy.empty') }}</p>
        <div class="flex gap-2 pt-3 border-t border-border-subtle">
          <select v-model="addId" class="field flex-1 min-w-0" :disabled="busy || !adding.length" :aria-label="t('interactionsTab.legacy.selectEntry')">
            <option value="">{{ t('interactionsTab.legacy.selectEntryPlaceholder') }}</option>
            <option v-for="scene in adding" :key="scene.sceneId" :value="scene.sceneId">{{ sceneTitle(scene) }} · {{ t(`interactionsTab.exportStatus.${scene.exportStatus}`) }}</option>
          </select>
          <button class="action" :disabled="busy || !addId" @click="add"><Plus class="w-4 h-4" />{{ t('common.actions.add') }}</button>
        </div>
      </section>
      <div class="flex items-center justify-between gap-4 pb-4">
        <p class="text-xs text-text-secondary">{{ t('interactionsTab.legacy.navigationHelp') }}</p>
        <button class="action bg-accent! text-text-on-accent!" :disabled="busy || !dirty" @click="run(save)"><Save class="w-4 h-4" />{{ t('common.actions.saveSettings') }}{{ dirty ? ' *' : '' }}</button>
      </div>
    </template>
  </div>
</template>

<style scoped>
@reference '../../styles/main.css';
.panel { @apply rounded-xl border border-border-subtle bg-surface-panel p-4; }
.action { @apply inline-flex shrink-0 items-center justify-center gap-2 rounded-md border border-border-subtle px-3 py-2 text-sm cursor-pointer hover:bg-state-hover disabled:opacity-40 disabled:cursor-default; }
.field { @apply rounded-md border border-border-subtle bg-surface-panel px-3 py-2 text-sm; }
.interaction-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 18rem), 1fr));
  gap: 0.75rem;
}
</style>
