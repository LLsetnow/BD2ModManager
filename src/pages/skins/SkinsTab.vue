<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useCharactersStore } from '../../stores/characters'
import { getCharName, useLang } from '../../utils/formatCharName'
import { onBeforeRouteLeave, useRoute } from 'vue-router'
import { Shirt, Plus, Trash2, RefreshCw, Download, ArrowRight } from '@lucide/vue'
import { convertFileSrc, invoke } from '@tauri-apps/api/core'
import Image from '../../components/common/Image.vue'
import EditableSelect from '../../components/common/EditableSelect.vue'
import ToggleSwitch from '../../components/common/ToggleSwitch.vue'

type Mapping = { sourceId: string; targetId: string; label: string; enabled: boolean }
type Settings = { schemaVersion: number; enabled: boolean; mappings: Mapping[]; selectedSkins: Record<string, string> }
type Change =
  | { type: 'setEnabled'; enabled: boolean }
  | { type: 'addMapping'; mapping: Mapping }
  | { type: 'removeMapping'; sourceId: string; targetId: string }
  | { type: 'updateMapping'; sourceId: string; targetId: string; label?: string; enabled?: boolean }
  | { type: 'selectSkin'; sourceId: string; targetId: string }
type Costume = { id: string; characterId: number; name: string; costume: string; skinType: string }
type Status = { settings: Settings; costumes: Costume[]; installed: boolean; pluginCurrent: boolean; pluginVersion: string; settingsPath: string }
const { t } = useI18n()
const lang = useLang()
const charactersStore = useCharactersStore()
const route = useRoute()
const status = ref<Status>()
const settings = ref<Settings>()
const sourceId = ref('001207')
const targetId = ref('001297')
const label = ref('')
const error = ref('')
const message = ref('')
const busy = ref(false)
const pendingSaves = ref(0)
const saved = ref(false)
const labelDrafts = ref<Record<string, string>>({})
let saveQueue: Promise<void> = Promise.resolve()
let saveRevision = 0
const source = computed(() => costume(sourceId.value))
const targets = computed(() => status.value?.costumes.filter(c => c.characterId === source.value?.characterId && c.id !== sourceId.value) ?? [])
const sourceOptions = computed(() => status.value?.costumes.map(c => ({ value: c.id, label: `${costumeNames(c).character} · ${costumeNames(c).costume} (${c.id})` })) ?? [])
const targetOptions = computed(() => targets.value.map(c => ({ value: c.id, label: `${costumeNames(c).costume} (${c.id})` })))
const canAdd = computed(() => !!source.value && targets.value.some(c => c.id === targetId.value))
const sources = computed(() => [...new Set(settings.value?.mappings.map(m => m.sourceId))])
function costume(id: string) { return status.value?.costumes.find(c => c.id === id) }
function costumeNames(c: Costume) {
  const character = charactersStore.getCharacterById(c.id)
  return character ? getCharName(character, lang.value) : { character: c.name, costume: c.costume }
}
function title(id: string) { const c = costume(id); return c ? `${costumeNames(c).character} · ${costumeNames(c).costume}` : id }
function preview(id: string) { return convertFileSrc(`standing/${id}`, 'bd2assets') }
function choices(id: string) { return settings.value?.mappings.filter(m => m.sourceId === id && m.enabled) ?? [] }
async function run(action: () => Promise<void>) {
  busy.value = true; error.value = ''; message.value = ''
  try { await action() } catch (cause) { error.value = String(cause) } finally { busy.value = false }
}
async function load() {
  const result = await invoke<Status>('get_costume_skins')
  status.value = result; settings.value = structuredClone(result.settings)
}
async function refresh() { await saveQueue; await load() }
async function commit(change: Change) {
  const revision = ++saveRevision
  const payload = JSON.parse(JSON.stringify(change)) as Change
  error.value = ''; message.value = ''; pendingSaves.value++
  const operation = saveQueue.then(() => invoke<Settings>('update_costume_skins', { change: payload }))
  saveQueue = operation.then(() => undefined, () => undefined)
  try {
    const next = await operation
    if (status.value) status.value.settings = structuredClone(next)
    if (revision === saveRevision) { settings.value = structuredClone(next); saved.value = true }
    return true
  } catch (cause) {
    error.value = t('skinsTab.autoSaveFailed', { reason: String(cause) })
    if (revision === saveRevision) {
      saved.value = false
      try { await load() } catch { if (status.value) settings.value = structuredClone(status.value.settings) }
    }
    return false
  } finally { pendingSaves.value-- }
}
function changeSource(value = sourceId.value) {
  sourceId.value = value
  if (source.value && !targets.value.some(c => c.id === targetId.value)) targetId.value = targets.value[0]?.id ?? ''
}
async function add() {
  if (!settings.value || !source.value || !targets.value.some(c => c.id === targetId.value)) return
  if (settings.value.mappings.some(m => m.sourceId === sourceId.value && m.targetId === targetId.value)) { error.value = t('skinsTab.duplicate'); return }
  const mapping = { sourceId: sourceId.value, targetId: targetId.value, label: label.value.trim() || costume(targetId.value)?.costume || '', enabled: true }
  const draft = label.value
  settings.value.mappings.push(mapping)
  settings.value.selectedSkins[sourceId.value] ??= sourceId.value
  if (await commit({ type: 'addMapping', mapping }) && label.value === draft) label.value = ''
}
function remove(index: number) {
  if (!settings.value) return
  const row = settings.value.mappings.splice(index, 1)[0]
  if (!row) return
  if (!settings.value.mappings.some(m => m.sourceId === row.sourceId)) delete settings.value.selectedSkins[row.sourceId]
  else if (settings.value.selectedSkins[row.sourceId] === row.targetId) settings.value.selectedSkins[row.sourceId] = row.sourceId
  delete labelDrafts.value[draftKey(row)]
  void commit({ type: 'removeMapping', sourceId: row.sourceId, targetId: row.targetId })
}
function enable(row: Mapping) {
  if (!settings.value) return
  if (!row.enabled && settings.value.selectedSkins[row.sourceId] === row.targetId) settings.value.selectedSkins[row.sourceId] = row.sourceId
  void commit({ type: 'updateMapping', sourceId: row.sourceId, targetId: row.targetId, enabled: row.enabled })
}
function setEnabled(enabled: boolean) { void commit({ type: 'setEnabled', enabled }) }
function selectSkin(sourceId: string) {
  const targetId = settings.value?.selectedSkins[sourceId]
  if (targetId) void commit({ type: 'selectSkin', sourceId, targetId })
}
function draftKey(row: Mapping) { return `${row.sourceId}:${row.targetId}` }
function editLabel(row: Mapping, event: Event) { labelDrafts.value[draftKey(row)] = (event.target as HTMLInputElement).value }
async function rename(row: Mapping) {
  const key = draftKey(row)
  const text = labelDrafts.value[key]
  if (text === undefined || text === row.label) return
  await commit({ type: 'updateMapping', sourceId: row.sourceId, targetId: row.targetId, label: text })
  if (labelDrafts.value[key] === text) delete labelDrafts.value[key]
}
function finishLabel(event: KeyboardEvent) {
  if (!event.isComposing) (event.target as HTMLInputElement).blur()
}
async function install() {
  await saveQueue
  await invoke('install_costume_skins'); await load()
  message.value = 'skinsTab.installed'
}
onMounted(() => run(async () => {
  await load()
  const requested = route.query.source
  if (typeof requested === 'string' && costume(requested)) { sourceId.value = requested; changeSource() }
}))
onBeforeRouteLeave(async () => { await saveQueue })
</script>

