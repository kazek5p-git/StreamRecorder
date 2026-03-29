use crate::config::AppPaths;
use crate::localization::tr;
use crate::logging::LogBus;
use crate::models::{AppConfig, ScheduleRule, Station};
use crate::recording::RecorderManager;
use chrono::{Datelike, Local, NaiveTime, Timelike};
use std::collections::HashSet;
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tokio::runtime::Runtime;
use tokio::sync::watch;
use uuid::Uuid;

#[derive(Clone)]
pub struct SchedulerService {
    stop: watch::Sender<bool>,
}

impl SchedulerService {
    pub fn spawn(
        runtime: Arc<Runtime>,
        config: Arc<Mutex<AppConfig>>,
        recorder: RecorderManager,
        paths: AppPaths,
        logs: LogBus,
    ) -> Self {
        let (stop_tx, mut stop_rx) = watch::channel(false);
        let scheduled_runs = Arc::new(Mutex::new(HashSet::<Uuid>::new()));
        let scheduled_runs_task = Arc::clone(&scheduled_runs);

        runtime.spawn(async move {
            let mut interval = tokio::time::interval(Duration::from_secs(30));
            interval.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);

            loop {
                tokio::select! {
                    _ = stop_rx.changed() => {
                        if *stop_rx.borrow() {
                            break;
                        }
                    }
                    _ = interval.tick() => {
                        let snapshot = config.lock().expect("scheduler config lock poisoned").clone();
                        reconcile_schedule(
                            &snapshot.stations,
                            &snapshot,
                            &recorder,
                            &paths,
                            &logs,
                            &scheduled_runs_task,
                        );
                    }
                }
            }
        });

        Self { stop: stop_tx }
    }

    pub fn stop(&self) {
        let _ = self.stop.send(true);
    }
}

fn reconcile_schedule(
    stations: &[Station],
    snapshot: &AppConfig,
    recorder: &RecorderManager,
    paths: &AppPaths,
    logs: &LogBus,
    scheduled_runs: &Arc<Mutex<HashSet<Uuid>>>,
) {
    let now = Local::now();
    let should_run_ids = stations
        .iter()
        .filter(|station| should_run_station(station, now))
        .map(|station| station.id)
        .collect::<HashSet<_>>();

    for station in stations {
        let mut scheduled = scheduled_runs
            .lock()
            .expect("scheduler state lock poisoned");

        if should_run_ids.contains(&station.id) {
            if !recorder.is_recording(station.id)
                && recorder
                    .start(
                        station.clone(),
                        snapshot.settings.clone(),
                        paths.clone(),
                        logs.clone(),
                    )
                    .is_ok()
            {
                scheduled.insert(station.id);
                logs.push(format!(
                    "{}: {}",
                    tr("Schedule started recording"),
                    station.name
                ));
            }
        } else if scheduled.remove(&station.id) {
            recorder.stop(station.id);
            logs.push(format!(
                "{}: {}",
                tr("Schedule stopped recording"),
                station.name
            ));
        }
    }
}

fn should_run_station(station: &Station, now: chrono::DateTime<Local>) -> bool {
    station
        .schedules
        .iter()
        .any(|rule| rule.enabled && is_rule_active(rule, now))
}

fn is_rule_active(rule: &ScheduleRule, now: chrono::DateTime<Local>) -> bool {
    let weekday = now.weekday().num_days_from_monday() as usize;
    if !rule.weekdays.get(weekday).copied().unwrap_or(false) {
        return false;
    }

    let now_time = NaiveTime::from_hms_opt(now.hour(), now.minute(), 0).unwrap_or(NaiveTime::MIN);
    let start = NaiveTime::from_hms_opt(rule.start_hour as u32, rule.start_minute as u32, 0)
        .unwrap_or(NaiveTime::MIN);
    let end = NaiveTime::from_hms_opt(rule.end_hour as u32, rule.end_minute as u32, 59)
        .unwrap_or_else(|| NaiveTime::from_hms_opt(23, 59, 59).unwrap());

    if start <= end {
        now_time >= start && now_time <= end
    } else {
        now_time >= start || now_time <= end
    }
}
