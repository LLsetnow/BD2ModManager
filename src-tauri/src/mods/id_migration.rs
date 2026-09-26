use std::{
    fs,
    io,
    os::windows::fs::MetadataExt,
    path::{Path, PathBuf},
};

use crate::mods::{BD2Mod, BD2ModType};

const TARGET_ID_LENGTH: usize = 6;
const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x400;

#[derive(Debug, thiserror::Error)]
pub enum ModIdMigrationError {
    #[error("Mod was not found: {mod_name}")]
    ModNotFound { mod_name: String },
    #[error("Target ID must contain exactly six digits: {target_id}")]
    InvalidTargetId { target_id: String },
    #[error("Only standing and cutscene mods can be migrated")]
    UnsupportedModType,
    #[error("Source ID and target ID are the same: {id}")]
    SameId { id: String },
    #[error("The mod folder must contain exactly one matching .modfile")]
    InvalidModfile,
    #[error("The destination mod folder already exists: {path}")]
    DestinationAlreadyExists { path: String },
    #[error("The mod contains a symbolic link or junction: {path}")]
    ModContainsSymlink { path: String },
    #[error("I/O error: {0}")]
    Io(#[from] io::Error),
}

impl serde::Serialize for ModIdMigrationError {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        use serde::ser::SerializeStruct;
        use serde_json::json;

        let (type_, details) = match self {
            Self::ModNotFound { mod_name } => ("ModNotFound", Some(json!({ "mod_name": mod_name }))),
            Self::InvalidTargetId { target_id } => ("InvalidTargetId", Some(json!({ "target_id": target_id }))),
            Self::UnsupportedModType => ("UnsupportedModType", None),
            Self::SameId { id } => ("SameId", Some(json!({ "id": id }))),
            Self::InvalidModfile => ("InvalidModfile", None),
            Self::DestinationAlreadyExists { path } => ("DestinationAlreadyExists", Some(json!({ "path": path }))),
            Self::ModContainsSymlink { path } => ("ModContainsSymlink", Some(json!({ "path": path }))),
            Self::Io(error) => ("Io", Some(json!({ "kind": format!("{:?}", error.kind()) }))),
        };

        let mut state = serializer.serialize_struct("ModIdMigrationError", 3)?;
        state.serialize_field("type", type_)?;
        state.serialize_field("details", &details)?;
        state.serialize_field("message", &self.to_string())?;
        state.end()
    }
}

fn resource_stem(mod_type: &BD2ModType) -> Result<(&'static str, &str), ModIdMigrationError> {
    match mod_type {
        BD2ModType::Standing { id } => Ok(("char", id)),
        BD2ModType::Cutscene { id } => Ok(("cutscene_char", id)),
        _ => Err(ModIdMigrationError::UnsupportedModType),
    }
}

fn is_supported_resource_file(path: &Path) -> bool {
    // Keep atlas page images under their existing names; the atlas points to those filenames.
    path.extension()
        .and_then(|extension| extension.to_str())
        .map(|extension| {
            ["modfile", "skel", "json", "atlas"]
                .iter()
                .any(|supported| extension.eq_ignore_ascii_case(supported))
        })
        .unwrap_or(false)
}

fn source_stem_matches(name: &str, source_stem: &str) -> bool {
    let name_lower = name.to_ascii_lowercase();
    let stem_lower = source_stem.to_ascii_lowercase();
    if !name_lower.starts_with(&stem_lower) {
        return false;
    }

    let suffix = &name[source_stem.len()..];
    suffix.is_empty() || suffix.starts_with('.') || suffix.starts_with('_')
}

fn copy_directory(source: &Path, destination: &Path) -> Result<(), ModIdMigrationError> {
    let metadata = fs::symlink_metadata(source)?;
    if metadata.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0 {
        return Err(ModIdMigrationError::ModContainsSymlink {
            path: source.to_string_lossy().into_owned(),
        });
    }

    fs::create_dir_all(destination)?;
    for entry in fs::read_dir(source)? {
        let entry = entry?;
        let source_path = entry.path();
        let destination_path = destination.join(entry.file_name());
        let metadata = fs::symlink_metadata(&source_path)?;

        if metadata.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0 {
            return Err(ModIdMigrationError::ModContainsSymlink {
                path: source_path.to_string_lossy().into_owned(),
            });
        }

        if metadata.is_dir() {
            copy_directory(&source_path, &destination_path)?;
        } else {
            fs::copy(&source_path, &destination_path)?;
        }
    }

    Ok(())
}

