import { useSettingsStore } from "../stores/settings";
import { useCharactersStore } from "../stores/characters";
import { useProfilesStore } from "../stores/profiles";
import { useModsStore } from "../stores/mods";
import { useLoggingStore } from "../stores/logging";
import { AppUpdateAvailable, useUpdater } from "./useUpdater";
import { globalModals } from "./useGlobalModals";
import { useNotificationStore } from "../stores/notification";
import { useI18n } from "vue-i18n";
import { useGameStore } from "../stores/game";
import { useLocalStorage } from "@vueuse/core";
import { watch } from "vue";

export function useAppInitializer() {
  const { t } = useI18n()
  const notificationStore = useNotificationStore()
  const loggingStore = useLoggingStore();
  const settingsStore = useSettingsStore();
  const modsStore = useModsStore();
  const gameStore = useGameStore()
  const skipUpdateVersion = useLocalStorage('skipUpdateVersion', '')
  let pendingUpdate: AppUpdateAvailable | null = null

  watch(() => settingsStore.settings.isFirstLaunch, (isFirstLaunch) => {
    if (!isFirstLaunch && pendingUpdate) {
      globalModals.updateAvailable.showModal(pendingUpdate)
      pendingUpdate = null
    }
  })

  const {
    checkForModPreviewUpdate,
    checkForAppUpdate,
    updateGameData,
  } = useUpdater()

  let initialized = false
  let catalogCheck: Promise<void> | undefined
  let catalogCheckPending = false

  function refreshGameData() {
    catalogCheckPending = true
    catalogCheck ??= (async () => {
      while (catalogCheckPending) {
        catalogCheckPending = false
        await updateGameData()
      }
    })().finally(() => { catalogCheck = undefined })
  }

  watch(() => [
    settingsStore.settings.assetLibraryDirectory,
    settingsStore.settings.gameDirectory,
  ] as const, ([root, game], [previousRoot, previousGame]) => {
    if (initialized && (root !== previousRoot || game !== previousGame)) refreshGameData()
  })

  async function initializeGamePath(): Promise<string | null> {
    try {
      const saved = settingsStore.settings.gameDirectory
      if (saved && await settingsStore.validateGamePath(saved)) return saved

      const detected = await settingsStore.locateGamePath()
      if (detected?.length !== 1) return null

      const gameDirectory = detected[0]
      await settingsStore.saveSettings({ gameDirectory })
      return gameDirectory
    } catch (error) {
      loggingStore.logError("Error during game directory initialization:", error)
      return null
    }
  }

  async function initialize(): Promise<{ isFirstLaunch: boolean, isBrownDustXOutdated: boolean, isUpdateAvailable: boolean }> {
    loggingStore.logDebug("Starting BD2ModManager");

    const gameDirectory = await initializeGamePath()
    const isFirstLaunch = settingsStore.settings.isFirstLaunch
    if (isFirstLaunch || !gameDirectory) {
      globalModals.welcome.showModal()
    }

    await Promise.all([
      useCharactersStore().loadCharacters(),
      useProfilesStore().loadProfiles(),
    ]);

    await modsStore.discoverMods()
    await gameStore.refresh()

    initialized = true
    refreshGameData()

    if (settingsStore.settings.autoUpdateModPreview) {
      checkForModPreviewUpdate(true)
    }

    let updateAvailable: AppUpdateAvailable | null = null

    if (import.meta.env.DEV || settingsStore.settings.checkForAppUpdates) {
      updateAvailable = await checkForAppUpdate()
    }
    
    if (updateAvailable && updateAvailable.versionAvailable !== skipUpdateVersion.value) {
      if (settingsStore.settings.isFirstLaunch) pendingUpdate = updateAvailable
      else globalModals.updateAvailable.showModal(updateAvailable)
    }

    if (gameStore.browndustxVersion?.status === "InstalledButOutdated") {
      notificationStore.add({
        type: "warn",
        title: t('app.notifications.brownDustXOutdated.title'),
        message: t('app.notifications.brownDustXOutdated.message'),
        duration: 10000,
      })
    }

    return {
      isFirstLaunch,
      isBrownDustXOutdated: gameStore.browndustxVersion?.status === "InstalledButOutdated",
      isUpdateAvailable: updateAvailable !== null
    }
  }

  return { initialize };
}
