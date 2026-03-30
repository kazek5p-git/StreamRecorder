use crate::config::AppPaths;
use crate::gpac::resolve_mp4box_path;
use crate::localization::tr;
use crate::logging::LogBus;
use crate::models::{AppSettings, Credentials, Station, StreamFormat, StreamProtocol};
use crate::naming::build_output_path;
use crate::probe::probe_stream;
use anyhow::{Context, Result, anyhow};
use chrono::{DateTime, Local};
use futures_util::StreamExt;
use reqwest::header::CONTENT_TYPE;
use reqwest::{Client, Response};
use std::collections::{HashMap, HashSet, VecDeque};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tokio::fs::File;
use tokio::io::AsyncWriteExt;
use tokio::runtime::Runtime;
use tokio::sync::watch;
use tokio::task::JoinHandle;
use tokio::time;
use url::Url;
use uuid::Uuid;

const INITIAL_PROBE_BYTES: usize = 16 * 1024;
const SEGMENT_HISTORY_LIMIT: usize = 2048;

#[derive(Debug, Clone)]
pub struct RecordingSnapshot {
    pub station_id: Uuid,
    pub station_name: String,
    pub active: bool,
    pub state_label: String,
    pub format: Option<StreamFormat>,
    pub output_path: Option<PathBuf>,
    pub bytes_written: u64,
    pub reconnect_count: u32,
    pub last_error: Option<String>,
    pub started_at: Option<DateTime<Local>>,
}

impl RecordingSnapshot {
    fn new(station: &Station) -> Self {
        Self {
            station_id: station.id,
            station_name: station.name.clone(),
            active: false,
            state_label: "Idle".to_string(),
            format: None,
            output_path: None,
            bytes_written: 0,
            reconnect_count: 0,
            last_error: None,
            started_at: None,
        }
    }
}

struct RecordingControl {
    stop: watch::Sender<bool>,
    task: JoinHandle<()>,
}

struct RecorderManagerInner {
    runtime: Arc<Runtime>,
    tasks: Mutex<HashMap<Uuid, RecordingControl>>,
    snapshots: Mutex<HashMap<Uuid, RecordingSnapshot>>,
}

#[derive(Clone)]
pub struct RecorderManager {
    inner: Arc<RecorderManagerInner>,
}

impl RecorderManager {
    pub fn new(runtime: Arc<Runtime>) -> Self {
        Self {
            inner: Arc::new(RecorderManagerInner {
                runtime,
                tasks: Mutex::new(HashMap::new()),
                snapshots: Mutex::new(HashMap::new()),
            }),
        }
    }

    pub fn start(
        &self,
        station: Station,
        settings: AppSettings,
        paths: AppPaths,
        logs: LogBus,
    ) -> Result<()> {
        if self.is_recording(station.id) {
            return Ok(());
        }

        self.set_snapshot(RecordingSnapshot {
            active: true,
            state_label: "Connecting".to_string(),
            started_at: Some(Local::now()),
            ..RecordingSnapshot::new(&station)
        });

        let (stop_tx, stop_rx) = watch::channel(false);
        let manager = self.clone();
        let station_id = station.id;

        let task = self.inner.runtime.spawn(async move {
            let result = record_station_task(
                manager.clone(),
                station.clone(),
                settings,
                paths,
                logs.clone(),
                stop_rx,
            )
            .await;

            if let Err(error) = result {
                logs.push(format!("{}: {}", station.name, error));
                manager.fail_station(station_id, error.to_string());
            }

            manager.remove_task(station_id);
        });

        self.inner
            .tasks
            .lock()
            .expect("recorder task lock poisoned")
            .insert(
                station_id,
                RecordingControl {
                    stop: stop_tx,
                    task,
                },
            );

        Ok(())
    }

    pub fn stop(&self, station_id: Uuid) {
        if let Some(control) = self
            .inner
            .tasks
            .lock()
            .expect("recorder task lock poisoned")
            .get(&station_id)
        {
            let _ = control.stop.send(true);
        }

        self.update_snapshot(station_id, |snapshot| {
            snapshot.state_label = "Stopping".to_string();
        });
    }

