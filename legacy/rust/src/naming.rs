use crate::config::AppPaths;
use crate::models::{AppSettings, Station};
use anyhow::{Context, Result};
use chrono::{DateTime, Local};
use std::fs;
use std::path::{Path, PathBuf};

pub fn resolve_recordings_dir(paths: &AppPaths, settings: &AppSettings) -> Result<PathBuf> {
    let folder = if settings.recordings_folder.is_absolute() {
        settings.recordings_folder.clone()
    } else {
        paths.root_dir.join(&settings.recordings_folder)
    };

    fs::create_dir_all(&folder)
        .with_context(|| format!("failed to create recordings folder {}", folder.display()))?;

    Ok(folder)
}

pub fn build_output_path(
    paths: &AppPaths,
    settings: &AppSettings,
    station: &Station,
    extension: &str,
    now: DateTime<Local>,
) -> Result<PathBuf> {
    let recordings_dir = resolve_recordings_dir(paths, settings)?;
    let mut file_name = apply_template(&settings.file_name_template, station, now);
    if file_name.trim().is_empty() {
        file_name = sanitize_file_name(&station.name);
    }

    file_name = sanitize_file_name(&file_name);
    let file_name = if extension.is_empty() {
        file_name
    } else {
        format!("{file_name}.{extension}")
    };

    Ok(ensure_unique(recordings_dir.join(file_name)))
}

pub fn sanitize_file_name(input: &str) -> String {
    let mut value = String::with_capacity(input.len());
    for ch in input.chars() {
        let safe =
            matches!(ch, '<' | '>' | ':' | '"' | '/' | '\\' | '|' | '?' | '*') || ch.is_control();
        if safe {
            value.push('_');
        } else {
            value.push(ch);
        }
    }

    value
        .trim_matches(['.', ' '])
        .chars()
        .take(160)
        .collect::<String>()
        .trim()
        .to_string()
}

fn apply_template(template: &str, station: &Station, now: DateTime<Local>) -> String {
    let station_name = sanitize_file_name(&station.name);
    let values = [
        ("%t", station_name.as_str()),
        ("%r", &now.format("%Y").to_string()),
        ("%n", &now.format("%m").to_string()),
        ("%M", &now.format("%m").to_string()),
        ("%d", &now.format("%d").to_string()),
        ("%h", &now.format("%H").to_string()),
        ("%m", &now.format("%M").to_string()),
        ("%s", &now.format("%S").to_string()),
    ];

    let mut result = template.to_string();
    for (token, replacement) in values {
        result = result.replace(token, replacement);
    }
    result
}

fn ensure_unique(path: PathBuf) -> PathBuf {
    if !path.exists() {
        return path;
    }

    let parent = path.parent().unwrap_or_else(|| Path::new("."));
    let stem = path
        .file_stem()
        .and_then(|value| value.to_str())
        .unwrap_or("recording");
    let extension = path
        .extension()
        .and_then(|value| value.to_str())
        .unwrap_or("");

    for index in 1..10000 {
        let candidate = if extension.is_empty() {
            parent.join(format!("{stem}_{index}"))
        } else {
            parent.join(format!("{stem}_{index}.{extension}"))
        };

        if !candidate.exists() {
            return candidate;
        }
    }

    path
}
