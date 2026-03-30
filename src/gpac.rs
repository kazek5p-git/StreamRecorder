use crate::config::AppPaths;
use crate::models::{AppSettings, GpacReleaseChannel};
use anyhow::{Context, Result, anyhow};
use reqwest::blocking::Client;
use std::env;
use std::fs;
use std::io::copy;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::Duration;
use url::Url;

pub const GPAC_DOWNLOADS_PAGE: &str = "https://gpac.io/downloads/gpac-nightly-builds/";
const GPAC_NIGHTLY_WINDOWS_X64_URL: &str =
    "https://download.tsi.telecom-paristech.fr/gpac/new_builds/windows/gpac_latest_head_win64.exe";

#[derive(Debug, Clone)]
pub struct GpacReleaseInfo {
    pub channel: GpacReleaseChannel,
    pub version: String,
    pub source_id: String,
    pub download_url: String,
    pub final_url: String,
    pub installer_name: String,
    pub page_url: String,
}

#[derive(Debug, Clone)]
pub struct InstalledGpacInfo {
    pub path: PathBuf,
    pub version: String,
    pub source_id: String,
}

pub fn detect_mp4box(paths: &AppPaths, settings: &AppSettings) -> Option<InstalledGpacInfo> {
    let path = resolve_mp4box_path(paths, settings)?;
    let version_output = read_mp4box_version(&path).unwrap_or_default();
    let source_id = extract_source_id(&version_output)
        .or_else(|| {
            if settings.gpac_installed_source.trim().is_empty() {
                None
            } else {
                Some(settings.gpac_installed_source.clone())
            }
        })
        .unwrap_or_default();

    Some(InstalledGpacInfo {
        path,
        version: version_output,
        source_id,
    })
}

pub fn resolve_mp4box_path(paths: &AppPaths, settings: &AppSettings) -> Option<PathBuf> {
    let mut candidates = Vec::new();

    if let Some(path) = settings.remux_tool_path.clone() {
        candidates.push(path);
    }

    candidates.push(paths.root_dir.join("Tools").join("GPAC").join("MP4Box.exe"));
    candidates.push(paths.root_dir.join("Tools").join("MP4Box.exe"));
    candidates.push(paths.root_dir.join("MP4Box.exe"));

    candidates.extend(program_files_candidates());

    candidates.into_iter().find(|path| path.exists())
}

pub fn fetch_gpac_release(channel: GpacReleaseChannel) -> Result<GpacReleaseInfo> {
    let client = build_client()?;
    let downloads_page = client
        .get(GPAC_DOWNLOADS_PAGE)
        .send()
        .context("failed to query the GPAC downloads page")?
        .error_for_status()
        .context("the GPAC downloads page returned an error")?
        .text()
        .context("failed to read the GPAC downloads page")?;

    let download_url = match channel {
        GpacReleaseChannel::Stable => find_stable_windows_installer_url(&downloads_page)?,
        GpacReleaseChannel::Nightly => GPAC_NIGHTLY_WINDOWS_X64_URL.to_string(),
    };
    let final_url = resolve_final_download_url(&client, &download_url)?;
    let installer_name = installer_name_from_url(&final_url)
        .or_else(|| installer_name_from_url(&download_url))
        .unwrap_or_else(|| "gpac_windows_installer.exe".to_string());
    let source_id = installer_stem(Path::new(&installer_name)).unwrap_or_else(|| installer_name);
    let version = detect_release_version(channel.clone(), &final_url, &downloads_page)
        .unwrap_or_else(|| fallback_release_version(&source_id, &channel));

    Ok(GpacReleaseInfo {
        channel,
        version,
        source_id,
        download_url: download_url.clone(),
        final_url: final_url.clone(),
        installer_name: installer_name_from_url(&final_url)
            .or_else(|| installer_name_from_url(&download_url))
            .unwrap_or_else(|| "gpac_windows_installer.exe".to_string()),
        page_url: GPAC_DOWNLOADS_PAGE.to_string(),
    })
}