    pub fn stop_all(&self) {
        let ids = self
            .inner
            .tasks
            .lock()
            .expect("recorder task lock poisoned")
            .keys()
            .copied()
            .collect::<Vec<_>>();
        for id in ids {
            self.stop(id);
        }
    }

    pub fn is_recording(&self, station_id: Uuid) -> bool {
        self.inner
            .tasks
            .lock()
            .expect("recorder task lock poisoned")
            .contains_key(&station_id)
    }

    pub fn snapshot(&self, station_id: Uuid) -> Option<RecordingSnapshot> {
        self.inner
            .snapshots
            .lock()
            .expect("snapshot lock poisoned")
            .get(&station_id)
            .cloned()
    }

    pub fn snapshots(&self) -> HashMap<Uuid, RecordingSnapshot> {
        self.inner
            .snapshots
            .lock()
            .expect("snapshot lock poisoned")
            .clone()
    }

    fn set_snapshot(&self, snapshot: RecordingSnapshot) {
        self.inner
            .snapshots
            .lock()
            .expect("snapshot lock poisoned")
            .insert(snapshot.station_id, snapshot);
    }

    fn update_snapshot(&self, station_id: Uuid, callback: impl FnOnce(&mut RecordingSnapshot)) {
        if let Some(snapshot) = self
            .inner
            .snapshots
            .lock()
            .expect("snapshot lock poisoned")
            .get_mut(&station_id)
        {
            callback(snapshot);
        }
    }

    fn mark_output_started(&self, station: &Station, format: StreamFormat, output_path: PathBuf) {
        self.update_snapshot(station.id, |snapshot| {
            snapshot.active = true;
            snapshot.format = Some(format);
            snapshot.output_path = Some(output_path);
            snapshot.state_label = format!("Recording {}", format.display_name());
            snapshot.started_at.get_or_insert_with(Local::now);
            snapshot.last_error = None;
        });
    }

    fn increment_written_bytes(&self, station_id: Uuid, bytes: usize) {
        self.update_snapshot(station_id, |snapshot| {
            snapshot.bytes_written += bytes as u64;
        });
    }

    fn note_reconnect(&self, station_id: Uuid, state_label: &str) {
        self.update_snapshot(station_id, |snapshot| {
            snapshot.reconnect_count += 1;
            snapshot.state_label = state_label.to_string();
        });
    }

    fn finish_station(&self, station_id: Uuid, state_label: &str) {
        self.update_snapshot(station_id, |snapshot| {
            snapshot.active = false;
            snapshot.state_label = state_label.to_string();
        });
    }

    fn fail_station(&self, station_id: Uuid, message: String) {
        self.update_snapshot(station_id, |snapshot| {
            snapshot.active = false;
            snapshot.state_label = format!("Error: {message}");
            snapshot.last_error = Some(message);
        });
    }

    fn update_output_path(&self, station_id: Uuid, output_path: PathBuf) {
        self.update_snapshot(station_id, |snapshot| {
            snapshot.output_path = Some(output_path);
        });
    }

    fn remove_task(&self, station_id: Uuid) {
        if let Some(control) = self
            .inner
            .tasks
            .lock()
            .expect("recorder task lock poisoned")
            .remove(&station_id)
        {
            control.task.abort();
        }
    }
}

struct OutputSession {
    file: File,
    path: PathBuf,
    format: StreamFormat,
}

async fn record_station_task(
    manager: RecorderManager,
    station: Station,
    settings: AppSettings,
    paths: AppPaths,
    logs: LogBus,
    stop: watch::Receiver<bool>,
) -> Result<()> {
    if station.url.to_ascii_lowercase().contains(".m3u8") {
        record_hls_loop(manager, station, settings, paths, logs, stop).await
    } else {
        record_http_loop(manager, station, settings, paths, logs, stop).await
    }
}

