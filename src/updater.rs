use crate::config::AppPaths;
use anyhow::{Context, Result, anyhow};
use reqwest::blocking::Client;
use serde::Deserialize;
use std::fs;
use std::io::copy;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::Duration;

#[derive(Debug, Clone)]
pub struct UpdateInfo {
    pub version: String,
    pub html_url: String,
    pub asset: Option<UpdateAsset>,
}

#[derive(Debug, Clone)]
pub struct UpdateAsset {
    pub name: String,
    pub download_url: String,
    pub size: u64,
    pub kind: UpdateAssetKind,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum UpdateAssetKind {
    Zip,
    Exe,
    Msi,
}

#[derive(Debug, Deserialize)]
struct GitHubRelease {
    tag_name: String,
    html_url: String,
    draft: bool,
    prerelease: bool,
    assets: Vec<GitHubAsset>,
}

#[derive(Debug, Deserialize)]
struct GitHubAsset {
    name: String,
    browser_download_url: String,
    size: u64,
}

pub fn check_for_updates(current_version: &str, repo: &str) -> Result<Option<UpdateInfo>> {
    let repo = repo.trim().trim_matches('/');
    if repo.is_empty() {
        return Ok(None);
    }

    let release = build_client()?
        .get(format!(
            "https://api.github.com/repos/{repo}/releases/latest"
        ))
        .send()
        .context("failed to query latest release")?
        .error_for_status()
        .context("GitHub update endpoint returned an error")?
        .json::<GitHubRelease>()
        .context("failed to parse update response")?;

    if release.draft || release.prerelease {
        return Ok(None);
    }

    let latest = release.tag_name.trim_start_matches('v');
    let current = current_version.trim_start_matches('v');
    if latest == current {
        return Ok(None);
    }

    Ok(Some(UpdateInfo {
        version: release.tag_name,
        html_url: release.html_url,
        asset: choose_preferred_asset(&release.assets),
    }))
}

pub fn download_update(paths: &AppPaths, update: &UpdateInfo) -> Result<PathBuf> {
    let asset = update
        .asset
        .as_ref()
        .context("release does not contain a supported downloadable asset")?;
    let updates_dir = paths.config_dir.join("updates");
    fs::create_dir_all(&updates_dir)
        .with_context(|| format!("failed to create {}", updates_dir.display()))?;

    let destination = updates_dir.join(&asset.name);
    if destination.exists() {
        let _ = fs::remove_file(&destination);
    }

    let mut response = build_client()?
        .get(&asset.download_url)
        .send()
        .with_context(|| format!("failed to download {}", asset.name))?
        .error_for_status()
        .with_context(|| format!("download failed for {}", asset.name))?;

    let mut file = fs::File::create(&destination)
        .with_context(|| format!("failed to create {}", destination.display()))?;
    copy(&mut response, &mut file)
        .with_context(|| format!("failed to write {}", destination.display()))?;

    Ok(destination)
}

pub fn install_downloaded_update(
    paths: &AppPaths,
    downloaded_asset: &Path,
    asset: &UpdateAsset,
    restart_exe: &Path,
    restart_args: &[String],
) -> Result<()> {
    let script_path =
        create_update_script(paths, downloaded_asset, asset, restart_exe, restart_args)?;
    let status = Command::new("powershell.exe")
        .args([
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-WindowStyle",
            "Hidden",
            "-File",
        ])
        .arg(&script_path)
        .spawn()
        .context("failed to launch update installer script")?;

    if status.id() == 0 {
        return Err(anyhow!("failed to start update installer process"));
    }

    Ok(())
}

fn create_update_script(
    paths: &AppPaths,
    downloaded_asset: &Path,
    asset: &UpdateAsset,
    restart_exe: &Path,
    restart_args: &[String],
) -> Result<PathBuf> {
    let script_path = paths.config_dir.join("updates").join("apply_update.ps1");
    let script = match asset.kind {
        UpdateAssetKind::Zip => {
            build_zip_script(paths, downloaded_asset, restart_exe, restart_args)
        }
        UpdateAssetKind::Exe | UpdateAssetKind::Msi => {
            build_installer_script(paths, downloaded_asset, restart_exe, restart_args)
        }
    };

    fs::write(&script_path, script)
        .with_context(|| format!("failed to write {}", script_path.display()))?;
    Ok(script_path)
}

fn build_zip_script(
    paths: &AppPaths,
    downloaded_asset: &Path,
    restart_exe: &Path,
    restart_args: &[String],
) -> String {
    let restart_args = powershell_array(restart_args);
    format!(
        concat!(
            "$ErrorActionPreference = 'Stop'\n",
            "$archivePath = '{archive_path}'\n",
            "$appRoot = '{app_root}'\n",
            "$restartExe = '{restart_exe}'\n",
            "$restartArgs = {restart_args}\n",
            "$scriptPath = $MyInvocation.MyCommand.Path\n",
            "Start-Sleep -Seconds 2\n",
            "$extractRoot = Join-Path ([System.IO.Path]::GetDirectoryName($archivePath)) ('extract_' + [Guid]::NewGuid().ToString('N'))\n",
            "Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force\n",
            "$sourceRoot = $extractRoot\n",
            "$children = Get-ChildItem -LiteralPath $extractRoot -Force\n",
            "if ($children.Count -eq 1 -and $children[0].PSIsContainer) {{ $sourceRoot = $children[0].FullName }}\n",
            "Get-ChildItem -LiteralPath $sourceRoot -Force | ForEach-Object {{\n",
            "  if ($_.Name -eq 'Config') {{\n",
            "    $targetConfig = Join-Path $appRoot 'Config'\n",
            "    New-Item -ItemType Directory -Path $targetConfig -Force | Out-Null\n",
            "    Get-ChildItem -LiteralPath $_.FullName -Force | Where-Object {{ $_.Name -notin @('app.toml', 'streamrecorder.log') }} | ForEach-Object {{\n",
            "      Copy-Item -LiteralPath $_.FullName -Destination $targetConfig -Recurse -Force\n",
            "    }}\n",
            "  }} else {{\n",
            "    Copy-Item -LiteralPath $_.FullName -Destination $appRoot -Recurse -Force\n",
            "  }}\n",
            "}}\n",
            "Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue\n",
            "Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue\n",
            "Start-Sleep -Milliseconds 500\n",
            "Start-Process -FilePath $restartExe -ArgumentList $restartArgs -WorkingDirectory $appRoot\n",
            "Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue\n"
        ),
        archive_path = ps_string(downloaded_asset),
        app_root = ps_string(&paths.root_dir),
        restart_exe = ps_string(restart_exe),
        restart_args = restart_args,
    )
}

fn build_installer_script(
    _paths: &AppPaths,
    downloaded_asset: &Path,
    _restart_exe: &Path,
    _restart_args: &[String],
) -> String {
    format!(
        concat!(
            "$ErrorActionPreference = 'Stop'\n",
            "$installerPath = '{installer_path}'\n",
            "$scriptPath = $MyInvocation.MyCommand.Path\n",
            "Start-Sleep -Seconds 2\n",
            "Start-Process -FilePath $installerPath\n",
            "Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue\n"
        ),
        installer_path = ps_string(downloaded_asset),
    )
}

fn choose_preferred_asset(assets: &[GitHubAsset]) -> Option<UpdateAsset> {
    assets
        .iter()
        .filter_map(map_asset)
        .max_by_key(|asset| asset_score(asset))
}

fn map_asset(asset: &GitHubAsset) -> Option<UpdateAsset> {
    let lower = asset.name.to_ascii_lowercase();
    let kind = if lower.ends_with(".zip") {
        UpdateAssetKind::Zip
    } else if lower.ends_with(".exe") {
        UpdateAssetKind::Exe
    } else if lower.ends_with(".msi") {
        UpdateAssetKind::Msi
    } else {
        return None;
    };

    Some(UpdateAsset {
        name: asset.name.clone(),
        download_url: asset.browser_download_url.clone(),
        size: asset.size,
        kind,
    })
}

fn asset_score(asset: &UpdateAsset) -> i32 {
    let lower = asset.name.to_ascii_lowercase();
    let mut score = match asset.kind {
        UpdateAssetKind::Zip => 300,
        UpdateAssetKind::Exe => 200,
        UpdateAssetKind::Msi => 180,
    };

    if lower.contains("portable") {
        score += 120;
    }
    if lower.contains("windows") || lower.contains("win") {
        score += 90;
    }
    if lower.contains("x86_64")
        || lower.contains("amd64")
        || lower.contains("win64")
        || lower.contains("64")
    {
        score += 70;
    }
    if lower.contains("setup") || lower.contains("installer") {
        score -= 30;
    }

    score
}

fn build_client() -> Result<Client> {
    Client::builder()
        .user_agent("StreamRecorder/0.1.1")
        .connect_timeout(Duration::from_secs(10))
        .timeout(Duration::from_secs(60))
        .build()
        .context("failed to build update client")
}

fn powershell_array(values: &[String]) -> String {
    if values.is_empty() {
        "@()".to_string()
    } else {
        format!(
            "@({})",
            values
                .iter()
                .map(|value| format!("'{}'", ps_escape(value)))
                .collect::<Vec<_>>()
                .join(", ")
        )
    }
}

fn ps_string(path: &Path) -> String {
    ps_escape(&path.to_string_lossy())
}

fn ps_escape(value: &str) -> String {
    value.replace('\'', "''")
}
