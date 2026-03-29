use crate::config::{AppPaths, load_or_create, save};
use crate::localization;
use crate::logging::LogBus;
use crate::models::{AppConfig, AppSettings, Station};
use crate::recording::{RecorderManager, RecordingSnapshot};
use crate::scheduler::SchedulerService;
use crate::updater::{UpdateInfo, check_for_updates};
use anyhow::Result;
use std::sync::{Arc, Mutex};
use tokio::runtime::Runtime;
use uuid::Uuid;

pub struct AppContext {
    pub paths: AppPaths,
    pub logs: LogBus,
    pub recorder: RecorderManager,
    pub scheduler: SchedulerService,
    runtime: Arc<Runtime>,
    config: Arc<Mutex<AppConfig>>,
}

impl AppContext {
    pub fn load() -> Result<Arc<Self>> {
        let paths = AppPaths::discover()?;
        let loaded_config = load_or_create(&paths)?;
        let _ = localization::initialize(
            &paths.root_dir.join("locale"),
            loaded_config.settings.language.clone(),
        );
        let config = Arc::new(Mutex::new(loaded_config));
        let logs = LogBus::new(paths.log_file.clone());
        let runtime = Arc::new(Runtime::new()?);
        let recorder = RecorderManager::new(Arc::clone(&runtime));
        let scheduler = SchedulerService::spawn(
            Arc::clone(&runtime),
            Arc::clone(&config),
            recorder.clone(),
            paths.clone(),
            logs.clone(),
        );

        Ok(Arc::new(Self {
            paths,
            logs,
            recorder,
            scheduler,
            runtime,
            config,
        }))
    }

    pub fn config_snapshot(&self) -> AppConfig {
        self.config.lock().expect("config lock poisoned").clone()
    }

    pub fn settings_snapshot(&self) -> AppSettings {
        self.config_snapshot().settings
    }

    pub fn stations_snapshot(&self) -> Vec<Station> {
        self.config_snapshot().stations
    }

    pub fn station(&self, station_id: Uuid) -> Option<Station> {
        self.stations_snapshot()
            .into_iter()
            .find(|station| station.id == station_id)
    }

    pub fn upsert_station(&self, station: Station) -> Result<()> {
        let mut config = self.config.lock().expect("config lock poisoned");
        if let Some(existing) = config
            .stations
            .iter_mut()
            .find(|existing| existing.id == station.id)
        {
            *existing = station;
        } else {
            config.stations.push(station);
        }

        save(&self.paths, &config)?;
        Ok(())
    }

    pub fn remove_station(&self, station_id: Uuid) -> Result<()> {
        self.recorder.stop(station_id);

        let mut config = self.config.lock().expect("config lock poisoned");
        config.stations.retain(|station| station.id != station_id);
        save(&self.paths, &config)?;
        Ok(())
    }

    pub fn save_settings(&self, settings: AppSettings) -> Result<()> {
        let mut config = self.config.lock().expect("config lock poisoned");
        config.settings = settings;
        save(&self.paths, &config)?;
        Ok(())
    }

    pub fn start_station(&self, station_id: Uuid) -> Result<()> {
        let station = self
            .station(station_id)
            .ok_or_else(|| anyhow::anyhow!("station not found"))?;
        let settings = self.settings_snapshot();
        self.recorder
            .start(station, settings, self.paths.clone(), self.logs.clone())
    }

    pub fn stop_station(&self, station_id: Uuid) {
        self.recorder.stop(station_id);
    }

    pub fn recording_snapshot(&self, station_id: Uuid) -> Option<RecordingSnapshot> {
        self.recorder.snapshot(station_id)
    }

    pub fn recording_snapshots(&self) -> Vec<RecordingSnapshot> {
        self.recorder.snapshots().into_values().collect()
    }

    pub fn check_for_updates(&self) -> Result<Option<UpdateInfo>> {
        let repo = self.settings_snapshot().update_repo;
        check_for_updates(env!("CARGO_PKG_VERSION"), &repo)
    }

    pub fn shutdown(&self) {
        self.scheduler.stop();
        self.recorder.stop_all();
        let _ = &self.runtime;
    }
}