async fn record_http_loop(
    manager: RecorderManager,
    station: Station,
    settings: AppSettings,
    paths: AppPaths,
    logs: LogBus,
    mut stop: watch::Receiver<bool>,
) -> Result<()> {
    let client = build_client()?;
    let mut output: Option<OutputSession> = None;

    'recording: loop {
        if *stop.borrow() {
            break;
        }

        manager.update_snapshot(station.id, |snapshot| {
            snapshot.state_label = if snapshot.output_path.is_some() {
                "Reconnecting".to_string()
            } else {
                "Connecting".to_string()
            };
        });

        let response = request_builder(&client, &station.url, station.credentials.as_ref())
            .send()
            .await;

        let response = match response {
            Ok(response) => response
                .error_for_status()
                .with_context(|| format!("stream server returned an error for {}", station.name)),
            Err(error) => {
                Err(error).with_context(|| format!("failed to connect to {}", station.url))
            }
        };

        let response = match response {
            Ok(response) => response,
            Err(error) => {
                logs.push(format!(
                    "{} {}: {}",
                    tr("Connection failed for"),
                    station.name,
                    error
                ));
                manager.note_reconnect(station.id, "Waiting for reconnect");
                wait_before_retry(stop.clone()).await;
                continue;
            }
        };

        let content_type = header_content_type(&response);
        let mut stream = response.bytes_stream();
        let mut buffered = Vec::new();
        let mut had_payload = false;

        while buffered.len() < INITIAL_PROBE_BYTES {
            tokio::select! {
                changed = stop.changed() => {
                    if changed.is_err() || *stop.borrow() {
                        break 'recording;
                    }
                }
                maybe_chunk = stream.next() => {
                    match maybe_chunk {
                        Some(Ok(chunk)) => {
                            if !chunk.is_empty() {
                                had_payload = true;
                                buffered.extend_from_slice(&chunk);
                                if buffered.len() >= 4096 {
                                    break;
                                }
                            }
                        }
                        Some(Err(error)) => {
                            logs.push(format!("{} {}: {}", tr("Read error for"), station.name, error));
                            break;
                        }
                        None => break,
                    }
                }
            }
        }

        if *stop.borrow() {
            break;
        }

        if !had_payload {
            logs.push(format!(
                "{} {}, {}",
                tr("Stream"),
                station.name,
                tr("temporarily produced no data, retrying")
            ));
            manager.note_reconnect(station.id, "Waiting for reconnect");
            wait_before_retry(stop.clone()).await;
            continue;
        }

        if output.is_none() {
            let probe = probe_stream(&station.url, content_type.as_deref(), &buffered);
            if probe.protocol == StreamProtocol::Hls {
                return record_hls_loop(manager, station, settings, paths, logs, stop).await;
            }

            let output_path =
                build_output_path(&paths, &settings, &station, probe.extension(), Local::now())?;

            let mut file = File::create(&output_path)
                .await
                .with_context(|| format!("failed to create {}", output_path.display()))?;
            file.write_all(&buffered)
                .await
                .with_context(|| format!("failed to write {}", output_path.display()))?;

            manager.mark_output_started(&station, probe.format, output_path.clone());
            manager.increment_written_bytes(station.id, buffered.len());
            logs.push(format!(
                "{}: {} -> {} ({})",
                tr("Recording started"),
                station.name,
                output_path.display(),
                probe.format.display_name()
            ));

            output = Some(OutputSession {
                file,
                path: output_path,
                format: probe.format,
            });
        } else if let Some(session) = output.as_mut() {
            session
                .file
                .write_all(&buffered)
                .await
                .with_context(|| format!("failed to append {}", session.path.display()))?;
            manager.increment_written_bytes(station.id, buffered.len());
        }

        loop {
            tokio::select! {
                changed = stop.changed() => {
                    if changed.is_err() || *stop.borrow() {
                        break 'recording;
                    }
                }
                maybe_chunk = stream.next() => {
                    match maybe_chunk {
                        Some(Ok(chunk)) => {
                            if let Some(session) = output.as_mut() {
                                session.file.write_all(&chunk)
                                    .await
                                    .with_context(|| format!("failed to write {}", session.path.display()))?;
                                manager.increment_written_bytes(station.id, chunk.len());
                            }
                        }
                        Some(Err(error)) => {
                            logs.push(format!("{} {}: {}", tr("Connection interrupted for"), station.name, error));
                            manager.note_reconnect(station.id, "Waiting for reconnect");
                            break;
                        }
                        None => {
                            logs.push(format!("{} {}, {}", tr("Connection ended for"), station.name, tr("retrying")));
                            manager.note_reconnect(station.id, "Waiting for reconnect");
                            break;
                        }
                    }
                }
            }
        }

        if *stop.borrow() {
            break;
        }

        wait_before_retry(stop.clone()).await;
    }

    finalize_output(&manager, &station, &settings, &paths, &logs, output).await?;
    Ok(())
}