pub fn is_gpac_release_current(
    installed: &InstalledGpacInfo,
    available: &GpacReleaseInfo,
    settings: &AppSettings,
) -> bool {
    if !installed.source_id.is_empty()
        && installed
            .source_id
            .eq_ignore_ascii_case(&available.source_id)
    {
        return true;
    }

    if !settings.gpac_installed_source.trim().is_empty()
        && settings
            .gpac_installed_source
            .eq_ignore_ascii_case(&available.source_id)
    {
        return true;
    }

    let installed_version = normalize_version_token(&installed.version);
    let available_version = normalize_version_token(&available.version);

    match available.channel {
        GpacReleaseChannel::Stable => {
            !installed_version.is_empty() && installed_version == available_version
        }
        GpacReleaseChannel::Nightly => {
            (!available_version.is_empty() && installed_version == available_version)
                || (!installed.version.is_empty()
                    && installed
                        .version
                        .to_ascii_lowercase()
                        .contains(&available.source_id.to_ascii_lowercase()))
        }
    }
}

pub fn download_gpac_installer(paths: &AppPaths, release: &GpacReleaseInfo) -> Result<PathBuf> {
    let downloads_dir = paths.config_dir.join("components");
    fs::create_dir_all(&downloads_dir)
        .with_context(|| format!("failed to create {}", downloads_dir.display()))?;

    let destination = downloads_dir.join(&release.installer_name);
    if destination.exists() {
        let _ = fs::remove_file(&destination);
    }

    let mut response = build_client()?
        .get(&release.download_url)
        .send()
        .with_context(|| format!("failed to download {}", release.installer_name))?
        .error_for_status()
        .with_context(|| format!("download failed for {}", release.installer_name))?;

    let mut file = fs::File::create(&destination)
        .with_context(|| format!("failed to create {}", destination.display()))?;
    copy(&mut response, &mut file)
        .with_context(|| format!("failed to write {}", destination.display()))?;

    Ok(destination)
}

pub fn launch_gpac_installer(installer_path: &Path) -> Result<()> {
    let child = Command::new(installer_path)
        .spawn()
        .with_context(|| format!("failed to launch {}", installer_path.display()))?;

    if child.id() == 0 {
        return Err(anyhow!("failed to start the GPAC installer"));
    }

    Ok(())
}

fn program_files_candidates() -> Vec<PathBuf> {
    let mut candidates = Vec::new();
    for key in ["ProgramFiles", "ProgramFiles(x86)"] {
        let Ok(base) = env::var(key) else {
            continue;
        };
        let base = PathBuf::from(base);
        candidates.push(base.join("GPAC").join("MP4Box.exe"));

        if let Ok(entries) = fs::read_dir(&base) {
            for entry in entries.flatten() {
                let path = entry.path();
                if !path.is_dir() {
                    continue;
                }
                let Some(name) = path.file_name().and_then(|name| name.to_str()) else {
                    continue;
                };
                if !name.to_ascii_lowercase().starts_with("gpac") {
                    continue;
                }
                candidates.push(path.join("MP4Box.exe"));
                candidates.push(path.join("gpac").join("MP4Box.exe"));
            }
        }
    }
    candidates
}

fn read_mp4box_version(path: &Path) -> Result<String> {
    let output = Command::new(path)
        .arg("-version")
        .output()
        .with_context(|| format!("failed to query {}", path.display()))?;

    let mut text = String::new();
    text.push_str(&String::from_utf8_lossy(&output.stdout));
    if !output.stderr.is_empty() {
        if !text.is_empty() && !text.ends_with('\n') {
            text.push('\n');
        }
        text.push_str(&String::from_utf8_lossy(&output.stderr));
    }

    let first_line = text
        .lines()
        .map(str::trim)
        .find(|line| !line.is_empty())
        .unwrap_or_default()
        .to_string();

    Ok(first_line)
}

fn find_stable_windows_installer_url(html: &str) -> Result<String> {
    iter_href_values(html)
        .find(|href| {
            let lower = href.to_ascii_lowercase();
            lower.contains("windows_installer.exe")
                && !lower.contains("latest_head")
                && (lower.contains("x64") || lower.contains("64"))
        })
        .map(|href| absolutize_url(&href))
        .transpose()?
        .ok_or_else(|| anyhow!("failed to locate the stable GPAC Windows installer"))
}

fn iter_href_values(html: &str) -> impl Iterator<Item = String> + '_ {
    html.match_indices("href=\"").filter_map(|(index, _)| {
        let tail = &html[index + 6..];
        let end = tail.find('"')?;
        Some(tail[..end].to_string())
    })
}

