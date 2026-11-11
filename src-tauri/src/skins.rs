use std::{collections::{BTreeMap, HashSet}, fs, path::{Path, PathBuf}};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use tauri::Manager;
use crate::{AppState, utils::path::get_game_dir};

const PLUGIN: &[u8] = include_bytes!("../resources/BD2CostumeSkins.dll");
const VERSION: &str = "0.1.1";

#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SkinMapping {
    source_id: String,
    target_id: String,
    label: String,
    enabled: bool,
}

#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SkinSettings {
    schema_version: u32,
    #[serde(default = "default_enabled")]
    enabled: bool,
    mappings: Vec<SkinMapping>,
    selected_skins: BTreeMap<String, String>,
}

fn default_enabled() -> bool { true }

#[derive(Deserialize)]
#[serde(tag = "type", rename_all = "camelCase", rename_all_fields = "camelCase")]
pub enum SkinChange {
    SetEnabled { enabled: bool },
    AddMapping { mapping: SkinMapping },
    RemoveMapping { source_id: String, target_id: String },
    UpdateMapping { source_id: String, target_id: String, label: Option<String>, enabled: Option<bool> },
    SelectSkin { source_id: String, target_id: String },
}

fn apply_change(current: &SkinSettings, change: SkinChange) -> Result<SkinSettings, String> {
    let mut next = current.clone();
    match change {
        SkinChange::SetEnabled { enabled } => next.enabled = enabled,
        SkinChange::AddMapping { mapping } => {
            next.selected_skins.entry(mapping.source_id.clone()).or_insert_with(|| mapping.source_id.clone());
            next.mappings.push(mapping);
        },
        SkinChange::RemoveMapping { source_id, target_id } => {
            let index = next.mappings.iter().position(|m| m.source_id == source_id && m.target_id == target_id).ok_or("该映射已不存在，请刷新页面")?;
            next.mappings.remove(index);
            if !next.mappings.iter().any(|m| m.source_id == source_id) { next.selected_skins.remove(&source_id); }
            else if next.selected_skins.get(&source_id) == Some(&target_id) { next.selected_skins.insert(source_id.clone(), source_id); }
        },
        SkinChange::UpdateMapping { source_id, target_id, label, enabled } => {
            let row = next.mappings.iter_mut().find(|m| m.source_id == source_id && m.target_id == target_id).ok_or("该映射已不存在，请刷新页面")?;
            if let Some(label) = label { row.label = label; }
            if let Some(enabled) = enabled {
                row.enabled = enabled;
                if !enabled && next.selected_skins.get(&source_id) == Some(&target_id) { next.selected_skins.insert(source_id.clone(), source_id); }
            }
        },
        SkinChange::SelectSkin { source_id, target_id } => {
            if source_id != target_id && !next.mappings.iter().any(|m| m.enabled && m.source_id == source_id && m.target_id == target_id) {
                return Err("目标皮肤尚未启用".into());
            }
            next.selected_skins.insert(source_id, target_id);
        },
    }
    validate(&next)?;
    Ok(next)
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Costume {
    id: String,
    character_id: u64,
    name: String,
    costume: String,
    skin_type: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SkinStatus {
    settings: SkinSettings,
    costumes: Vec<Costume>,
    installed: bool,
    plugin_current: bool,
    plugin_version: &'static str,
    settings_path: String,
}

fn defaults() -> SkinSettings {
    SkinSettings {
        schema_version: 1,
        enabled: true,
        mappings: vec![SkinMapping { source_id: "001207".into(), target_id: "001297".into(), label: "甜蜜的诱惑".into(), enabled: true }],
        selected_skins: BTreeMap::from([("001207".into(), "001207".into())]),
    }
}

fn catalog() -> Vec<Costume> {
    let data: Value = serde_json::from_str(include_str!("../data/characters.json")).expect("bundled character catalog");
    data["characters"].as_array().into_iter().flatten().filter_map(|row| Some(Costume {
        id: row["costume_id"].as_str()?.into(), character_id: row["character_id"].as_u64()?,
        name: row.pointer("/character_name/cn")?.as_str()?.into(),
        costume: row.pointer("/costume_name/cn")?.as_str()?.into(),
        skin_type: row["skin_type"].as_str().unwrap_or("normal").into(),
    })).collect()
}

fn valid_id(id: &str) -> bool { id.len() == 6 && id != "000000" && id.bytes().all(|b| b.is_ascii_digit()) }

fn validate(settings: &SkinSettings) -> Result<(), String> {
    if settings.schema_version != 1 { return Err("不支持的皮肤配置版本".into()); }
    let costumes = catalog();
    let mut pairs = HashSet::new();
    let sources: HashSet<_> = settings.mappings.iter().map(|m| &m.source_id).collect();
    for mapping in &settings.mappings {
        if !valid_id(&mapping.source_id) || !valid_id(&mapping.target_id) || mapping.source_id == mapping.target_id
            || mapping.label.chars().count() > 100 || !pairs.insert((&mapping.source_id, &mapping.target_id)) {
            return Err("皮肤映射编号或名称无效，或映射重复".into());
        }
        if sources.contains(&mapping.target_id) { return Err("皮肤目标不能同时作为映射来源".into()); }
        let source = costumes.iter().find(|c| c.id == mapping.source_id).ok_or("找不到来源服装")?;
        let target = costumes.iter().find(|c| c.id == mapping.target_id).ok_or("找不到目标服装")?;
        if source.character_id != target.character_id { return Err("请选择同一角色的皮肤".into()); }
    }
    for (source, target) in &settings.selected_skins {
        if !sources.contains(source) || !valid_id(target) || (source != target
            && !settings.mappings.iter().any(|m| &m.source_id == source && &m.target_id == target)) {
            return Err("当前皮肤不属于该服装的映射".into());
        }
    }
    Ok(())
}

fn plugin_dir(state: &AppState) -> Result<PathBuf, String> {
    let config = state.config.lock().map_err(|e| e.to_string())?;
    let game = get_game_dir(&config).ok_or("请先在设置中选择游戏目录")?;
    if !game.join("BrownDust II.exe").exists() { return Err("游戏目录无效".into()); }
    Ok(game.join("BepInEx/plugins/BD2CostumeSkins"))
}

fn manager_path(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    Ok(app.path().app_data_dir().map_err(|e| e.to_string())?.join("costume-skins.json"))
}

fn read_settings(app: &tauri::AppHandle, state: &AppState) -> Result<SkinSettings, String> {
    let game = plugin_dir(state).ok().map(|d| d.join("settings.json"));
    for path in game.into_iter().chain(std::iter::once(manager_path(app)?)) {
        if path.exists() {
            let settings = serde_json::from_slice(&fs::read(&path).map_err(|e| e.to_string())?)
                .map_err(|e| format!("{}: {e}", path.display()))?;
            validate(&settings)?;
            return Ok(settings);
        }
    }
    Ok(defaults())
}

fn write_json(path: &Path, settings: &SkinSettings) -> Result<(), String> {
    fs::create_dir_all(path.parent().ok_or("配置路径无效")?).map_err(|e| e.to_string())?;
    let temporary = path.with_extension("manager.tmp");
    fs::write(&temporary, serde_json::to_vec_pretty(settings).map_err(|e| e.to_string())?).map_err(|e| e.to_string())?;
    fs::rename(temporary, path).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn get_costume_skins(app: tauri::AppHandle, state: tauri::State<AppState>) -> Result<SkinStatus, String> {
    let settings = read_settings(&app, &state)?;
    let dir = plugin_dir(&state).ok();
    let dll = dir.as_ref().map(|d| d.join("BD2CostumeSkins.dll"));
    Ok(SkinStatus {
        settings, costumes: catalog(), installed: dll.as_ref().is_some_and(|p| p.exists()),
        plugin_current: dll.and_then(|p| fs::read(p).ok()).is_some_and(|bytes| bytes == PLUGIN),
        plugin_version: VERSION, settings_path: dir.map(|p| p.join("settings.json").to_string_lossy().into_owned()).unwrap_or_default(),
    })
}

#[tauri::command]
pub fn save_costume_skins(app: tauri::AppHandle, state: tauri::State<AppState>, settings: SkinSettings) -> Result<(), String> {
    validate(&settings)?;
    write_json(&plugin_dir(&state)?.join("settings.json"), &settings)?;
    write_json(&manager_path(&app)?, &settings)
}

#[tauri::command]
pub fn update_costume_skins(app: tauri::AppHandle, state: tauri::State<AppState>, change: SkinChange) -> Result<SkinSettings, String> {
    // Apply only this edit to the latest file, preserving Skin selections made in the game.
    let settings = apply_change(&read_settings(&app, &state)?, change)?;
    write_json(&plugin_dir(&state)?.join("settings.json"), &settings)?;
    write_json(&manager_path(&app)?, &settings)?;
    Ok(settings)
}

#[tauri::command]
pub fn install_costume_skins(app: tauri::AppHandle, state: tauri::State<AppState>) -> Result<(), String> {
    let dir = plugin_dir(&state)?;
    let game = dir.ancestors().nth(3).ok_or("无效游戏目录")?;
    if !game.join("BepInEx/core/BepInEx.dll").exists() { return Err("请先安装 BepInEx 模组加载器".into()); }
    let settings = read_settings(&app, &state)?;
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let dll = dir.join("BD2CostumeSkins.dll");
    if dll.exists() { fs::OpenOptions::new().write(true).open(&dll).map_err(|_| "插件被占用，请退出游戏后安装")?; }
    let temporary = dir.join("BD2CostumeSkins.dll.tmp");
    fs::write(&temporary, PLUGIN).map_err(|e| e.to_string())?;
    fs::rename(temporary, dll).map_err(|e| format!("安装失败，请退出游戏后重试：{e}"))?;
    write_json(&dir.join("settings.json"), &settings)?;
    write_json(&manager_path(&app)?, &settings)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn edits_preserve_current_game_selection_and_unrelated_settings() {
        let mut current = defaults();
        current.selected_skins.insert("001207".into(), "001297".into());
        let edited = apply_change(&current, SkinChange::UpdateMapping { source_id: "001207".into(), target_id: "001297".into(), label: Some("新名称".into()), enabled: None }).unwrap();
        assert_eq!(edited.selected_skins, current.selected_skins);
        assert_eq!(edited.mappings[0].label, "新名称");
        let disabled = apply_change(&edited, SkinChange::SetEnabled { enabled: false }).unwrap();
        assert_eq!(disabled.selected_skins, current.selected_skins);
        assert!(!disabled.enabled);
    }
    #[test] fn disabling_or_removing_selected_mapping_restores_valid_choice() {
        let mut current = defaults(); current.selected_skins.insert("001207".into(), "001297".into());
        let disabled = apply_change(&current, SkinChange::UpdateMapping { source_id: "001207".into(), target_id: "001297".into(), label: None, enabled: Some(false) }).unwrap();
        assert_eq!(disabled.selected_skins["001207"], "001207");
        assert!(apply_change(&disabled, SkinChange::SelectSkin { source_id: "001207".into(), target_id: "001297".into() }).is_err());
        let removed = apply_change(&current, SkinChange::RemoveMapping { source_id: "001207".into(), target_id: "001297".into() }).unwrap();
        assert!(removed.mappings.is_empty() && removed.selected_skins.is_empty());
    }
    #[test] fn invalid_add_does_not_change_original_and_camel_case_actions_deserialize() {
        let current = defaults();
        let duplicate = current.mappings[0].clone();
        assert!(apply_change(&current, SkinChange::AddMapping { mapping: duplicate }).is_err());
        assert_eq!(current.mappings.len(), 1);
        let change = serde_json::from_value(serde_json::json!({"type":"selectSkin","sourceId":"001207","targetId":"001297"})).unwrap();
        assert_eq!(apply_change(&current, change).unwrap().selected_skins["001207"], "001297");
    }
    #[test] fn master_switch_preserves_choices_and_supports_legacy_settings() {
        let mut json = serde_json::to_value(defaults()).unwrap();
        json.as_object_mut().unwrap().remove("enabled");
        let mut settings: SkinSettings = serde_json::from_value(json).unwrap();
        assert!(settings.enabled);
        settings.selected_skins.insert("001207".into(), "001297".into());
        let mappings = serde_json::to_value(&settings.mappings).unwrap();
        settings.enabled = false;
        validate(&settings).unwrap();
        let saved = serde_json::to_value(&settings).unwrap();
        assert_eq!(saved["mappings"], mappings);
        let loaded: SkinSettings = serde_json::from_value(saved).unwrap();
        assert!(!loaded.enabled);
        assert_eq!(loaded.selected_skins["001207"], "001297");
    }
    #[test] fn default_liatris_mapping_is_valid() { validate(&defaults()).unwrap(); }
    #[test] fn rejects_cross_character_and_unknown_ids() {
        let mut settings = defaults(); settings.mappings[0].target_id = "000202".into();
        assert!(validate(&settings).is_err());
        settings.mappings[0].target_id = "999999".into(); assert!(validate(&settings).is_err());
    }
    #[test] fn rejects_cycles_duplicates_and_unrelated_selection() {
        let mut settings = defaults(); settings.mappings.push(settings.mappings[0].clone());
        assert!(validate(&settings).is_err());
        settings.mappings[1].source_id = "001297".into(); settings.mappings[1].target_id = "001207".into();
        assert!(validate(&settings).is_err());
        settings.mappings.pop(); settings.selected_skins.insert("001207".into(), "001201".into());
        assert!(validate(&settings).is_err());
    }
    #[test] fn disabled_selection_can_be_preserved_and_ids_keep_leading_zeroes() {
        let mut settings = defaults(); settings.mappings[0].enabled = false;
        settings.selected_skins.insert("001207".into(), "001297".into()); validate(&settings).unwrap();
        let bytes = serde_json::to_vec(&settings).unwrap();
        let restored: SkinSettings = serde_json::from_slice(&bytes).unwrap();
        assert_eq!(restored.mappings[0].source_id, "001207");
    }
}