<template>
  <div class="h-full overflow-y-auto p-6 space-y-5 text-text-primary">
    <div class="flex items-center justify-between gap-3">
      <div><h1 class="text-xl font-semibold flex items-center gap-2"><Shirt class="w-5 h-5" />{{ t('skinsTab.title') }}</h1>
        <p class="text-sm text-text-secondary mt-1">{{ t('skinsTab.description') }}</p></div>
      <button class="action" :disabled="busy" @click="run(refresh)"><RefreshCw class="w-4 h-4" />{{ t('common.actions.refresh') }}</button>
    </div>
    <p v-if="error" role="alert" class="rounded-lg border border-red-400/40 bg-red-500/10 p-3 text-sm text-red-400">{{ error }}</p>
    <p v-if="message" role="status" class="panel text-sm">{{ t(message) }}</p>
    <template v-if="status && settings">
      <section class="panel flex items-center justify-between gap-4">
        <div><h2 class="font-semibold">{{ t('skinsTab.toggle') }}</h2>
          <p class="text-sm text-text-secondary mt-1">{{ t('skinsTab.toggleHelp') }}</p></div>
        <div class="flex items-center gap-3 shrink-0">
          <span class="text-sm text-text-secondary">{{ t(settings.enabled ? 'common.status.enabled' : 'common.status.disabled') }}</span>
          <ToggleSwitch v-model="settings.enabled" :label="t('skinsTab.toggle')" :disabled="busy" @update:model-value="setEnabled" />
        </div>
      </section>
      <section class="panel flex items-center justify-between gap-4">
        <div><p class="font-medium">{{ t('skinsTab.plugin', { version: status.pluginVersion }) }}</p>
          <p class="text-sm text-text-secondary mt-1">{{ t(status.pluginCurrent ? 'common.plugin.current' : status.installed ? 'common.plugin.updateAvailable' : 'common.plugin.notInstalled') }}</p></div>
        <button class="action" :disabled="busy" @click="run(install)"><Download class="w-4 h-4" />{{ t(status.installed ? 'common.plugin.reinstall' : 'skinsTab.install') }}</button>
      </section>
      <section class="panel space-y-3">
        <h2 class="font-semibold">{{ t('skinsTab.addMapping') }}</h2>
        <div class="grid grid-cols-2 gap-3">
          <div class="text-sm space-y-1">
            <label for="skin-source">{{ t('skinsTab.source') }}</label>
            <EditableSelect id="skin-source" :model-value="sourceId" :options="sourceOptions" :label="t('skinsTab.source')" :disabled="busy" @update:model-value="changeSource" />
          </div>
          <div class="text-sm space-y-1">
            <label for="skin-target">{{ t('skinsTab.target') }}</label>
            <EditableSelect id="skin-target" v-model="targetId" :options="targetOptions" :label="t('skinsTab.target')" :disabled="busy || !targets.length" />
          </div>
        </div>
        <div class="flex gap-3"><input v-model="label" class="field flex-1 min-w-0" :placeholder="t('skinsTab.optionalName')" maxlength="100" :disabled="busy" :aria-label="t('skinsTab.newName')" />
          <button class="action" :disabled="busy || !canAdd" @click="add"><Plus class="w-4 h-4" />{{ t('common.actions.add') }}</button></div>
        <p class="text-xs text-text-secondary">{{ t('skinsTab.mappingHelp') }}</p>
      </section>
      <div class="mapping-grid">
        <section v-for="id in sources" :key="id" class="panel min-w-0 space-y-4">
          <div class="min-w-0"><h2 class="font-semibold break-words">{{ title(id) }}</h2><p class="text-xs text-text-secondary mt-1">{{ t('skinsTab.sourceId', { id }) }}</p></div>
          <label class="text-sm flex flex-col gap-1.5 min-w-0">{{ t('skinsTab.current') }}
            <select v-model="settings.selectedSkins[id]" class="field w-full min-w-0" :disabled="busy" @change="selectSkin(id)">
              <option :value="id">{{ t('skinsTab.original', { id }) }}</option>
              <option v-for="m in choices(id)" :key="m.targetId" :value="m.targetId">{{ m.label || title(m.targetId) }} · {{ m.targetId }}</option>
            </select>
          </label>
          <div v-for="row in settings.mappings.filter(m => m.sourceId === id)" :key="row.targetId" class="border-t border-border-subtle pt-3 space-y-3 min-w-0">
            <div class="flex items-center gap-2 min-w-0">
              <input v-model="row.enabled" type="checkbox" class="shrink-0" :aria-label="t('skinsTab.enable', { name: row.label || title(row.targetId) })" :disabled="busy" @change="enable(row)" />
              <Image :src="preview(row.sourceId)" :alt="title(row.sourceId)" class="w-20 h-24 shrink-0 object-cover rounded-md bg-surface-panel" error-src="characters/standing/placeholder_character.png" />
              <ArrowRight class="w-4 h-4 shrink-0 text-text-secondary" />
              <Image :src="preview(row.targetId)" :alt="title(row.targetId)" class="w-20 h-24 shrink-0 object-cover rounded-md bg-surface-panel" error-src="characters/standing/placeholder_character.png" />
              <button class="action ml-auto px-2!" :disabled="busy" :aria-label="t('skinsTab.remove', { name: row.label || title(row.targetId) })" @click="remove(settings.mappings.indexOf(row))"><Trash2 class="w-4 h-4" /></button>
            </div>
            <div class="min-w-0"><input :value="labelDrafts[draftKey(row)] ?? row.label" class="field w-full min-w-0" maxlength="100" :disabled="busy" :aria-label="t('skinsTab.name')" @input="editLabel(row, $event)" @change="rename(row)" @keydown.enter="finishLabel" />
              <p class="text-xs text-text-secondary mt-2 break-words">{{ row.sourceId }} → {{ row.targetId }} · {{ title(row.targetId) }}</p></div>
          </div>
        </section>
      </div>
      <div class="flex flex-wrap justify-between items-center gap-2 pb-4 text-xs text-text-secondary">
        <p :title="status.settingsPath">{{ t('skinsTab.autoSaveHelp') }}</p>
        <span role="status" aria-live="polite">{{ pendingSaves ? t('common.status.saving') : saved ? t('common.status.autoSaved') : '' }}</span>
      </div>
    </template>
  </div>
</template>

<style scoped>
@reference '../../styles/main.css';
.panel { @apply rounded-xl border border-border-subtle bg-surface-panel p-4; }
.action { @apply inline-flex shrink-0 items-center justify-center gap-2 rounded-md border border-border-subtle px-3 py-2 text-sm cursor-pointer hover:bg-state-hover disabled:opacity-40 disabled:cursor-not-allowed; }
.field { @apply rounded-md border border-border-subtle bg-surface-panel px-3 py-2 text-sm text-text-primary; }
.mapping-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 20rem), 1fr));
  gap: 1rem;
  align-items: start;
}
</style>
