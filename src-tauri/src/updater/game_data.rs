use log::{debug, info, warn};
use std::path::PathBuf;
use serde::Serialize;
use tauri::{ipc::Channel, Manager};

use crate::updater::commands::{GameDataDownloadProgress, GameDataEvent};

#[derive(Debug, thiserror::Error)]
pub enum GameDataUpdateError {
    #[error("failed to check for game data updates: {0}")]
    CheckFailed(String),
    #[error("failed to download game data: {0}")]
    DownloadFailed(String),
    #[error("failed to save game data: {0}")]
    SaveFailed(String),
    #[error("invalid game data: {0}")]
    InvalidData(String),
}

impl Serialize for GameDataUpdateError {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        serializer.serialize_str(&self.to_string())
    }
}

const STANDING_ASSETS_BASE_URL: &str = "https://raw.githubusercontent.com/bruhnn/BD2ModManager/refs/heads/main/public/characters/standing/";
const HEADS_ASSETS_BASE_URL: &str = "https://raw.githubusercontent.com/bruhnn/BD2ModManager/refs/heads/main/public/characters/heads/";
const ASSET_BASE_URL: &str = "https://raw.githubusercontent.com/LLsetnow/Brown-Dust-2-Asset/refs/heads/master/";
const BUNDLED_CHARACTERS: &str = include_str!("../../data/characters.json");

fn valid_characters(value: &serde_json::Value) -> bool {
    value.get("version").and_then(|version| version.as_str()).is_some()
        && value.get("characters").and_then(|characters| characters.as_array()).is_some()
        && value.get("dating").and_then(|dating| dating.as_object()).is_some()
}

pub fn load_characters(app_handle: &tauri::AppHandle, asset_root: &std::path::Path) -> Result<serde_json::Value, GameDataUpdateError> {
    let installed = super::catalogs::read_installed(asset_root, "characters.json");
    let previous = crate::utils::path::get_characters_path(app_handle)
        .and_then(|path| std::fs::read(path).ok());
    let value = [installed.as_deref(), previous.as_deref(), Some(BUNDLED_CHARACTERS.as_bytes())]
        .into_iter().flatten()
        .filter_map(|bytes| serde_json::from_slice(bytes).ok())
        .find(valid_characters);
    value.ok_or_else(|| GameDataUpdateError::InvalidData("no usable characters.json catalog is available".into()))
}

pub(crate) fn character_image_path(character: &serde_json::Value, category: &str) -> Option<String> {
    let (key, directory) = match category {
        "standing" => ("character_image", "ui/illust/illust_inven_char"),
        "heads" => ("head_image", "ui/icon/icon_char"),
        _ => return None,
    };
    let name = character["assets"][key].as_str()?;
    let stem = name.strip_suffix(".png")?;
    if stem.is_empty() || !stem.bytes().all(|byte| byte.is_ascii_alphanumeric() || byte == b'_') {
        return None;
    }
    Some(format!("{directory}/{name}"))
}