fn absolutize_url(value: &str) -> Result<String> {
    let url = Url::parse(value)
        .or_else(|_| Url::parse(GPAC_DOWNLOADS_PAGE)?.join(value))
        .context("failed to resolve GPAC download url")?;
    Ok(url.to_string())
}

fn resolve_final_download_url(client: &Client, url: &str) -> Result<String> {
    let response = client
        .head(url)
        .send()
        .or_else(|_| client.get(url).send())
        .with_context(|| format!("failed to resolve {}", url))?
        .error_for_status()
        .with_context(|| format!("GPAC download endpoint returned an error for {}", url))?;
    Ok(response.url().to_string())
}

fn installer_name_from_url(url: &str) -> Option<String> {
    Url::parse(url).ok().and_then(|value| {
        value
            .path_segments()
            .and_then(|segments| segments.last().map(ToString::to_string))
    })
}

fn installer_stem(path: &Path) -> Option<String> {
    path.file_stem()
        .and_then(|value| value.to_str())
        .map(|value| value.to_string())
}

fn detect_release_version(
    channel: GpacReleaseChannel,
    final_url: &str,
    html: &str,
) -> Option<String> {
    match channel {
        GpacReleaseChannel::Stable => extract_stable_version(final_url).or_else(|| {
            html.find("Current Stable Release")
                .and_then(|index| slice_version_hint(&html[index..]))
        }),
        GpacReleaseChannel::Nightly => {
            extract_stable_version(final_url).or_else(|| Some("nightly".to_string()))
        }
    }
}

fn fallback_release_version(source_id: &str, channel: &GpacReleaseChannel) -> String {
    let version = extract_stable_version(source_id);
    match (channel, version) {
        (_, Some(version)) => version,
        (GpacReleaseChannel::Nightly, None) => "nightly".to_string(),
        (GpacReleaseChannel::Stable, None) => "stable".to_string(),
    }
}

fn extract_stable_version(value: &str) -> Option<String> {
    let lower = value.to_ascii_lowercase();
    let marker = lower.find("gpac-")?;
    let tail = &value[marker + 5..];
    let mut version = String::new();
    for ch in tail.chars() {
        if ch.is_ascii_digit() || ch == '.' {
            version.push(ch);
        } else if !version.is_empty() {
            break;
        }
    }
    if version.is_empty() {
        None
    } else {
        Some(version)
    }
}

fn slice_version_hint(value: &str) -> Option<String> {
    let mut version = String::new();
    let mut started = false;
    for ch in value.chars() {
        if ch.is_ascii_digit() || (started && ch == '.') {
            version.push(ch);
            started = true;
        } else if started {
            break;
        }
    }
    if version.is_empty() {
        None
    } else {
        Some(version)
    }
}

fn extract_source_id(value: &str) -> Option<String> {
    if value.trim().is_empty() {
        return None;
    }

    if let Some(hash_index) = value.find("-g") {
        let tail = &value[hash_index + 1..];
        let token = tail
            .chars()
            .take_while(|ch| ch.is_ascii_alphanumeric() || *ch == '-' || *ch == '_')
            .collect::<String>();
        if !token.is_empty() {
            return Some(token);
        }
    }

    installer_name_from_url(value).and_then(|name| installer_stem(Path::new(&name)))
}

fn normalize_version_token(value: &str) -> String {
    extract_stable_version(value).unwrap_or_else(|| {
        let marker = value.to_ascii_lowercase().find("version");
        let slice = marker
            .and_then(|index| value.get(index + "version".len()..))
            .unwrap_or(value);
        slice_version_hint(slice).unwrap_or_default()
    })
}

fn build_client() -> Result<Client> {
    Client::builder()
        .user_agent("StreamRecorder/0.1.2")
        .connect_timeout(Duration::from_secs(10))
        .timeout(Duration::from_secs(60))
        .redirect(reqwest::redirect::Policy::limited(10))
        .build()
        .context("failed to build GPAC update client")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn extracts_version_from_gpac_filename() {
        let url = "https://download.example/gpac-26.02-rev0-g118e60a9-master-x64.exe";
        assert_eq!(extract_stable_version(url).as_deref(), Some("26.02"));
    }

    #[test]
    fn extracts_source_id_from_version_line() {
        let text = "MP4Box - GPAC version 2.5-DEV-rev123-g50c2ab06f-master";
        assert_eq!(
            extract_source_id(text).as_deref(),
            Some("g50c2ab06f-master")
        );
    }
}
