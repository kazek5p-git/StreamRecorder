use crate::models::AppConfig;
use anyhow::{Context, Result};
use std::env;
use std::fs;
use std::path::{Path, PathBuf};

const CONFIG_DIR_NAME: &str = "Config";
const RECORDINGS_DIR_NAME: &str = "My recordings";
const CONFIG_FILE_NAME: &str = "app.toml";

#[derive(Debug, Clone)]
pub struct AppPaths {
    pub root_dir: PathBuf,
    pub config_dir: PathBuf,
    pub recordings_dir: PathBuf,
    pub config_file: PathBuf,
    pub log_file: PathBuf,
}

impl AppPaths {
    pub fn discover() -> Result<Self> {
        let exe = env::current_exe().context("failed to read current executable path")?;
        let root_dir = exe
            .parent()
            .map(Path::to_path_buf)
            .context("failed to resolve executable directory")?;

        let config_dir = root_dir.join(CONFIG_DIR_NAME);
        let recordings_dir = root_dir.join(RECORDINGS_DIR_NAME);
        let config_file = config_dir.join(CONFIG_FILE_NAME);
        let log_file = config_dir.join("streamrecorder.log");

        Ok(Self {
            root_dir,
            config_dir,
            recordings_dir,
            config_file,
            log_file,
        })
    }

    pub fn ensure_directories(&self) -> Result<()> {
        fs::create_dir_all(&self.config_dir)
            .with_context(|| format!("failed to create {}", self.config_dir.display()))?;
        fs::create_dir_all(&self.recordings_dir)
            .with_context(|| format!("failed to create {}", self.recordings_dir.display()))?;
        Ok(())
    }
}

pub fn load_or_create(paths: &AppPaths) -> Result<AppConfig> {
    paths.ensure_directories()?;

    if !paths.config_file.exists() {
        let mut config = AppConfig::default();
        config.settings.recordings_folder = PathBuf::from(RECORDINGS_DIR_NAME);
        save(paths, &config)?;
        return Ok(config);
    }

    let contents = fs::read_to_string(&paths.config_file)
        .with_context(|| format!("failed to read {}", paths.config_file.display()))?;
    let mut config: AppConfig =
        toml::from_str(&contents).context("failed to parse app configuration")?;

    if config.settings.recordings_folder.as_os_str().is_empty() {
        config.settings.recordings_folder = PathBuf::from(RECORDINGS_DIR_NAME);
    }

    Ok(config)
}

pub fn save(paths: &AppPaths, config: &AppConfig) -> Result<()> {
    paths.ensure_directories()?;
    let toml = toml::to_string_pretty(config).context("failed to serialize app configuration")?;
    fs::write(&paths.config_file, toml)
        .with_context(|| format!("failed to write {}", paths.config_file.display()))?;
    Ok(())
}
