<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { FolderOpen, Heart, Plus, RefreshCw, Save, Trash2, Download } from '@lucide/vue'
import { convertFileSrc, invoke } from '@tauri-apps/api/core'
import { open } from '@tauri-apps/plugin-dialog'
import Image from '../../components/common/Image.vue'
import ToggleSwitch from '../../components/common/ToggleSwitch.vue'

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
const labels = { ready: 'JSON 可用', missing: '缺少 JSON', expired: 'JSON 已过期', invalid: 'JSON 无效' }
const paths = [
  { key: 'exportDirectory', label: '互动 JSON 目录' },
  { key: 'audioDirectory', label: '约会音频目录', hint: '选择素材库中的 audio / dating 文件夹。' },
  { key: 'assetsDirectory', label: '美术素材库目录' },
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
  return time ? `有效期至 ${new Date(time).toLocaleString()}` : ''
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
  message.value = '已保存。重新打开游戏中的约会界面后生效。'
}
async function install() {
  // Save the visible choices before installing so installation cannot revert edits.
  if (dirty.value) await invoke('save_local_interactions', { settings: settings.value })
  await invoke('install_local_interactions')
  await load()
  message.value = '互动插件已安装。下次启动游戏时加载。'
}
onMounted(() => run(load))
</script>

<template>
  <div class="h-full overflow-y-auto text-text-primary p-6 space-y-5">
    <div class="flex items-center justify-between gap-4">
      <div>
        <h1 class="text-xl font-semibold flex items-center gap-2"><Heart class="w-5 h-5" />本地互动</h1>
        <p class="text-sm text-text-secondary mt-1">管理头像栏中的 P 互动。使用本地 JSON 和音频，保留官方约会与账号进度。</p>
      </div>
      <button class="action" :disabled="busy || dirty" @click="run(load)"><RefreshCw class="w-4 h-4" />刷新</button>
    </div>
    <p v-if="error" role="alert" class="rounded-lg border border-red-400/40 bg-red-500/10 text-red-400 p-3 text-sm">{{ error }}</p>
    <p v-if="message" role="status" class="rounded-lg border border-border-subtle bg-accent-muted p-3 text-sm">{{ message }}</p>
    <template v-if="settings && status">
      <section class="panel flex items-center justify-between gap-4">
        <div>
          <h2 class="font-semibold">本地互动总开关</h2>
          <p class="text-sm text-text-secondary mt-1">关闭后隐藏全部 P 互动，保留各条目的配置。保存后重新进入约会界面生效。</p>
        </div>
        <div class="flex items-center gap-3 shrink-0">
          <span class="text-sm text-text-secondary">{{ settings.enabled ? '已开启' : '已关闭' }}</span>
          <ToggleSwitch v-model="settings.enabled" label="本地互动总开关" :disabled="busy" @update:model-value="dirty = true" />
        </div>
      </section>
      <section class="panel flex items-center justify-between gap-4">
        <div>
          <p class="font-medium">互动插件 {{ status.pluginVersion }}</p>
          <p class="text-sm text-text-secondary mt-1">{{ status.pluginCurrent ? '已安装当前版本' : status.installed ? '已有插件，可更新至当前版本' : '尚未安装' }} · 安装或更新前请退出游戏</p>
        </div>
        <button class="action" :disabled="busy" @click="run(install)"><Download class="w-4 h-4" />{{ status.installed ? '重新安装 / 更新' : '安装互动插件' }}</button>
      </section>
      <section class="panel space-y-4">
        <div v-for="path in paths" :key="path.key">
          <label class="text-sm font-medium" :for="path.key">{{ path.label }}</label>
          <div class="flex gap-2 mt-1.5">
            <input :id="path.key" v-model="settings[path.key]" class="field flex-1 min-w-0" :disabled="busy" @input="dirty = true" />
            <button class="action" :disabled="busy" :aria-label="`选择${path.label}`" @click="choose(path.key)"><FolderOpen class="w-4 h-4" /></button>
          </div>
          <p v-if="'hint' in path" class="text-xs text-text-secondary mt-1">{{ path.hint }}</p>
        </div>
        <div class="flex flex-wrap gap-8 text-sm pt-1">
          <label class="flex gap-2 items-center">左键长按后可拖动
            <input v-model.number="settings.dragHoldSeconds" class="field w-20" type="number" min="0.15" max="1.5" step="0.05" :disabled="busy" @input="dirty = true" />秒
          </label>
          <label class="flex gap-2 items-center">触点透明度
            <input v-model.number="settings.markerOpacity" type="range" min="0.08" max="0.6" step="0.01" :disabled="busy" @input="dirty = true" />
            {{ Math.round(settings.markerOpacity * 100) }}%
          </label>
        </div>
      </section>
      <section class="panel space-y-3">
        <div class="flex justify-between items-center"><h2 class="font-semibold">头像栏互动</h2><span class="text-xs text-text-secondary">{{ settings.enabled ? `${settings.scenes.filter(s => s.enabled).length} 项已启用` : '总开关已关闭' }}</span></div>
        <p class="text-sm text-text-secondary">新添加的条目默认关闭；启用时可选择原版资源或应用已启用的模组。导出 JSON 本身不会新增条目。</p>
        <div class="interaction-grid">
          <article v-for="(scene, index) in settings.scenes" :key="scene.sceneId" class="min-w-0 rounded-lg border border-border-subtle p-3 flex flex-col gap-3">
            <div class="flex items-start gap-3 min-w-0">
              <Image :src="portrait(scene)" :alt="scene.title" class="w-20 h-24 shrink-0 object-cover rounded-md bg-surface-panel" error-src="characters/standing/placeholder_character.png" />
              <div class="min-w-0 flex-1">
                <h3 class="text-sm font-medium break-words">{{ scene.title }}</h3>
                <p class="text-xs text-text-secondary mt-2 break-words">{{ scene.sceneId }}</p>
                <p class="text-xs mt-1" :title="expiry(scene.sceneId)" :class="exportState(scene.sceneId) === 'ready' ? 'text-green-400' : 'text-amber-400'">{{ labels[exportState(scene.sceneId)] }}</p>
              </div>
            </div>
            <div class="flex items-center gap-2 mt-auto">
              <select :value="sceneMode(scene)" class="field min-w-0 flex-1" :disabled="busy" :aria-label="`${scene.title}的互动状态`" @change="setSceneMode(scene, $event)">
                <option value="disabled">关闭</option>
                <option value="official">启用 · 原版</option>
                <option value="modded">启用 · 应用模组</option>
              </select>
              <button class="action" :disabled="busy" :aria-label="`移除${scene.title}`" @click="settings.scenes.splice(index, 1); dirty = true"><Trash2 class="w-4 h-4" /></button>
            </div>
          </article>
        </div>
        <p v-if="!settings.scenes.length" class="text-sm text-text-secondary py-3">尚未添加互动。可从下方选择。</p>
        <div class="flex gap-2 pt-3 border-t border-border-subtle">
          <select v-model="addId" class="field flex-1 min-w-0" :disabled="busy || !adding.length" aria-label="选择要添加的互动">
            <option value="">选择要添加的互动…</option>
            <option v-for="scene in adding" :key="scene.sceneId" :value="scene.sceneId">{{ scene.title }} · {{ labels[scene.exportStatus] }}</option>
          </select>
          <button class="action" :disabled="busy || !addId" @click="add"><Plus class="w-4 h-4" />添加</button>
        </div>
      </section>
      <div class="flex items-center justify-between gap-4 pb-4">
        <p class="text-xs text-text-secondary">右键移动画面，滚轮缩放；原生 Reset 恢复初始 pose 和好感度。</p>
        <button class="action bg-accent! text-text-on-accent!" :disabled="busy || !dirty" @click="run(save)"><Save class="w-4 h-4" />保存设置{{ dirty ? ' *' : '' }}</button>
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
