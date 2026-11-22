use serde::Deserialize;
use serde_json::Value;
use sha2::{Digest, Sha256};
use std::path::Path;
use tauri::ipc::Channel;

use crate::updater::commands::{GameDataDownloadProgress, GameDataEvent};

use super::game_data::GameDataUpdateError;

const MANIFEST: &str = "catalog-manifest.json";
const BASES: [&str; 2] = [
    "https://raw.githubusercontent.com/LLsetnow/Brown-Dust-2-Asset/refs/heads/master/",
    "https://cdn.jsdelivr.net/gh/LLsetnow/Brown-Dust-2-Asset@master/",
];
const MAX_BYTES: usize = 64 * 1024 * 1024;

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Manifest {
    schema_version: u32,
    catalog_version: String,
    game_version: String,
    files: Vec<FileEntry>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct FileEntry {
    path: String,
    data_version: String,
    sha256: String,
}

fn safe_path(path: &str) -> bool {
    if path.is_empty() || path.contains(['\\', ':', '?', '#', '%']) || !path.ends_with(".json") {
        return false;
    }
    let parts: Vec<_> = path.split('/').collect();
    if parts.iter().any(|part| part.is_empty() || *part == "." || *part == "..") {
        return false;
    }
    matches!(parts[0], "audio" | "backgrounds")
        || (parts.len() == 1
            && matches!(path, "characters.json" | "index.json" | "modmanager.json" | "modpreview.json"))
}

fn sha256(bytes: &[u8]) -> String {
    format!("{:x}", Sha256::digest(bytes))
}

fn parse_manifest(bytes: &[u8]) -> Result<Manifest, GameDataUpdateError> {
    let manifest: Manifest = serde_json::from_slice(bytes)
        .map_err(|error| GameDataUpdateError::InvalidData(format!("catalog manifest: {error}")))?;
    if manifest.schema_version != 1 || manifest.files.is_empty() || manifest.catalog_version.is_empty() {
        return Err(GameDataUpdateError::InvalidData("unsupported or empty catalog manifest".into()));
    }
    let mut paths = std::collections::HashSet::new();
    for file in &manifest.files {
        if !safe_path(&file.path)
            || file.data_version.is_empty()
            || file.sha256.len() != 64
            || !file.sha256.bytes().all(|byte| byte.is_ascii_hexdigit())
            || !paths.insert(file.path.as_str())
        {
            return Err(GameDataUpdateError::InvalidData(format!("invalid catalog entry: {}", file.path)));
        }
    }
    Ok(manifest)
}

async fn fetch<T>(
    client: &reqwest::Client,
    path: &str,
    validate: impl Fn(&[u8]) -> Result<T, String>,
) -> Result<(Vec<u8>, T), String> {
    let mut last_error = String::new();
    for base in BASES {
        let response = match client.get(format!("{base}{path}")).send().await {
            Ok(response) if response.status().is_success() => response,
            Ok(response) => { last_error = format!("HTTP {}", response.status()); continue; },
            Err(error) => { last_error = error.to_string(); continue; },
        };
        if response.content_length().is_some_and(|length| length > MAX_BYTES as u64) {
            last_error = "file exceeds 64 MB".into();
            continue;
        }
        match response.bytes().await {
            Ok(bytes) if bytes.len() <= MAX_BYTES => match validate(&bytes) {
                Ok(data) => return Ok((bytes.to_vec(), data)),
                Err(error) => last_error = error,
            },
            Ok(_) => last_error = "file exceeds 64 MB".into(),
            Err(error) => last_error = error.to_string(),
        }
    }
    Err(last_error)
}

async fn save_atomic(path: &Path, bytes: &[u8]) -> Result<(), GameDataUpdateError> {
    let parent = path.parent().ok_or_else(|| GameDataUpdateError::SaveFailed("invalid catalog path".into()))?;
    tokio::fs::create_dir_all(parent).await.map_err(|error| GameDataUpdateError::SaveFailed(error.to_string()))?;
    let file_name = path.file_name().and_then(|name| name.to_str()).unwrap_or("catalog.json");
    let temporary = parent.join(format!(".{file_name}.{}.download", std::process::id()));
    tokio::fs::write(&temporary, bytes).await.map_err(|error| GameDataUpdateError::SaveFailed(error.to_string()))?;
    if let Err(error) = tokio::fs::rename(&temporary, path).await {
        let _ = std::fs::remove_file(&temporary);
        return Err(GameDataUpdateError::SaveFailed(error.to_string()));
    }
    Ok(())
}

pub async fn update_catalogs(
    asset_root: &Path,
    game_version: Option<&str>,
    on_event: &Channel<GameDataEvent>,
) -> Result<bool, GameDataUpdateError> {
    let Some(game_version) = game_version else {
        let _ = on_event.send(GameDataEvent::Updated { resource: "catalogs".into(), label: "game_version_unavailable".into() });
        return Ok(false);
    };
    tokio::fs::create_dir_all(asset_root).await.map_err(|error| GameDataUpdateError::SaveFailed(error.to_string()))?;
    let asset_root = tokio::fs::canonicalize(asset_root).await
        .map_err(|error| GameDataUpdateError::SaveFailed(error.to_string()))?;
    let client = reqwest::Client::builder().timeout(std::time::Duration::from_secs(12)).build()
        .map_err(|error| GameDataUpdateError::CheckFailed(error.to_string()))?;
    let _ = on_event.send(GameDataEvent::Updating {
        resource: "catalogs".into(), percentage: 0, label: MANIFEST.into(),
        download: Some(GameDataDownloadProgress { percentage: None, current: None, total: None }),
    });
    let (manifest_bytes, manifest) = fetch(&client, MANIFEST, |bytes| {
        parse_manifest(bytes).map_err(|error| error.to_string())
    }).await.map_err(GameDataUpdateError::CheckFailed)?;
    if manifest.game_version != game_version {
        log::info!("No asset catalog for game version {game_version}; published catalog targets {}", manifest.game_version);
        let _ = on_event.send(GameDataEvent::Updated { resource: "catalogs".into(), label: "no_matching_game_version".into() });
        return Ok(false);
    }

    let total = manifest.files.len();
    let mut updates = Vec::new();
    for (index, file) in manifest.files.iter().enumerate() {
        let destination = asset_root.join(&file.path);
        let parent = destination.parent().expect("validated catalog path");
        tokio::fs::create_dir_all(parent).await.map_err(|error| GameDataUpdateError::SaveFailed(error.to_string()))?;
        let parent = tokio::fs::canonicalize(parent).await
            .map_err(|error| GameDataUpdateError::SaveFailed(error.to_string()))?;
        if !parent.starts_with(&asset_root) {
            return Err(GameDataUpdateError::SaveFailed(format!("catalog path outside asset library: {}", file.path)));
        }
        let existing = tokio::fs::read(&destination).await.ok();
        if existing.as_deref().is_some_and(|bytes| sha256(bytes).eq_ignore_ascii_case(&file.sha256)) {
            continue;
        }
        let _ = on_event.send(GameDataEvent::Updating {
            resource: "catalogs".into(),
            percentage: ((index as f32 / total as f32) * 100.0) as u8,
            label: file.path.clone(),
            download: Some(GameDataDownloadProgress { percentage: None, current: Some(index as u32), total: Some(total as u32) }),
        });
        let (bytes, ()) = fetch(&client, &file.path, |bytes| {
            if !sha256(bytes).eq_ignore_ascii_case(&file.sha256) {
                return Err("SHA-256 mismatch".into());
            }
            let value: Value = serde_json::from_slice(bytes).map_err(|error| error.to_string())?;
            if !value.is_object() { return Err("expected a JSON object".into()); }
            Ok(())
        }).await.map_err(|error| GameDataUpdateError::DownloadFailed(format!("{}: {error}", file.path)))?;
        updates.push((destination, bytes, existing));
    }
    // Finish downloading and checking all files before touching the installed catalog.
    let manifest_path = asset_root.join(MANIFEST);
    let changed = !updates.is_empty() || tokio::fs::read(&manifest_path).await.ok().as_deref() != Some(&manifest_bytes[..]);
    let mut applied = Vec::new();
    let result = async {
        for (path, bytes, previous) in &updates {
            save_atomic(path, bytes).await?;
            applied.push((path, previous));
        }
        if changed { save_atomic(&manifest_path, &manifest_bytes).await?; }
        Ok::<(), GameDataUpdateError>(())
    }.await;
    if let Err(error) = result {
        for (path, previous) in applied.into_iter().rev() {
            let restored = match previous {
                Some(bytes) => save_atomic(path, bytes).await,
                None => tokio::fs::remove_file(path).await
                    .map_err(|error| GameDataUpdateError::SaveFailed(error.to_string())),
            };
            if let Err(restore_error) = restored {
                log::warn!("Could not restore catalog {}: {restore_error}", path.display());
            }
        }
        return Err(error);
    }
    let _ = on_event.send(GameDataEvent::Updated {
        resource: "catalogs".into(),
        label: if changed { format!("catalogs_updated:{}", manifest.catalog_version) } else { "already_uptodate".into() },
    });
    Ok(changed)
}

pub(crate) fn read_installed(asset_root: &Path, file: &str) -> Option<Vec<u8>> {
    let manifest = parse_manifest(&std::fs::read(asset_root.join(MANIFEST)).ok()?).ok()?;
    let entry = manifest.files.iter().find(|entry| entry.path == file)?;
    let bytes = std::fs::read(asset_root.join(file)).ok()?;
    sha256(&bytes).eq_ignore_ascii_case(&entry.sha256).then_some(bytes)
}
