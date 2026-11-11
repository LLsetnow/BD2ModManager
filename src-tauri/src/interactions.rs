use std::{collections::HashSet, fs, path::{Path, PathBuf}, time::{SystemTime, UNIX_EPOCH}};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use tauri::Manager;
use crate::{AppState, utils::path::get_game_dir};

const PLUGIN: &[u8] = include_bytes!("../resources/BD2PreviewInteractions.dll");
const PLUGIN_VERSION: &str = "0.2.8";
const CHARACTERS: &str = include_str!("../data/characters.json");

#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SceneEntry {
    scene_id: String,
    costume_id: u32,
    avatar_address: String,
    title: String,
    enabled: bool,
    use_mods: bool,
}

#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct InteractionSettings {
    schema_version: u32,
    #[serde(default = "default_enabled")]
    enabled: bool,
    export_directory: String,
    audio_directory: String,
    assets_directory: String,
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
    expires_at_ms: Option<u64>,
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
            enabled: true, use_mods: true,
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
        schema_version: 1,
        enabled: true,
        export_directory: r"F:\Neowiz\mods\dating-interaction-configs".into(),
        audio_directory: r"F:\Neowiz\Brown-Dust-2-Asset-master\audio\dating".into(),
        assets_directory: r"F:\Neowiz\Brown-Dust-2-Asset-master".into(),
        drag_hold_seconds: 0.35, marker_opacity: 0.32,
        scenes: catalog().into_iter().filter(|e| matches!(scene_number(&e.scene_id), Some(7|9|12|18|19|21))).collect(),
    }
}

fn read_settings(app: &tauri::AppHandle, state: &AppState) -> Result<InteractionSettings, String> {
    // Prefer game settings so the page describes what the plugin will load.
    let game = plugin_dir(state).ok().map(|p| p.join("settings.json"));
    for path in game.into_iter().chain(std::iter::once(manager_path(app)?)) {
        if path.exists() {
            let settings = serde_json::from_slice(&fs::read(&path).map_err(|e| e.to_string())?).map_err(|e| format!("{}: {e}", path.display()))?;
            validate(&settings)?;
            return Ok(settings);
        }
    }
    Ok(defaults())
}

fn validate(settings: &InteractionSettings) -> Result<(), String> {
    if settings.schema_version != 1 { return Err("不支持的配置版本".into()); }
    for path in [&settings.export_directory, &settings.audio_directory, &settings.assets_directory] {
        if !Path::new(path).is_absolute() { return Err("请选择完整的本地目录路径".into()); }
    }
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
pub fn get_local_interactions(app: tauri::AppHandle, state: tauri::State<AppState>) -> Result<InteractionStatus, String> {
    let settings = read_settings(&app, &state)?;
    let now = SystemTime::now().duration_since(UNIX_EPOCH).map_err(|e| e.to_string())?.as_millis() as u64;
    let available = catalog().into_iter().map(|entry| {
        let path = Path::new(&settings.export_directory).join(format!("{}.json", entry.scene_id));
        let json = fs::read(&path).ok().and_then(|b| serde_json::from_slice::<Value>(&b).ok());
        let expiry = json.as_ref().and_then(|v| v["expiresAtMs"].as_u64());
        let status = match json {
            None if !path.exists() => "missing",
            Some(ref v) if v["schemaVersion"] == 1 && v["sceneId"] == entry.scene_id
                && v.pointer("/scene/groups").is_some_and(Value::is_array)
                && v.pointer("/scene/points").is_some_and(Value::is_array) => {
                    if expiry.unwrap_or(0) > now { "ready" } else { "expired" }
                },
            _ => "invalid",
        };
        CatalogEntry { entry, export_status: status.into(), expires_at_ms: expiry }
    }).collect();
    let dll = plugin_dir(&state).ok().map(|p| p.join("BD2PreviewInteractions.dll"));
    let installed = dll.as_ref().is_some_and(|p| p.exists());
    let plugin_current = dll.and_then(|p| fs::read(p).ok()).is_some_and(|b| b == PLUGIN);
    Ok(InteractionStatus { settings, available, installed, plugin_current, plugin_version: PLUGIN_VERSION,
        settings_path: plugin_dir(&state).map(|p| p.join("settings.json").to_string_lossy().into_owned()).unwrap_or_default() })
}

#[tauri::command]
pub fn save_local_interactions(app: tauri::AppHandle, state: tauri::State<AppState>, settings: InteractionSettings) -> Result<(), String> {
    validate(&settings)?;
    let target = plugin_dir(&state)?.join("settings.json");
    write_json(&target, &settings)?;
    write_json(&manager_path(&app)?, &settings)
}

#[tauri::command]
pub fn install_local_interactions(app: tauri::AppHandle, state: tauri::State<AppState>) -> Result<(), String> {
    let dir = plugin_dir(&state)?;
    let game = dir.ancestors().nth(3).ok_or("无效游戏路径")?;
    if !game.join("BepInEx/core/BepInEx.dll").exists() { return Err("请先在模组加载器页面安装 BepInEx".into()); }
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let target = dir.join("BD2PreviewInteractions.dll");
    if target.exists() {
        fs::OpenOptions::new().write(true).open(&target).map_err(|_| "插件正在使用或目录不可写，请退出游戏后安装".to_string())?;
    }
    let settings = read_settings(&app, &state)?;
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
    fn catalog_preserves_six_defaults_and_does_not_add_archira() {
        assert_eq!(catalog().len(), 22);
        let settings = defaults();
        assert_eq!(settings.scenes.len(), 6);
        assert!(!settings.scenes.iter().any(|s| s.scene_id == "illust_dating22"));
        assert_eq!(catalog().iter().find(|s| s.scene_id == "illust_dating18").unwrap().costume_id, 396);
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