async fn download_missing_assets(
    app_handle: &tauri::AppHandle,
    asset_root: &std::path::Path,
    characters: &serde_json::Value,
    on_event: &Channel<GameDataEvent>
) -> Result<bool, GameDataUpdateError> {
    let client = reqwest::Client::builder()
        .timeout(std::time::Duration::from_secs(15))
        .build()
        .map_err(|e| GameDataUpdateError::DownloadFailed(e.to_string()))?;

    let standing_characters_dir = app_handle
        .path()
        .app_data_dir()
        .map_err(|e| GameDataUpdateError::SaveFailed(e.to_string()))?
        .join("assets")
        .join("standing");

    let heads_characters_dir = app_handle
        .path()
        .app_data_dir()
        .map_err(|e| GameDataUpdateError::SaveFailed(e.to_string()))?
        .join("assets")
        .join("heads");

    tokio::fs::create_dir_all(&standing_characters_dir)
        .await
        .map_err(|e| GameDataUpdateError::SaveFailed(e.to_string()))?;

    tokio::fs::create_dir_all(&heads_characters_dir)
        .await
        .map_err(|e| GameDataUpdateError::SaveFailed(e.to_string()))?;

    let bundled_assets: std::collections::HashSet<String> = app_handle
        .asset_resolver()
        .iter()
        .map(|(path, _)| path.to_string())
        .collect();

    let characters_w_missing_assets: Vec<(&serde_json::Value, &str, bool, bool)> = {
        // #[cfg(debug_assertions)]
        // {
        //     vec![]
        // }

        characters["characters"]
            .as_array()
            .ok_or_else(|| {
                GameDataUpdateError::InvalidData("characters must be an array".to_string())
            })?
            .iter()
            .filter_map(|character| {
                let id = character["id"].as_str()?;
                if id.len() != 6 || !id.bytes().all(|byte| byte.is_ascii_digit()) { return None; }

                let is_standing_bundled =
                    bundled_assets.contains(&format!("/characters/standing/{}.png", id));
                let is_head_bundled =
                    bundled_assets.contains(&format!("/characters/heads/{}.png", id));

                // check if is bundled and not already download
                let is_standing_downloaded =
                    standing_characters_dir.join(format!("{}.png", id)).exists();
                let is_head_downloaded =
                    heads_characters_dir.join(format!("{}.png", id)).exists();

                debug!(
                    "[{}] bundled=(standing:{}, head:{}) downloaded=(standing:{}, head:{})",
                    id,
                    is_standing_bundled,
                    is_head_bundled,
                    is_standing_downloaded,
                    is_head_downloaded
                );

                let has_local_image = |category| character_image_path(character, category)
                    .is_some_and(|path| asset_root.join(path).is_file());
                let must_download_standing = !is_standing_bundled && !is_standing_downloaded && !has_local_image("standing");
                let must_download_head = !is_head_bundled && !is_head_downloaded && !has_local_image("heads");

                if must_download_standing || must_download_head {
                    Some((character, id, must_download_standing, must_download_head))
                } else {
                    None
                }
            })
            .collect()
    };

    let total = characters_w_missing_assets
        .iter()
        .map(|(_, _, must_download_standing, must_download_head)| {
            (*must_download_standing as usize) + (*must_download_head as usize)
        })
        .sum::<usize>();

    if total == 0 {
        info!("No character assets found to download");
        return Ok(false);
    }

    info!("Found {total} missing character assets, downloading...");

    let _ = on_event.send(GameDataEvent::Updating {
        resource: "char_assets".to_string(),
        percentage: 0,
        label: "downloading_assets".to_string(),
        download: Some(GameDataDownloadProgress {
            percentage: Some(0),
            current: Some(0),
            total: Some(total as u32),
        }),
    });

    let mut current = 0;
    let mut downloaded = false;

    for (character, character_id, must_download_standing, must_download_head) in
        characters_w_missing_assets
    {
        info!("Downloading missing assets for {character_id}; standing: {must_download_standing}, head: {must_download_head}");

        if must_download_standing {
            let standing_url = character_image_path(character, "standing")
                .map(|path| format!("{ASSET_BASE_URL}{path}"))
                .unwrap_or_else(|| format!("{STANDING_ASSETS_BASE_URL}{character_id}.png"));
            let response = client.get(&standing_url).send().await.map_err(|e| {
                GameDataUpdateError::DownloadFailed(format!(
                    "standing asset for {character_id}: {e}"
                ))
            })?;

            if response.status().is_success() {
                let bytes = response.bytes().await.map_err(|e| {
                    GameDataUpdateError::DownloadFailed(format!(
                        "standing asset for {character_id}: {e}"
                    ))
                })?;

                tokio::fs::write(
                    standing_characters_dir.join(format!("{}.png", character_id)),
                    &bytes,
                )
                .await
                .map_err(|e| {
                    GameDataUpdateError::SaveFailed(format!(
                        "standing asset for {character_id}: {e}"
                    ))
                })?;
                downloaded = true;
            } else {
                warn!("Standing asset not found for {character_id} at URL: {standing_url}");
            }

            current += 1;

            let percentage = (current as f32 / total as f32 * 100.0) as u8;

            let _ = on_event.send(GameDataEvent::Updating {
                resource: "char_assets".to_string(),
                percentage,
                label: character_id.to_owned(),
                download: Some(GameDataDownloadProgress {
                    percentage: Some(percentage),
                    current: Some(current as u32),
                    total: Some(total as u32),
                }),
            });
        }

        if must_download_head {
            let head_url = character_image_path(character, "heads")
                .map(|path| format!("{ASSET_BASE_URL}{path}"))
                .unwrap_or_else(|| format!("{HEADS_ASSETS_BASE_URL}{character_id}.png"));
            let response = client.get(&head_url).send().await.map_err(|e| {
                GameDataUpdateError::DownloadFailed(format!("head asset for {character_id}: {e}"))
            })?;

            if response.status().is_success() {
                let bytes = response.bytes().await.map_err(|e| {
                    GameDataUpdateError::DownloadFailed(format!(
                        "head asset for {character_id}: {e}"
                    ))
                })?;

                tokio::fs::write(
                    heads_characters_dir.join(format!("{}.png", character_id)),
                    &bytes,
                )
                .await
                .map_err(|e| {
                    GameDataUpdateError::SaveFailed(format!("head asset for {character_id}: {e}"))
                })?;
                downloaded = true;
            } else {
                warn!("Head asset not found for {character_id} at URL: {head_url}");
            }

            current += 1;

            let percentage = (current as f32 / total as f32 * 100.0) as u8;

            let _ = on_event.send(GameDataEvent::Updating {
                resource: "char_assets".to_string(),
                percentage,
                label: character_id.to_owned(),
                download: Some(GameDataDownloadProgress {
                    percentage: Some(percentage),
                    current: Some(current as u32),
                    total: Some(total as u32),
                }),
            });
        }
    }

    Ok(downloaded)
}

pub async fn update_game_data(
    app_handle: tauri::AppHandle,
    asset_root: PathBuf,
    game_version: Option<String>,
    download_character_images: bool,
    on_event: Channel<GameDataEvent>,
) -> Result<bool, GameDataUpdateError> {
    let _ = on_event.send(GameDataEvent::Started);

    let catalogs_updated = super::catalogs::update_catalogs(&asset_root, game_version.as_deref(), &on_event).await?;
    let mut images_updated = false;
    if download_character_images {
        let characters = load_characters(&app_handle, &asset_root)?;
        match download_missing_assets(&app_handle, &asset_root, &characters, &on_event).await {
            Ok(updated) => images_updated = updated,
            Err(error) => warn!("Could not download character images: {error}"),
        }
        let _ = on_event.send(GameDataEvent::Updated {
            resource: "char_assets".into(),
            label: "assets_updated".into(),
        });
    }
    let _ = on_event.send(GameDataEvent::Finished);
    Ok(catalogs_updated || images_updated)
}
