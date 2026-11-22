use std::{env, path::{PathBuf}};

use tauri::{path::BaseDirectory, AppHandle, Manager};

use crate::config::BD2Config;

fn get_executable_dir() -> PathBuf {
    env::current_exe()
        .expect("Failed to get the current exe path")
        .parent()
        .unwrap()
        .to_path_buf()
}

pub fn get_default_profiles_dir(app: &AppHandle, on_executable_dir: bool) -> PathBuf {
    // receives (on_executable_dir: bool), if true then return executable_dir/profiles, if not appdata/profiles

    if on_executable_dir {
        let exec_dir = get_executable_dir();
        exec_dir
    } else {
        match app
            .app_handle()
            .path()
            .resolve("profiles", BaseDirectory::AppData)
        {
            Ok(path) => path,
            Err(_error) => {
                let exec_dir = get_executable_dir();
                exec_dir
            }
        }
    }
}

pub fn get_config_file_path() -> PathBuf {
    let exec_dir = get_executable_dir();
    exec_dir.join("config.json").to_path_buf()
}

pub fn get_default_staging_dir() -> PathBuf {
    let exec_dir = get_executable_dir();
    exec_dir.join("mods").to_path_buf()
}

pub fn get_default_asset_library_dir() -> PathBuf {
    get_executable_dir().join("asset")
}

pub fn share_asset_library_root(root: &std::path::Path) -> std::io::Result<()> {
    let Some(directory) = env::var_os("APPDATA") else { return Ok(()); };
    let directory = PathBuf::from(directory).join("LLsetnow").join("BD2AssetLibrary");
    std::fs::create_dir_all(&directory)?;
    let temporary = directory.join("root.txt.tmp");
    std::fs::write(&temporary, root.to_string_lossy().as_bytes())?;
    std::fs::rename(temporary, directory.join("root.txt"))
}

pub fn get_staging_dir(config: &BD2Config) -> PathBuf {
    match &config.staging_directory {
        Some(path) => return PathBuf::from(path),
        None => return get_default_staging_dir(),
    }
}

pub fn get_game_dir(config: &BD2Config) -> Option<PathBuf> {
    match &config.game_directory {
        Some(path) => return Some(PathBuf::from(path)),
        None => None,
    }
}

pub fn get_mod_preview_path(app: &AppHandle) -> Option<PathBuf> {
    if let Ok(path) = app
        .app_handle()
        .path()
        .resolve("tools", BaseDirectory::AppData)
    {
        Some(path.join("BD2ModPreview.exe").to_path_buf())
    } else {
        None
    }
}

pub fn get_characters_path(app_handle: &AppHandle) -> Option<PathBuf> {
    app_handle
    .path()
    .app_data_dir()
    .ok()
    .map(|dir| dir.join("characters.json"))
}