async fn record_hls_loop(
    manager: RecorderManager,
    station: Station,
    settings: AppSettings,
    paths: AppPaths,
    logs: LogBus,
    stop: watch::Receiver<bool>,
) -> Result<()> {
    let client = build_client()?;
    let mut playlist_url =
        Url::parse(&station.url).with_context(|| format!("invalid stream URL {}", station.url))?;
    let mut output: Option<OutputSession> = None;
    let mut seen_segments = HashSet::new();
    let mut segment_order = VecDeque::new();
    let mut stop = stop;

    loop {
        if *stop.borrow() {
            break;
        }

        let response =
            request_builder(&client, playlist_url.as_str(), station.credentials.as_ref())
                .send()
                .await;

        let response = match response {
            Ok(response) => response
                .error_for_status()
                .with_context(|| format!("playlist request failed for {}", playlist_url)),
            Err(error) => Err(error).context("failed to fetch HLS playlist"),
        };

        let response = match response {
            Ok(response) => response,
            Err(error) => {
                logs.push(format!(
                    "{} {}: {}",
                    tr("HLS playlist error for"),
                    station.name,
                    error
                ));
                manager.note_reconnect(station.id, "Waiting for playlist");
                wait_before_retry(stop.clone()).await;
                continue;
            }
        };

        let body = response
            .text()
            .await
            .context("failed to read playlist body")?;
        let parsed = parse_hls_playlist(&playlist_url, &body)?;

        match parsed {
            ParsedPlaylist::Master(next_variant) => {
                playlist_url = next_variant;
                continue;
            }
            ParsedPlaylist::Media {
                segments,
                target_duration,
            } => {
                let mut wrote_segment = false;

                for segment_url in segments {
                    if *stop.borrow() {
                        break;
                    }

                    let segment_key = segment_url.to_string();
                    if seen_segments.contains(&segment_key) {
                        continue;
                    }

                    let response = request_builder(
                        &client,
                        segment_url.as_str(),
                        station.credentials.as_ref(),
                    )
                    .send()
                    .await;

                    let response = match response {
                        Ok(response) => response.error_for_status(),
                        Err(error) => Err(error),
                    };

                    let response = match response {
                        Ok(response) => response,
                        Err(error) => {
                            logs.push(format!(
                                "{} {}: {}",
                                tr("HLS segment error for"),
                                station.name,
                                error
                            ));
                            continue;
                        }
                    };

                    let bytes = response
                        .bytes()
                        .await
                        .context("failed to read HLS segment")?;
                    if bytes.is_empty() {
                        continue;
                    }

                    if output.is_none() {
                        let probe = probe_stream(segment_url.as_str(), None, &bytes);
                        let output_path = build_output_path(
                            &paths,
                            &settings,
                            &station,
                            probe.extension(),
                            Local::now(),
                        )?;

                        let mut file = File::create(&output_path).await.with_context(|| {
                            format!("failed to create {}", output_path.display())
                        })?;
                        file.write_all(&bytes).await.with_context(|| {
                            format!("failed to write {}", output_path.display())
                        })?;

                        manager.mark_output_started(&station, probe.format, output_path.clone());
                        manager.increment_written_bytes(station.id, bytes.len());
                        logs.push(format!(
                            "{}: {} -> {} ({})",
                            tr("HLS recording started"),
                            station.name,
                            output_path.display(),
                            probe.format.display_name()
                        ));

                        output = Some(OutputSession {
                            file,
                            path: output_path,
                            format: probe.format,
                        });
                    } else if let Some(session) = output.as_mut() {
                        session.file.write_all(&bytes).await.with_context(|| {
                            format!("failed to append {}", session.path.display())
                        })?;
                        manager.increment_written_bytes(station.id, bytes.len());
                    }

                    remember_segment(&mut seen_segments, &mut segment_order, segment_key);
                    wrote_segment = true;
                }

                if *stop.borrow() {
                    break;
                }

                if !wrote_segment {
                    manager.update_snapshot(station.id, |snapshot| {
                        snapshot.state_label = "Waiting for HLS segments".to_string();
                    });
                }

                let wait = target_duration
                    .checked_div(2)
                    .unwrap_or_else(|| Duration::from_secs(2));

                tokio::select! {
                    _ = stop.changed() => {}
                    _ = time::sleep(wait.max(Duration::from_secs(1))) => {}
                }
            }
        }
    }

    finalize_output(&manager, &station, &settings, &paths, &logs, output).await?;
    Ok(())
}

