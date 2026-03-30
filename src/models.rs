use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use uuid::Uuid;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub enum Language {
    Polish,
    English,
}

impl Default for Language {
    fn default() -> Self {
        Self::Polish
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub enum GpacReleaseChannel {
    Stable,
    Nightly,
}

impl Default for GpacReleaseChannel {
    fn default() -> Self {
        Self::Stable
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Credentials {
    pub username: String,
    pub password: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ScheduleRule {
    pub enabled: bool,
    pub weekdays: [bool; 7],
    pub start_hour: u8,
    pub start_minute: u8,
    pub end_hour: u8,
    pub end_minute: u8,
}

impl Default for ScheduleRule {
    fn default() -> Self {
        Self {
            enabled: false,
            weekdays: [true, true, true, true, true, true, true],
            start_hour: 0,
            start_minute: 0,
            end_hour: 23,
            end_minute: 59,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Station {
    pub id: Uuid,
    pub name: String,
    pub url: String,
    pub credentials: Option<Credentials>,
    pub schedules: Vec<ScheduleRule>,
}

impl Station {
    pub fn new(name: impl Into<String>, url: impl Into<String>) -> Self {
        Self {
            id: Uuid::new_v4(),
            name: name.into(),
            url: url.into(),
            credentials: None,
            schedules: Vec::new(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(default)]
pub struct AppSettings {
    pub launch_on_startup: bool,
    pub always_on_top: bool,
    pub minimize_to_tray: bool,
    pub confirm_on_exit: bool,
    pub restart_on_crash: bool,
    pub prevent_sleep: bool,
    pub start_minimized: bool,
    pub remux_raw_aac_to_m4a: bool,
    pub recordings_folder: PathBuf,
    pub file_name_template: String,
    pub language: Language,
    pub update_repo: String,
    pub remux_tool_path: Option<PathBuf>,
    pub gpac_release_channel: GpacReleaseChannel,
    pub gpac_installed_version: String,
    pub gpac_installed_source: String,
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            launch_on_startup: false,
            always_on_top: false,
            minimize_to_tray: true,
            confirm_on_exit: true,
            restart_on_crash: false,
            prevent_sleep: false,
            start_minimized: false,
            remux_raw_aac_to_m4a: true,
            recordings_folder: PathBuf::from("My recordings"),
            file_name_template: "%t_%r-%M-%d_%h-%m-%s".to_string(),
            language: Language::Polish,
            update_repo: String::new(),
            remux_tool_path: None,
            gpac_release_channel: GpacReleaseChannel::Stable,
            gpac_installed_version: String::new(),
            gpac_installed_source: String::new(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(default)]
pub struct AppConfig {
    pub settings: AppSettings,
    pub stations: Vec<Station>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum StreamProtocol {
    Http,
    Hls,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum StreamFormat {
    Mp3,
    AacRaw,
    Ogg,
    Flac,
    Wma,
    Wav,
    MpegTs,
    Unknown,
}

impl StreamFormat {
    pub fn extension(self) -> &'static str {
        match self {
            StreamFormat::Mp3 => "mp3",
            StreamFormat::AacRaw => "aac",
            StreamFormat::Ogg => "ogg",
            StreamFormat::Flac => "flac",
            StreamFormat::Wma => "wma",
            StreamFormat::Wav => "wav",
            StreamFormat::MpegTs => "ts",
            StreamFormat::Unknown => "bin",
        }
    }

    pub fn display_name(self) -> &'static str {
        match self {
            StreamFormat::Mp3 => "MP3",
            StreamFormat::AacRaw => "AAC",
            StreamFormat::Ogg => "OGG",
            StreamFormat::Flac => "FLAC",
            StreamFormat::Wma => "WMA",
            StreamFormat::Wav => "WAV",
            StreamFormat::MpegTs => "MPEG-TS",
            StreamFormat::Unknown => "Unknown",
        }
    }
}
