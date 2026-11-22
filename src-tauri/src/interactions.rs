use std::{collections::HashSet, fs, path::{Path, PathBuf}};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use tauri::Manager;
use crate::{AppState, game::{bundled_plugins, game::BundledPluginStatus}, utils::path::get_game_dir};

const PLUGIN: &[u8] = include_bytes!("../resources/BD2PreviewInteractions.dll");
const PLUGIN_VERSION: &str = "0.5.2";
const PLUGIN_DLL: &str = "BD2PreviewInteractions.dll";

#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SceneEntry {
    scene_id: String,
    costume_id: u32,
    avatar_address: String,
    title: String,
    enabled: bool,
    #[serde(default)]
    native_enabled: bool,
    #[serde(default = "default_enabled")]
    use_mods: bool,
}

#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct InteractionSettings {
    schema_version: u32,
    #[serde(default)]
    assets_directory: String,
    #[serde(default = "default_enabled")]
    enabled: bool,
    #[serde(default = "default_enabled")]
    p_entries_enabled: bool,
    #[serde(default = "default_enabled")]
    native_entries_enabled: bool,
    drag_hold_seconds: f32,
    marker_opacity: f32,
    scenes: Vec<SceneEntry>,
}

fn default_enabled() -> bool { true }

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CatalogEntry {
    #[serde(flatten)]
    entry: SceneEntry,
    export_status: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct InteractionStatus {
    settings: InteractionSettings,
    available: Vec<CatalogEntry>,
    #[serde(flatten)]
    plugin: BundledPluginStatus,
    settings_path: String,
}

fn plugin_dir(state: &AppState) -> Result<PathBuf, String> {
    let config = state.config.lock().map_err(|e| e.to_string())?;
    let game = get_game_dir(&config).ok_or("请先在设置中选择游戏目录")?;
    if !game.join("BrownDust II.exe").exists() { return Err("游戏目录无效".into()); }
    Ok(plugin_dir_for_game(&game))
}

fn plugin_dir_for_game(game: &Path) -> PathBuf {
    game.join("BepInEx/plugins/BD2PreviewInteractions")
}

pub(crate) fn bundled_plugin_status(state: &AppState) -> BundledPluginStatus {
    let dll = plugin_dir(state).ok().map(|dir| dir.join(PLUGIN_DLL));
    BundledPluginStatus::from_dll(dll, PLUGIN, PLUGIN_VERSION)
}

pub(crate) fn plugin_status_for_game(game: &Path) -> BundledPluginStatus {
    BundledPluginStatus::from_dll(Some(plugin_dir_for_game(game).join(PLUGIN_DLL)), PLUGIN, PLUGIN_VERSION)
}

fn manager_path(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    Ok(app.path().app_data_dir().map_err(|e| e.to_string())?.join("local-interactions.json"))
}

fn catalog(data: &Value) -> Vec<SceneEntry> {
    let Some(dating) = data["dating"].as_object() else { return vec![]; };
    let Some(characters) = data["characters"].as_array() else { return vec![]; };
    let mut entries: Vec<_> = dating.iter().filter_map(|(number, costume)| {
        scene_number(&format!("illust_dating{number}"))?;
        let costume = costume.as_str()?;
        let character = characters.iter().find(|c| c["costume_id"].as_str() == Some(costume))?;
        let portrait = character.pointer("/assets/character_image").or_else(|| character.get("character_image"))?.as_str()?;
        let name = character.pointer("/character_name/cn").and_then(Value::as_str).unwrap_or("");
        let title = character.pointer("/costume_name/cn").and_then(Value::as_str).unwrap_or("");
        Some(SceneEntry {
            scene_id: format!("illust_dating{number}"), costume_id: costume.parse().ok()?,
            avatar_address: format!("IllustInventory_1/{portrait}"), title: format!("{name} · {title}"),
            enabled: false, native_enabled: true, use_mods: true,
        })
    }).collect();
    entries.sort_by_key(|e| scene_number(&e.scene_id).unwrap_or(0));
    entries
}

fn scene_number(id: &str) -> Option<u32> {
    let number = id.strip_prefix("illust_dating")?;
    if number.is_empty() || !number.bytes().all(|b| b.is_ascii_digit()) { return None; }
    number.parse::<u32>().ok().filter(|n| (1..=9999).contains(n))
}

fn defaults(entries: Vec<SceneEntry>) -> InteractionSettings {
    InteractionSettings {
        schema_version: 2,
        assets_directory: String::new(),
        enabled: true,
        p_entries_enabled: true,
        native_entries_enabled: true,
        drag_hold_seconds: 0.35, marker_opacity: 0.32,
        scenes: entries,
    }
}

fn legacy_native_mode(dir: Option<&Path>) -> bool {
    let cfg = dir.and_then(|dir| dir.ancestors().nth(2)
        .map(|p| p.join("config/local.bd2.previewinteractions.cfg")));
    cfg.and_then(|p| fs::read_to_string(p).ok()).and_then(|text| text.lines().find_map(|line| {
        let (key, value) = line.split_once('=')?;
        (key.trim() == "UseNativeDatingEntries").then(|| value.trim().eq_ignore_ascii_case("true"))
    })).unwrap_or(true)
}

fn normalize(mut settings: InteractionSettings, legacy_native: bool, legacy_defaults: &HashSet<u32>, entries: &[SceneEntry]) -> Result<InteractionSettings, String> {
    if settings.schema_version != 1 && settings.schema_version != 2 { return Err("不支持的配置版本".into()); }
    let was_legacy = settings.schema_version == 1;
    if settings.schema_version == 1 {
        for scene in &mut settings.scenes {
            let added = scene_number(&scene.scene_id).is_some_and(|id| legacy_defaults.contains(&id));
            scene.native_enabled = if added { legacy_native && scene.enabled } else { true };
            if added && legacy_native { scene.enabled = false; }
        }
        settings.schema_version = 2;
    }
    for mut entry in entries.iter().cloned() {
        if !settings.scenes.iter().any(|s| s.scene_id == entry.scene_id) {
            // Existing official entries were visible even in the old P-only UI.
            entry.native_enabled = was_legacy && !scene_number(&entry.scene_id).is_some_and(|id| legacy_defaults.contains(&id));
            settings.scenes.push(entry);
        }
    }
    // Keep the legacy field compatible while retiring the per-scene resource choice.
    for scene in &mut settings.scenes { scene.use_mods = true; }
    settings.scenes.sort_by_key(|entry| scene_number(&entry.scene_id).unwrap_or(0));
    validate(&settings)?;
    Ok(settings)
}

fn legacy_native_defaults(asset_root: &Path) -> HashSet<u32> {
    let data = crate::updater::catalogs::read_installed(asset_root, "modmanager.json")
        .and_then(|bytes| serde_json::from_slice::<Value>(&bytes).ok())
        .or_else(|| serde_json::from_str(include_str!("../data/modmanager.json")).ok());
    data.as_ref().and_then(|value| value.pointer("/dating/legacyNativeSceneIds")?.as_array())
        .into_iter().flatten().filter_map(|value| u32::try_from(value.as_u64()?).ok()).collect()
}

fn read_settings_for_dir(app: &tauri::AppHandle, dir: Option<&Path>, asset_root: &Path, entries: &[SceneEntry]) -> Result<InteractionSettings, String> {
    // Prefer game settings so the page describes what the plugin will load.
    let game = dir.map(|p| p.join("settings.json"));
    for path in game.into_iter().chain(std::iter::once(manager_path(app)?)) {
        if path.exists() {
            let settings = serde_json::from_slice(&fs::read(&path).map_err(|e| e.to_string())?).map_err(|e| format!("{}: {e}", path.display()))?;
            return normalize(settings, legacy_native_mode(dir), &legacy_native_defaults(asset_root), entries);
        }
    }
    Ok(defaults(entries.to_vec()))
}

fn asset_library_root(state: &AppState) -> Result<PathBuf, String> {
    let path = {
        let config = state.config.lock().map_err(|e| e.to_string())?;
        config.ensure_default_asset_library().map_err(|e| format!("无法创建默认美术素材库目录：{e}"))?;
        config.asset_library_path()
    };
    if !path.is_absolute() { return Err("请选择完整的美术素材库根目录".into()); }
    if !path.is_dir() { return Err("美术素材库根目录不存在或不是文件夹".into()); }
    Ok(path)
}

fn valid_scene_file(bytes: &[u8], scene_id: &str) -> bool {
    let Ok(value) = serde_json::from_slice::<Value>(bytes) else { return false; };
    value["schemaVersion"] == 1 && value["sceneId"] == scene_id
        && value.pointer("/scene/groups").is_some_and(Value::is_array)
        && value.pointer("/scene/points").is_some_and(Value::is_array)
}

fn validate(settings: &InteractionSettings) -> Result<(), String> {
    if settings.schema_version != 2 { return Err("不支持的配置版本".into()); }
    if !settings.drag_hold_seconds.is_finite() || !(0.15..=1.5).contains(&settings.drag_hold_seconds)
        || !settings.marker_opacity.is_finite() || !(0.08..=0.6).contains(&settings.marker_opacity) {
        return Err("长按时间或触点透明度超出范围".into());
    }
    let mut ids = HashSet::new();
    for scene in &settings.scenes {
        if scene_number(&scene.scene_id).is_none() || !ids.insert(&scene.scene_id) || scene.costume_id == 0
            || !scene.avatar_address.starts_with("IllustInventory_1/") || scene.avatar_address.contains("..") {
            return Err("互动编号、头像或服装配置无效".into());
        }
    }
    Ok(())
}

fn write_json(path: &Path, value: &impl Serialize) -> Result<(), String> {
    fs::create_dir_all(path.parent().ok_or("无效配置路径")?).map_err(|e| e.to_string())?;
    let temporary = path.with_extension("json.tmp");
    fs::write(&temporary, serde_json::to_vec_pretty(value).map_err(|e| e.to_string())?).map_err(|e| e.to_string())?;
    fs::rename(&temporary, path).map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn get_local_interactions(app: tauri::AppHandle, state: tauri::State<'_, AppState>) -> Result<InteractionStatus, String> {
    let assets_directory = asset_library_root(&state)?;
    let characters = crate::updater::game_data::load_characters(&app, &assets_directory).map_err(|e| e.to_string())?;
    let entries = catalog(&characters);
    let mut settings = read_settings_for_dir(&app, plugin_dir(&state).ok().as_deref(), &assets_directory, &entries)?;
    settings.assets_directory = assets_directory.to_string_lossy().into_owned();
    let dating_directory = assets_directory.join("audio").join("dating");
    let available = entries.into_iter().map(|entry| {
        let path = dating_directory.join(format!("{}.json", entry.scene_id));
        let status = match fs::read(path) {
            Ok(bytes) if valid_scene_file(&bytes, &entry.scene_id) => "ready",
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => "missing",
            _ => "invalid",
        };
        CatalogEntry { entry, export_status: status.into() }
    }).collect();
    Ok(InteractionStatus { settings, available, plugin: bundled_plugin_status(&state),
        settings_path: plugin_dir(&state).map(|p| p.join("settings.json").to_string_lossy().into_owned()).unwrap_or_default() })
}

#[tauri::command]
pub fn save_local_interactions(app: tauri::AppHandle, state: tauri::State<AppState>, mut settings: InteractionSettings) -> Result<(), String> {
    settings.assets_directory = asset_library_root(&state)?.to_string_lossy().into_owned();
    validate(&settings)?;
    for scene in &mut settings.scenes { scene.use_mods = true; }
    let target = plugin_dir(&state)?.join("settings.json");
    write_json(&target, &settings)?;
    write_json(&manager_path(&app)?, &settings)
}

#[tauri::command]
pub async fn install_local_interactions(app: tauri::AppHandle, state: tauri::State<'_, AppState>) -> Result<(), String> {
    let dir = plugin_dir(&state)?;
    let game = dir.ancestors().nth(3).ok_or("无效游戏路径")?;
    if !bundled_plugins::has_bepinex(game) {
        return Err("请先在模组加载器页面安装 BepInEx".into());
    }

    bundled_plugins::require_game_closed()?;
    let assets_directory = asset_library_root(&state)?;

    let _operation = bundled_plugins::lock_installation()?;
    if plugin_dir(&state)? != dir || !bundled_plugins::has_bepinex(game) {
        return Err("游戏目录或模组框架已改变，请重新安装".into());
    }
    bundled_plugins::require_game_closed()?;
    deploy_bundled_plugin(&app, game, &assets_directory, true).map(|_| ())
}

pub(crate) fn deploy_bundled_plugin(
    app: &tauri::AppHandle, game: &Path, assets: &Path, force: bool,
) -> Result<bool, String> {
    let dir = plugin_dir_for_game(game);
    bundled_plugins::deploy_dll(&dir.join(PLUGIN_DLL), PLUGIN, force, |path| {
        let characters = crate::updater::game_data::load_characters(app, assets).map_err(|e| e.to_string())?;
        let mut settings = read_settings_for_dir(app, Some(&dir), assets, &catalog(&characters))?;
        settings.assets_directory = assets.to_string_lossy().into_owned();
        bundled_plugins::write_initial_settings(path, &settings)
    })
}

#[cfg(test)]
mod tests {
    use super::{catalog as build_catalog, defaults as build_defaults, normalize as normalize_with_catalog, *};

    fn catalog() -> Vec<SceneEntry> {
        build_catalog(&serde_json::from_str(include_str!("../data/characters.json")).unwrap())
    }

    fn defaults() -> InteractionSettings { build_defaults(catalog()) }

    fn normalize(settings: InteractionSettings, legacy_native: bool) -> Result<InteractionSettings, String> {
        normalize_with_catalog(settings, legacy_native, &HashSet::from([7, 9, 12, 18, 19, 21]), &catalog())
    }

    #[test]
    fn master_switch_preserves_entries_and_supports_legacy_settings() {
        let mut json = serde_json::to_value(defaults()).unwrap();
        json.as_object_mut().unwrap().remove("enabled");
        let mut settings: InteractionSettings = serde_json::from_value(json).unwrap();
        assert!(settings.enabled);
        let scenes = serde_json::to_value(&settings.scenes).unwrap();
        settings.enabled = false;
        validate(&settings).unwrap();
        let saved = serde_json::to_value(&settings).unwrap();
        assert_eq!(saved["enabled"], false);
        assert_eq!(saved["scenes"], scenes);
        let loaded: InteractionSettings = serde_json::from_value(saved).unwrap();
        assert!(!loaded.enabled);
    }
    #[test]
    fn catalog_covers_all_dating_scenes_with_independent_modes() {
        assert_eq!(catalog().len(), 22);
        let settings = defaults();
        assert_eq!(settings.scenes.len(), 22);
        assert!(settings.scenes.iter().all(|s| s.native_enabled && !s.enabled));
        assert_eq!(catalog().iter().find(|s| s.scene_id == "illust_dating18").unwrap().costume_id, 396);
    }
    #[test]
    fn legacy_native_and_p_profiles_keep_their_behavior() {
        let mut old = defaults();
        old.schema_version = 1;
        old.scenes.retain(|s| matches!(scene_number(&s.scene_id), Some(7|9|12|18|19|21)));
        for s in &mut old.scenes { s.enabled = true; }
        let native = normalize(old.clone(), true).unwrap();
        assert_eq!(native.scenes.len(), 22);
        assert!(native.scenes.iter().all(|s| s.native_enabled && !s.enabled));
        let p = normalize(old, false).unwrap();
        assert_eq!(p.scenes.iter().filter(|s| s.enabled).count(), 6);
        assert_eq!(p.scenes.iter().filter(|s| s.native_enabled).count(), 16);
    }
    #[test]
    fn independent_toggles_and_global_switches_round_trip() {
        let mut settings = defaults();
        settings.scenes[0].enabled = true;
        settings.scenes[0].native_enabled = true;
        settings.scenes[0].use_mods = false;
        settings.scenes[1].enabled = false;
        settings.scenes[1].native_enabled = false;
        settings.p_entries_enabled = false;
        settings.native_entries_enabled = false;
        let loaded = normalize(serde_json::from_value(serde_json::to_value(&settings).unwrap()).unwrap(), true).unwrap();
        assert!(loaded.scenes[0].enabled && loaded.scenes[0].native_enabled);
        assert!(!loaded.scenes[1].enabled && !loaded.scenes[1].native_enabled);
        assert!(!loaded.p_entries_enabled && !loaded.native_entries_enabled);
        assert!(loaded.scenes.iter().all(|s| s.use_mods));
    }
    #[test]
    fn duplicate_and_invalid_entries_are_rejected() {
        let mut settings = defaults();
        assert!(validate(&settings).is_ok());
        settings.scenes.push(settings.scenes[0].clone());
        assert!(validate(&settings).is_err());
        settings.scenes.pop();
        settings.scenes[0].scene_id = "illust_dating../1".into();
        assert!(validate(&settings).is_err());
    }
}
