use std::{collections::HashSet, fs, path::{Path, PathBuf}, time::Duration};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use tauri::Manager;
use crate::{AppState, utils::path::get_game_dir};

const PLUGIN: &[u8] = include_bytes!("../resources/BD2PreviewInteractions.dll");
const PLUGIN_VERSION: &str = "0.5.1";
const CHARACTERS: &str = include_str!("../data/characters.json");

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
    installed: bool,
    plugin_current: bool,
    plugin_version: &'static str,
    settings_path: String,
}

fn plugin_dir(state: &AppState) -> Result<PathBuf, String> {
    let config = state.config.lock().map_err(|e| e.to_string())?;
    let game = get_game_dir(&config).ok_or("请先在设置中选择游戏目录")?;
    if !game.join("BrownDust II.exe").exists() { return Err("游戏目录无效".into()); }
    Ok(game.join("BepInEx/plugins/BD2PreviewInteractions"))
}

fn manager_path(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    Ok(app.path().app_data_dir().map_err(|e| e.to_string())?.join("local-interactions.json"))
}

fn catalog() -> Vec<SceneEntry> {
    let data: Value = serde_json::from_str(CHARACTERS).expect("bundled characters");
    let Some(dating) = data["dating"].as_object() else { return vec![]; };
    let Some(characters) = data["characters"].as_array() else { return vec![]; };
    let mut entries: Vec<_> = dating.iter().filter_map(|(number, costume)| {
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

fn defaults() -> InteractionSettings {
    InteractionSettings {
        schema_version: 2,
        assets_directory: String::new(),
        enabled: true,
        p_entries_enabled: true,
        native_entries_enabled: true,
        drag_hold_seconds: 0.35, marker_opacity: 0.32,
        scenes: catalog(),
    }
}

fn legacy_native_mode(state: &AppState) -> bool {
    let cfg = plugin_dir(state).ok().and_then(|dir| dir.ancestors().nth(2)
        .map(|p| p.join("config/local.bd2.previewinteractions.cfg")));
    cfg.and_then(|p| fs::read_to_string(p).ok()).and_then(|text| text.lines().find_map(|line| {
        let (key, value) = line.split_once('=')?;
        (key.trim() == "UseNativeDatingEntries").then(|| value.trim().eq_ignore_ascii_case("true"))
    })).unwrap_or(true)
}

fn normalize(mut settings: InteractionSettings, legacy_native: bool) -> Result<InteractionSettings, String> {
    if settings.schema_version != 1 && settings.schema_version != 2 { return Err("不支持的配置版本".into()); }
    let was_legacy = settings.schema_version == 1;
    if settings.schema_version == 1 {
        for scene in &mut settings.scenes {
            let added = matches!(scene_number(&scene.scene_id), Some(7|9|12|18|19|21));
            scene.native_enabled = if added { legacy_native && scene.enabled } else { true };
            if added && legacy_native { scene.enabled = false; }
        }
        settings.schema_version = 2;
    }
    for mut entry in catalog() {
        if !settings.scenes.iter().any(|s| s.scene_id == entry.scene_id) {
            // Existing official entries were visible even in the old P-only UI.
            entry.native_enabled = was_legacy && !matches!(scene_number(&entry.scene_id), Some(7|9|12|18|19|21));
            settings.scenes.push(entry);
        }
    }
    // Keep the legacy field compatible while retiring the per-scene resource choice.
    for scene in &mut settings.scenes { scene.use_mods = true; }
    settings.scenes.sort_by_key(|entry| scene_number(&entry.scene_id).unwrap_or(0));
    validate(&settings)?;
    Ok(settings)
}

fn read_settings(app: &tauri::AppHandle, state: &AppState) -> Result<InteractionSettings, String> {
    // Prefer game settings so the page describes what the plugin will load.
    let game = plugin_dir(state).ok().map(|p| p.join("settings.json"));
    for path in game.into_iter().chain(std::iter::once(manager_path(app)?)) {
        if path.exists() {
            let settings = serde_json::from_slice(&fs::read(&path).map_err(|e| e.to_string())?).map_err(|e| format!("{}: {e}", path.display()))?;
            return normalize(settings, legacy_native_mode(state));
        }
    }
    Ok(defaults())
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

async fn ensure_dating_configs(directory: &Path) -> Result<(), String> {
    fs::create_dir_all(directory).map_err(|e| format!("无法创建约会素材目录：{e}"))?;
    let client = reqwest::Client::builder().timeout(Duration::from_secs(25)).build().map_err(|e| e.to_string())?;
    let bases = [
        "https://raw.githubusercontent.com/LLsetnow/Brown-Dust-2-Asset/master/audio/dating/",
        "https://cdn.jsdelivr.net/gh/LLsetnow/Brown-Dust-2-Asset@master/audio/dating/",
    ];
    for entry in catalog() {
        let path = directory.join(format!("{}.json", entry.scene_id));
        if path.is_file() { continue; }
        let mut failure = String::new();
        let mut downloaded = false;
        for base in bases {
            let response = match client.get(format!("{base}{}.json", entry.scene_id)).send().await {
                Ok(response) if response.status().is_success() => response,
                Ok(response) => { failure = format!("HTTP {}", response.status()); continue; },
                Err(error) => { failure = error.to_string(); continue; },
            };
            let bytes = match response.bytes().await {
                Ok(bytes) if bytes.len() <= 8 * 1024 * 1024 => bytes,
                Ok(_) => { failure = "文件超过 8 MB".into(); continue; },
                Err(error) => { failure = error.to_string(); continue; },
            };
            if !valid_scene_file(&bytes, &entry.scene_id) {
                failure = "JSON 格式或约会编号无效".into();
                continue;
            }
            let temporary = path.with_extension("json.download");
            fs::write(&temporary, &bytes).map_err(|e| e.to_string())?;
            fs::rename(&temporary, &path).map_err(|e| e.to_string())?;
            downloaded = true;
            break;
        }
        if !downloaded {
            return Err(format!("无法从 Brown-Dust-2-Asset 下载 {}.json：{failure}", entry.scene_id));
        }
    }
    Ok(())
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
    let mut settings = read_settings(&app, &state)?;
    let assets_directory = asset_library_root(&state)?;
    settings.assets_directory = assets_directory.to_string_lossy().into_owned();
    let dating_directory = assets_directory.join("audio").join("dating");
    ensure_dating_configs(&dating_directory).await?;
    let available = catalog().into_iter().map(|entry| {
        let path = dating_directory.join(format!("{}.json", entry.scene_id));
        let json = fs::read(&path).ok().and_then(|b| serde_json::from_slice::<Value>(&b).ok());
        let status = match json {
            None if !path.exists() => "missing",
            Some(ref v) if v["schemaVersion"] == 1 && v["sceneId"] == entry.scene_id
                && v.pointer("/scene/groups").is_some_and(Value::is_array)
                && v.pointer("/scene/points").is_some_and(Value::is_array) => "ready",
            _ => "invalid",
        };
        CatalogEntry { entry, export_status: status.into() }
    }).collect();
    let dll = plugin_dir(&state).ok().map(|p| p.join("BD2PreviewInteractions.dll"));
    let installed = dll.as_ref().is_some_and(|p| p.exists());
    let plugin_current = dll.and_then(|p| fs::read(p).ok()).is_some_and(|b| b == PLUGIN);
    Ok(InteractionStatus { settings, available, installed, plugin_current, plugin_version: PLUGIN_VERSION,
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
    if !game.join("BepInEx/core/BepInEx.dll").exists() { return Err("请先在模组加载器页面安装 BepInEx".into()); }
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let target = dir.join("BD2PreviewInteractions.dll");
    if target.exists() {
        fs::OpenOptions::new().write(true).open(&target).map_err(|_| "插件正在使用或目录不可写，请退出游戏后安装".to_string())?;
    }
    let mut settings = read_settings(&app, &state)?;
    let assets_directory = asset_library_root(&state)?;
    settings.assets_directory = assets_directory.to_string_lossy().into_owned();
    let dating_directory = assets_directory.join("audio").join("dating");
    ensure_dating_configs(&dating_directory).await?;
    let temporary = dir.join("BD2PreviewInteractions.dll.tmp");
    fs::write(&temporary, PLUGIN).map_err(|e| e.to_string())?;
    fs::rename(&temporary, &target).map_err(|e| format!("请退出游戏后安装插件：{e}"))?;
    write_json(&dir.join("settings.json"), &settings)?;
    write_json(&manager_path(&app)?, &settings)
}

#[cfg(test)]
mod tests {
    use super::*;
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