async fn finalize_output(
    manager: &RecorderManager,
    station: &Station,
    settings: &AppSettings,
    paths: &AppPaths,
    logs: &LogBus,
    mut output: Option<OutputSession>,
) -> Result<()> {
    if let Some(mut session) = output.take() {
        session
            .file
            .flush()
            .await
            .with_context(|| format!("failed to flush {}", session.path.display()))?;

        if session.format == StreamFormat::AacRaw && settings.remux_raw_aac_to_m4a {
            if let Some(m4a_path) = remux_aac(paths, settings, logs, &session.path).await? {
                manager.update_output_path(station.id, m4a_path);
            }
        }

        manager.finish_station(station.id, "Stopped");
        logs.push(format!("{}: {}", tr("Recording stopped"), station.name));
    } else {
        manager.finish_station(station.id, "Stopped");
    }

    Ok(())
}

async fn remux_aac(
    paths: &AppPaths,
    settings: &AppSettings,
    logs: &LogBus,
    input_path: &Path,
) -> Result<Option<PathBuf>> {
    let Some(tool_path) = resolve_mp4box_path(paths, settings) else {
        logs.push(tr("Skipping AAC to M4A remux: MP4Box.exe was not found"));
        return Ok(None);
    };

    if !tool_path.exists() {
        logs.push(tr("Skipping AAC to M4A remux: MP4Box.exe was not found"));
        return Ok(None);
    }

    let input_path = input_path.to_path_buf();
    let output_path = input_path.with_extension("m4a");
    let output_path_for_log = output_path.clone();
    logs.push(format!(
        "{}: {}",
        tr("AAC to M4A remux"),
        output_path_for_log.display()
    ));

    let input_for_command = input_path.clone();
    let status = tokio::task::spawn_blocking(move || {
        Command::new(tool_path)
            .arg("-add")
            .arg(format!("{}#audio", input_for_command.display()))
            .arg("-new")
            .arg(output_path.display().to_string())
            .status()
    })
    .await
    .context("failed to wait for remux worker")?
    .context("failed to launch MP4Box")?;

    if status.success() {
        let _ = std::fs::remove_file(input_path);
        Ok(Some(output_path_for_log))
    } else {
        logs.push(tr("AAC to M4A remux failed"));
        Ok(None)
    }
}

fn build_client() -> Result<Client> {
    Client::builder()
        .user_agent("StreamRecorder/0.1.2")
        .connect_timeout(Duration::from_secs(15))
        .redirect(reqwest::redirect::Policy::limited(10))
        .build()
        .context("failed to build HTTP client")
}