fn collect_files(root: &Path, files: &mut Vec<PathBuf>) -> Result<(), ModIdMigrationError> {
    for entry in fs::read_dir(root)? {
        let entry = entry?;
        let path = entry.path();
        let metadata = fs::symlink_metadata(&path)?;

        if metadata.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0 {
            return Err(ModIdMigrationError::ModContainsSymlink {
                path: path.to_string_lossy().into_owned(),
            });
        }

        if metadata.is_dir() {
            collect_files(&path, files)?;
        } else if metadata.is_file() {
            files.push(path);
        }
    }

    Ok(())
}

fn has_single_matching_modfile(root: &Path, source_stem: &str) -> Result<bool, ModIdMigrationError> {
    let mut marker_count = 0;
    let mut matching_marker_count = 0;

    for entry in fs::read_dir(root)? {
        let entry = entry?;
        let path = entry.path();
        if !path.is_file() {
            continue;
        }

        if !path
            .extension()
            .and_then(|extension| extension.to_str())
            .map(|extension| extension.eq_ignore_ascii_case("modfile"))
            .unwrap_or(false)
        {
            continue;
        }

        marker_count += 1;
        if path
            .file_name()
            .and_then(|name| name.to_str())
            .map(|name| source_stem_matches(name, source_stem))
            .unwrap_or(false)
        {
            matching_marker_count += 1;
        }
    }

    Ok(marker_count == 1 && matching_marker_count == 1)
}

pub fn duplicate_mod_with_id(
    mod_: &BD2Mod,
    target_id: &str,
) -> Result<PathBuf, ModIdMigrationError> {
    if target_id.len() != TARGET_ID_LENGTH || !target_id.bytes().all(|byte| byte.is_ascii_digit()) {
        return Err(ModIdMigrationError::InvalidTargetId {
            target_id: target_id.to_string(),
        });
    }

    let mod_type = mod_
        .mod_type
        .as_ref()
        .ok_or(ModIdMigrationError::UnsupportedModType)?;
    let (prefix, source_id) = resource_stem(mod_type)?;
    if source_id == target_id {
        return Err(ModIdMigrationError::SameId {
            id: target_id.to_string(),
        });
    }

    let source_stem = format!("{prefix}{source_id}");
    let target_stem = format!("{prefix}{target_id}");
    let source_path = fs::canonicalize(&mod_.path)?;
    if !source_path.is_dir() || !has_single_matching_modfile(&source_path, &source_stem)? {
        return Err(ModIdMigrationError::InvalidModfile);
    }

    let source_folder_name = mod_
        .path
        .file_name()
        .and_then(|name| name.to_str())
        .unwrap_or("Mod");
    let destination_parent = mod_
        .path
        .parent()
        .ok_or(ModIdMigrationError::InvalidModfile)?;
    let destination = destination_parent.join(format!("{source_folder_name} - ID {target_id}"));
    if destination.exists() {
        return Err(ModIdMigrationError::DestinationAlreadyExists {
            path: destination.to_string_lossy().into_owned(),
        });
    }

    if let Err(error) = copy_directory(&source_path, &destination) {
        let _ = fs::remove_dir_all(&destination);
        return Err(error);
    }

    let result = (|| {
        let mut files = Vec::new();
        collect_files(&destination, &mut files)?;

        let renames: Vec<(PathBuf, PathBuf)> = files
            .into_iter()
            .filter(|path| is_supported_resource_file(path))
            .filter_map(|path| {
                let file_name = path.file_name()?.to_str()?;
                if !source_stem_matches(file_name, &source_stem) {
                    return None;
                }

                let suffix = &file_name[source_stem.len()..];
                let new_name = format!("{target_stem}{suffix}");
                Some((path.clone(), path.with_file_name(new_name)))
            })
            .collect();

        if !renames.iter().any(|(old_path, _)| {
            old_path
                .file_name()
                .and_then(|name| name.to_str())
                .map(|name| name.to_ascii_lowercase().ends_with(".modfile"))
                .unwrap_or(false)
        }) {
            return Err(ModIdMigrationError::InvalidModfile);
        }

        for (_, new_path) in &renames {
            if new_path.exists() {
                return Err(ModIdMigrationError::DestinationAlreadyExists {
                    path: new_path.to_string_lossy().into_owned(),
                });
            }
        }

        for (old_path, new_path) in renames {
            fs::rename(old_path, new_path)?;
        }

        Ok(())
    })();

    if let Err(error) = result {
        let _ = fs::remove_dir_all(&destination);
        return Err(error);
    }

    Ok(destination)
}
