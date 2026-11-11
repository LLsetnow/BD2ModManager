<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
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
const sourceOptions = computed(() => status.value?.costumes.map(c => ({ value: c.id, label: `${c.name} · ${c.costume}（${c.id}）` })) ?? [])
const targetOptions = computed(() => targets.value.map(c => ({ value: c.id, label: `${c.costume}（${c.id}）` })))
const canAdd = computed(() => !!source.value && targets.value.some(c => c.id === targetId.value))
const sources = computed(() => [...new Set(settings.value?.mappings.map(m => m.sourceId))])
function costume(id: string) { return status.value?.costumes.find(c => c.id === id) }
function title(id: string) { const c = costume(id); return c ? `${c.name} · ${c.costume}` : id }
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
    error.value = `自动保存失败：${String(cause)}`
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
  if (settings.value.mappings.some(m => m.sourceId === sourceId.value && m.targetId === targetId.value)) { error.value = '该映射已存在'; return }
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
  message.value = '皮肤插件已安装，下次启动游戏时加载。'
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
      <div><h1 class="text-xl font-semibold flex items-center gap-2"><Shirt class="w-5 h-5" />皮肤映射</h1>
        <p class="text-sm text-text-secondary mt-1">在游戏角色小人上方点击 Skin，切换立绘、背景、小人及技能演出。</p></div>
      <button class="action" :disabled="busy" @click="run(refresh)"><RefreshCw class="w-4 h-4" />刷新</button>
    </div>
    <p v-if="error" role="alert" class="rounded-lg border border-red-400/40 bg-red-500/10 p-3 text-sm text-red-400">{{ error }}</p>
    <p v-if="message" role="status" class="panel text-sm">{{ message }}</p>
    <template v-if="status && settings">
      <section class="panel flex items-center justify-between gap-4">
        <div><h2 class="font-semibold">皮肤映射总开关</h2>
          <p class="text-sm text-text-secondary mt-1">关闭后恢复游戏原有外观，保留映射和皮肤选择。修改后自动保存并生效。</p></div>
        <div class="flex items-center gap-3 shrink-0">
          <span class="text-sm text-text-secondary">{{ settings.enabled ? '已开启' : '已关闭' }}</span>
          <ToggleSwitch v-model="settings.enabled" label="皮肤映射总开关" :disabled="busy" @update:model-value="setEnabled" />
        </div>
      </section>
      <section class="panel flex items-center justify-between gap-4">
        <div><p class="font-medium">皮肤插件 {{ status.pluginVersion }}</p>
          <p class="text-sm text-text-secondary mt-1">{{ status.pluginCurrent ? '已安装当前版本' : status.installed ? '可更新至当前版本' : '尚未安装' }}</p></div>
        <button class="action" :disabled="busy" @click="run(install)"><Download class="w-4 h-4" />{{ status.installed ? '重新安装 / 更新' : '安装皮肤插件' }}</button>
      </section>
      <section class="panel space-y-3">
        <h2 class="font-semibold">添加映射</h2>
        <div class="grid grid-cols-2 gap-3">
          <div class="text-sm space-y-1">
            <label for="skin-source">来源服装</label>
            <EditableSelect id="skin-source" :model-value="sourceId" :options="sourceOptions" label="来源服装" :disabled="busy" @update:model-value="changeSource" />
          </div>
          <div class="text-sm space-y-1">
            <label for="skin-target">目标皮肤</label>
            <EditableSelect id="skin-target" v-model="targetId" :options="targetOptions" label="目标皮肤" :disabled="busy || !targets.length" />
          </div>
        </div>
        <div class="flex gap-3"><input v-model="label" class="field flex-1 min-w-0" placeholder="皮肤名称（可选）" maxlength="100" :disabled="busy" aria-label="新皮肤名称" />
          <button class="action" :disabled="busy || !canAdd" @click="add"><Plus class="w-4 h-4" />添加</button></div>
        <p class="text-xs text-text-secondary">目标使用游戏内同一角色的资源。游戏中的 Skin 按钮按列表顺序循环切换，原服装的等级和技能数值保持不变。</p>
      </section>
      <div class="mapping-grid">
        <section v-for="id in sources" :key="id" class="panel min-w-0 space-y-4">
          <div class="min-w-0"><h2 class="font-semibold break-words">{{ title(id) }}</h2><p class="text-xs text-text-secondary mt-1">原服装 ID：{{ id }}</p></div>
          <label class="text-sm flex flex-col gap-1.5 min-w-0">当前皮肤
            <select v-model="settings.selectedSkins[id]" class="field w-full min-w-0" :disabled="busy" @change="selectSkin(id)">
              <option :value="id">原版 · {{ id }}</option>
              <option v-for="m in choices(id)" :key="m.targetId" :value="m.targetId">{{ m.label || title(m.targetId) }} · {{ m.targetId }}</option>
            </select>
          </label>
          <div v-for="row in settings.mappings.filter(m => m.sourceId === id)" :key="row.targetId" class="border-t border-border-subtle pt-3 space-y-3 min-w-0">
            <div class="flex items-center gap-2 min-w-0">
              <input v-model="row.enabled" type="checkbox" class="shrink-0" :aria-label="`启用 ${row.label}`" :disabled="busy" @change="enable(row)" />
              <Image :src="preview(row.sourceId)" :alt="title(row.sourceId)" class="w-20 h-24 shrink-0 object-cover rounded-md bg-surface-panel" error-src="characters/standing/placeholder_character.png" />
              <ArrowRight class="w-4 h-4 shrink-0 text-text-secondary" />
              <Image :src="preview(row.targetId)" :alt="title(row.targetId)" class="w-20 h-24 shrink-0 object-cover rounded-md bg-surface-panel" error-src="characters/standing/placeholder_character.png" />
              <button class="action ml-auto px-2!" :disabled="busy" :aria-label="`移除 ${row.label}`" @click="remove(settings.mappings.indexOf(row))"><Trash2 class="w-4 h-4" /></button>
            </div>
            <div class="min-w-0"><input :value="labelDrafts[draftKey(row)] ?? row.label" class="field w-full min-w-0" maxlength="100" :disabled="busy" aria-label="皮肤名称" @input="editLabel(row, $event)" @change="rename(row)" @keydown.enter="finishLabel" />
              <p class="text-xs text-text-secondary mt-2 break-words">{{ row.sourceId }} → {{ row.targetId }} · {{ title(row.targetId) }}</p></div>
          </div>
        </section>
      </div>
      <div class="flex flex-wrap justify-between items-center gap-2 pb-4 text-xs text-text-secondary">
        <p :title="status.settingsPath">添加、删除和切换会自动保存；名称编辑完成后自动保存。</p>
        <span role="status" aria-live="polite">{{ pendingSaves ? '正在保存…' : saved ? '已自动保存' : '' }}</span>
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
