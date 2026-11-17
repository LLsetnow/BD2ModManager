use log::debug;
use std::path::Path;

use crate::{AppState, config::{AppConfig, PartialAppConfig}};

#[tauri::command]
pub fn get_settings(state: tauri::State<AppState>) -> AppConfig {
    let config = state.config.lock().unwrap();
    config.get_config().clone()
}

#[tauri::command]
pub fn set_settings(app: tauri::AppHandle, state: tauri::State<AppState>, value: PartialAppConfig) -> Result<(), String> {
    if let Some(directory) = value.asset_library_directory.as_deref() {
        if directory.trim().is_empty() || !Path::new(directory).is_absolute() {
            return Err("请选择完整的本地素材库根目录".into());
        }
    }
    let update_asset_library_directory = value.asset_library_directory.is_some();
    let mut config = state.config.lock().map_err(|_| "Lock poisoned")?;
    debug!("Updating config with: {:?}", value);
    config.update_config(value).map_err(|e| e.to_string())
        .map(|()| {
            drop(config);
            if update_asset_library_directory {
                crate::ipc::publish_preview_mods(app);
            }
        })
}
