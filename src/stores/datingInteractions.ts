import { defineStore } from 'pinia'
import { ref } from 'vue'
import { invoke } from '@tauri-apps/api/core'

export type DatingScene = { sceneId: string; costumeId: number; avatarAddress: string; title: string; enabled: boolean; nativeEnabled: boolean; useMods: boolean }
export type DatingSettings = { schemaVersion: number; enabled: boolean; pEntriesEnabled: boolean; nativeEntriesEnabled: boolean; dragHoldSeconds: number; markerOpacity: number; scenes: DatingScene[] }
export type DatingAvailable = DatingScene & { exportStatus: 'ready' | 'missing' | 'invalid' }
export type DatingStatus = { settings: DatingSettings; available: DatingAvailable[]; installed: boolean; pluginCurrent: boolean; pluginVersion: string; settingsPath: string }

// Both pages edit one draft, so saving resource settings cannot overwrite a
// newer character switch and navigating cannot discard unsaved selections.
export const useDatingInteractionsStore = defineStore('datingInteractions', () => {
  const status = ref<DatingStatus>()
  const settings = ref<DatingSettings>()
  const busy = ref(false)
  const dirty = ref(false)
  const message = ref('')
  const error = ref('')

  async function load() {
    const result = await invoke<DatingStatus>('get_local_interactions')
    status.value = result
    settings.value = structuredClone(result.settings)
    dirty.value = false
  }
  async function ensureLoaded() { if (!settings.value) await load() }
  async function run(action: () => Promise<void>) {
    if (busy.value) return
    busy.value = true; error.value = ''; message.value = ''
    try { await action() } catch (cause) { error.value = String(cause) } finally { busy.value = false }
  }
  async function save() {
    if (!settings.value) return
    await invoke('save_local_interactions', { settings: settings.value })
    await load()
    message.value = 'interactionsTab.saved'
  }
  async function install() {
    if (dirty.value) await invoke('save_local_interactions', { settings: settings.value })
    await invoke('install_local_interactions')
    await load()
    message.value = 'interactionsTab.installed'
  }
  return { status, settings, busy, dirty, message, error, load, ensureLoaded, run, save, install }
})
