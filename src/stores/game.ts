import { defineStore } from "pinia";
import { readonly, ref, watch } from "vue";
import { useSettingsStore } from "./settings";
import { invoke } from "@tauri-apps/api/core";
import { useLoggingStore } from "./logging";
import { useNotificationStore } from "./notification";

// #[derive(Serialize)]
// #[serde(tag = "type", content = "reason")]
// pub enum CanRemove {
//     Yes,
//     No(String),
// }

type CanRemove = { type: "Yes" } | { type: "No"; reason: string }

interface VersionInfo {
    status: "Installed" | "InstalledButOutdated" | "NotInstalled",
    version: string,
    canRemove: CanRemove,
    canConfigure: boolean
}

export type BundledGamePluginStatus = { installed: boolean; pluginCurrent: boolean; pluginVersion: string }
export type BundledGamePlugins = { dating: BundledGamePluginStatus; skins: BundledGamePluginStatus }
type BundledPluginSync = { changed: boolean; gameRunning: boolean; errors: string[] }

export const useGameStore = defineStore("game", () => {
    const loggingStore = useLoggingStore()
    const notificationStore = useNotificationStore()

    const settingsStore = useSettingsStore()

    const gameVersion = ref<string | null>(null)
    const bepinexVersion = ref<VersionInfo | null>(null)
    const browndustxVersion = ref<VersionInfo | null>(null)
    const configurationManagerVersion = ref<VersionInfo | null>(null)

    const isGameRunning = ref<boolean>(false)
    const bundledPluginUpdates = ref<BundledGamePlugins | null>(null)
    let retryPluginSync: ReturnType<typeof setTimeout> | undefined
    let syncRequest = 0

    async function syncBundledPlugins() {
        clearTimeout(retryPluginSync)
        const request = ++syncRequest
        const gameDirectory = settingsStore.settings.gameDirectory
        if (!gameDirectory) return

        try {
            const result = await invoke<BundledPluginSync>('sync_bundled_game_plugins', { gameDirectory })
            if (settingsStore.settings.gameDirectory !== gameDirectory || !result) return

            if (result.gameRunning && request === syncRequest) {
                retryPluginSync = setTimeout(() => void syncBundledPlugins(), 15_000)
            }
            if (result.errors.length) {
                loggingStore.logError('Could not update all bundled game plugins:', result.errors)
                notificationStore.add({ type: 'warn', message: result.errors.join('\n') })
            }
            if (result.changed) {
                const plugins = await invoke<BundledGamePlugins>('get_bundled_game_plugins')
                if (settingsStore.settings.gameDirectory === gameDirectory) bundledPluginUpdates.value = plugins
            }
        } catch (error) {
            loggingStore.logError('Could not check bundled game plugins:', error)
        }
    }

    async function launchGame() {}

    async function refresh() {
        const gameDirectory = settingsStore.settings.gameDirectory
        if (!gameDirectory) {
            gameVersion.value = null
            bepinexVersion.value = null
            browndustxVersion.value = null
            configurationManagerVersion.value = null
            return
        }

        const [game, bepinex, bdx, configManager] = await Promise.all([
            invoke<string | null>('get_game_version'),
            invoke<VersionInfo | null>('get_bepinex_version'),
            invoke<VersionInfo | null>('get_browndustx_version'),
            invoke<VersionInfo | null>('get_configmanager_version'),
        ])
        if (settingsStore.settings.gameDirectory !== gameDirectory) return

        loggingStore.logDebug("Game:", game)
        loggingStore.logDebug("BepInEx:", JSON.stringify(bepinex))
        loggingStore.logDebug("BrowndustX:", JSON.stringify(bdx))
        loggingStore.logDebug("Configuration Manager:", JSON.stringify(configManager))

        gameVersion.value = game
        bepinexVersion.value = bepinex
        browndustxVersion.value = bdx
        configurationManagerVersion.value = configManager
    }

    watch(
        () => settingsStore.settings.gameDirectory,
        () => {
            // always refresh when game directory changes, even if it's null
            bundledPluginUpdates.value = null
            void syncBundledPlugins()
            void refresh()
        },
        {
            immediate: true
        }
    )

    return {
        gameVersion: readonly(gameVersion),
        bepinexVersion: readonly(bepinexVersion),
        browndustxVersion: readonly(browndustxVersion),
        configurationManagerVersion: readonly(configurationManagerVersion),
        isGameRunning: readonly(isGameRunning),
        bundledPluginUpdates: readonly(bundledPluginUpdates),
        syncBundledPlugins,
        launchGame,
        refresh
    }
})