fn request_builder<'a>(
    client: &'a Client,
    url: &'a str,
    credentials: Option<&'a Credentials>,
) -> reqwest::RequestBuilder {
    let builder = client
        .get(url)
        .header("Icy-MetaData", "0")
        .header("Cache-Control", "no-cache");

    if let Some(credentials) = credentials {
        builder.basic_auth(&credentials.username, Some(&credentials.password))
    } else {
        builder
    }
}

fn header_content_type(response: &Response) -> Option<String> {
    response
        .headers()
        .get(CONTENT_TYPE)
        .and_then(|value| value.to_str().ok())
        .map(|value| value.to_string())
}

async fn wait_before_retry(stop: watch::Receiver<bool>) {
    tokio::select! {
        _ = time::sleep(Duration::from_secs(3)) => {}
        _ = async {
            let mut stop = stop;
            let _ = stop.changed().await;
        } => {}
    }
}

fn remember_segment(
    seen_segments: &mut HashSet<String>,
    segment_order: &mut VecDeque<String>,
    value: String,
) {
    seen_segments.insert(value.clone());
    segment_order.push_back(value);

    while segment_order.len() > SEGMENT_HISTORY_LIMIT {
        if let Some(old) = segment_order.pop_front() {
            seen_segments.remove(&old);
        }
    }
}

enum ParsedPlaylist {
    Master(Url),
    Media {
        segments: Vec<Url>,
        target_duration: Duration,
    },
}

fn parse_hls_playlist(base_url: &Url, body: &str) -> Result<ParsedPlaylist> {
    if !body.trim_start().starts_with("#EXTM3U") {
        return Err(anyhow!("playlist is not a valid M3U8 document"));
    }

    if body.contains("#EXT-X-STREAM-INF") {
        let mut best_variant: Option<(u64, Url)> = None;
        let mut lines = body.lines().peekable();

        while let Some(line) = lines.next() {
            if !line.starts_with("#EXT-X-STREAM-INF") {
                continue;
            }

            let bandwidth = parse_bandwidth(line).unwrap_or(0);
            if let Some(target) = next_uri_line(&mut lines) {
                let resolved = base_url
                    .join(target)
                    .with_context(|| format!("failed to resolve HLS variant {target}"))?;

                match &best_variant {
                    Some((best_bandwidth, _)) if *best_bandwidth >= bandwidth => {}
                    _ => {
                        best_variant = Some((bandwidth, resolved));
                    }
                }
            }
        }

        let (_, url) = best_variant.context("master playlist does not contain variants")?;
        return Ok(ParsedPlaylist::Master(url));
    }

    let mut segments = Vec::new();
    let mut target_duration = Duration::from_secs(4);

    for line in body.lines() {
        let line = line.trim();
        if line.is_empty() {
            continue;
        }
        if let Some(value) = line.strip_prefix("#EXT-X-TARGETDURATION:") {
            if let Ok(seconds) = value.trim().parse::<u64>() {
                target_duration = Duration::from_secs(seconds.max(1));
            }
            continue;
        }
        if line.starts_with('#') {
            continue;
        }

        let resolved = base_url
            .join(line)
            .with_context(|| format!("failed to resolve HLS segment {line}"))?;
        segments.push(resolved);
    }

    Ok(ParsedPlaylist::Media {
        segments,
        target_duration,
    })
}

fn parse_bandwidth(line: &str) -> Option<u64> {
    line.split(',').find_map(|part| {
        let (_, value) = part.split_once("BANDWIDTH=")?;
        value.trim().parse::<u64>().ok()
    })
}

fn next_uri_line<'a, I>(lines: &mut I) -> Option<&'a str>
where
    I: Iterator<Item = &'a str>,
{
    for line in lines {
        let line = line.trim();
        if line.is_empty() || line.starts_with('#') {
            continue;
        }
        return Some(line);
    }
    None
}
