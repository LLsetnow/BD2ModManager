use std::{
    fs,
    io::{self, Write},
    path::Path,
    sync::{Mutex, MutexGuard},
};

use serde::Serialize;
use tauri::AppHandle;
use windows_sys::Win32::{
    Foundation::{CloseHandle, ERROR_NO_MORE_FILES, INVALID_HANDLE_VALUE},
    System::Diagnostics::ToolHelp::{
        CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W,
        TH32CS_SNAPPROCESS,
    },
};

static INSTALL_LOCK: Mutex<()> = Mutex::new(());

pub(crate) fn lock_installation() -> Result<MutexGuard<'static, ()>, String> {
    INSTALL_LOCK.lock().map_err(|e| e.to_string())
}

pub(crate) fn has_bepinex(game: &Path) -> bool {
    game.join("BepInEx/core/BepInEx.dll").is_file()
}

fn game_is_running() -> io::Result<bool> {
    // The game's executable name is the same for each supported installation.
    unsafe {
        let snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if snapshot == INVALID_HANDLE_VALUE {
            return Err(io::Error::last_os_error());
        }

        let mut entry = PROCESSENTRY32W::default();
        entry.dwSize = std::mem::size_of::<PROCESSENTRY32W>() as u32;
        let mut next = Process32FirstW(snapshot, &mut entry);
        let result = loop {
            if next == 0 {
                let error = io::Error::last_os_error();
                break if error.raw_os_error() == Some(ERROR_NO_MORE_FILES as i32) {
                    Ok(false)
                } else {
                    Err(error)
                };
            }
            let filename = entry.szExeFile.split(|&c| c == 0).next().unwrap_or_default();
            if String::from_utf16_lossy(filename).eq_ignore_ascii_case("BrownDust II.exe") {
                break Ok(true);
            }
            next = Process32NextW(snapshot, &mut entry);
        };
        CloseHandle(snapshot);
        result
    }
}

pub(crate) fn require_game_closed() -> Result<(), String> {
    if game_is_running().map_err(|e| format!("无法检查游戏是否正在运行：{e}"))? {
        return Err("请退出游戏后再安装或更新插件".into());
    }
    Ok(())
}

pub(crate) fn write_initial_settings<T: Serialize>(path: &Path, settings: &T) -> Result<(), String> {
    let directory = path.parent().ok_or("无效的插件设置路径")?;
    let mut pending = tempfile::NamedTempFile::new_in(directory).map_err(|e| e.to_string())?;
    serde_json::to_writer_pretty(pending.as_file_mut(), settings).map_err(|e| e.to_string())?;
    match pending.persist_noclobber(path) {
        Ok(_) => Ok(()),
        Err(error) if error.error.kind() == io::ErrorKind::AlreadyExists => Ok(()),
        Err(error) => Err(format!("无法写入插件设置：{}: {error}", path.display())),
    }
}

pub(crate) fn deploy_dll(
    dll: &Path,
    bytes: &[u8],
    force: bool,
    initialize_settings: impl FnOnce(&Path) -> Result<(), String>,
) -> Result<bool, String> {
    match fs::read(dll) {
        Ok(installed) if !force && installed == bytes => return Ok(false),
        Ok(_) => {}
        Err(error) if error.kind() == io::ErrorKind::NotFound => {}
        Err(error) => return Err(format!("无法读取插件 {}: {error}", dll.display())),
    }
    let dir = dll.parent().ok_or("无效的插件路径")?;
    fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let settings_path = dir.join("settings.json");
    if !settings_path.try_exists().map_err(|e| e.to_string())? {
        initialize_settings(&settings_path)?;
    }

    let mut pending = tempfile::NamedTempFile::new_in(dir).map_err(|e| e.to_string())?;
    pending.write_all(bytes).map_err(|e| e.to_string())?;
    pending.persist(dll).map_err(|e| format!("无法替换插件 {}: {e}", dll.display()))?;
    Ok(true)
}

#[derive(Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct BundledPluginSync {
    pub changed: bool,
    pub game_running: bool,
    pub errors: Vec<String>,
}

// The caller holds INSTALL_LOCK for the whole operation, including any game launch that follows.
pub(crate) fn synchronize(
    app: &AppHandle,
    game: &Path,
    assets: &Path,
) -> Result<BundledPluginSync, String> {
    let mut result = BundledPluginSync::default();
    if !has_bepinex(game) {
        return Ok(result);
    }
    let dating_needs_update = !crate::interactions::plugin_status_for_game(game).plugin_current;
    let skins_need_update = !crate::skins::plugin_status_for_game(game).plugin_current;
    if !dating_needs_update && !skins_need_update {
        return Ok(result);
    }
    if game_is_running().map_err(|e| format!("无法检查游戏是否正在运行：{e}"))? {
        result.game_running = true;
        return Ok(result);
    }

    if dating_needs_update {
        match crate::interactions::deploy_bundled_plugin(app, game, assets, false) {
            Ok(changed) => result.changed |= changed,
            Err(error) => result.errors.push(format!("BD2PreviewInteractions: {error}")),
        }
    }
    if skins_need_update {
        match crate::skins::deploy_bundled_plugin(app, game, false) {
            Ok(changed) => result.changed |= changed,
            Err(error) => result.errors.push(format!("BD2CostumeSkins: {error}")),
        }
    }
    Ok(result)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn installs_and_updates_inside_a_chinese_path_without_rewriting_settings() {
        let root = tempfile::tempdir().unwrap();
        let dir = root.path().join("游戏目录").join("皮肤替换");
        let dll = dir.join("Plugin.dll");
        let settings = dir.join("settings.json");
        assert!(deploy_dll(&dll, b"first", false, |path| {
            write_initial_settings(path, &serde_json::json!({"selectedSkins": {"000206": "000296"}}))
        }).unwrap());
        let saved = fs::read(&settings).unwrap();
        assert_eq!(fs::read(&dll).unwrap(), b"first");

        assert!(deploy_dll(&dll, b"second", false, |_| panic!("Existing settings must be kept")).unwrap());
        assert!(!deploy_dll(&dll, b"second", false, |_| panic!("Up-to-date DLLs must be skipped")).unwrap());
        assert_eq!(fs::read(&dll).unwrap(), b"second");
        assert_eq!(fs::read(&settings).unwrap(), saved);
    }

    #[test]
    fn a_failed_settings_write_leaves_the_existing_binary_intact() {
        let root = tempfile::tempdir().unwrap();
        let dll = root.path().join("Plugin.dll");
        fs::write(&dll, b"existing").unwrap();

        let result = deploy_dll(&dll, b"replacement", false, |_| Err("settings error".into()));
        assert_eq!(result, Err("settings error".into()));
        assert_eq!(fs::read(&dll).unwrap(), b"existing");
    }
}
