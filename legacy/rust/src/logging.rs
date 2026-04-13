use anyhow::{Context, Result};
use chrono::{DateTime, Local, NaiveDateTime, TimeZone};
use crossbeam_channel::{Receiver, Sender, unbounded};
use std::fs;
use std::fs::OpenOptions;
use std::io::Write;
use std::path::PathBuf;
use std::sync::{Arc, Mutex};

#[derive(Debug, Clone)]
pub struct LogEntry {
    pub timestamp: DateTime<Local>,
    pub message: String,
}

impl LogEntry {
    pub fn format_line(&self) -> String {
        format!(
            "[{}] {}",
            self.timestamp.format("%Y-%m-%d %H:%M:%S"),
            self.message
        )
    }
}

#[derive(Clone)]
pub struct LogBus {
    file_path: PathBuf,
    entries: Arc<Mutex<Vec<LogEntry>>>,
    subscribers: Arc<Mutex<Vec<Sender<LogEntry>>>>,
}

impl LogBus {
    pub fn new(file_path: PathBuf) -> Self {
        let existing_entries = load_existing_entries(&file_path).unwrap_or_default();

        Self {
            file_path,
            entries: Arc::new(Mutex::new(existing_entries)),
            subscribers: Arc::new(Mutex::new(Vec::new())),
        }
    }

    pub fn subscribe(&self) -> Receiver<LogEntry> {
        let (sender, receiver) = unbounded();
        if let Ok(mut subscribers) = self.subscribers.lock() {
            subscribers.push(sender);
        }
        receiver
    }

    pub fn entries_text(&self) -> String {
        self.entries
            .lock()
            .map(|entries| {
                entries
                    .iter()
                    .map(LogEntry::format_line)
                    .collect::<Vec<_>>()
                    .join("\r\n")
            })
            .unwrap_or_default()
    }

    pub fn push(&self, message: impl Into<String>) {
        let entry = LogEntry {
            timestamp: Local::now(),
            message: message.into(),
        };

        if let Ok(mut entries) = self.entries.lock() {
            entries.push(entry.clone());
        }

        let _ = self.append_to_file(&entry);

        if let Ok(subscribers) = self.subscribers.lock() {
            for subscriber in subscribers.iter() {
                let _ = subscriber.send(entry.clone());
            }
        }
    }

    fn append_to_file(&self, entry: &LogEntry) -> Result<()> {
        let mut file = OpenOptions::new()
            .append(true)
            .create(true)
            .open(&self.file_path)
            .with_context(|| format!("failed to open {}", self.file_path.display()))?;
        writeln!(file, "{}", entry.format_line()).context("failed to write log line")?;
        Ok(())
    }
}

fn load_existing_entries(file_path: &PathBuf) -> Result<Vec<LogEntry>> {
    if !file_path.exists() {
        return Ok(Vec::new());
    }

    let contents = fs::read_to_string(file_path)
        .with_context(|| format!("failed to read {}", file_path.display()))?;

    Ok(contents
        .lines()
        .filter_map(parse_log_line)
        .collect::<Vec<_>>())
}

fn parse_log_line(line: &str) -> Option<LogEntry> {
    let stripped = line.strip_prefix('[')?;
    let (timestamp, message) = stripped.split_once("] ")?;
    let naive = NaiveDateTime::parse_from_str(timestamp, "%Y-%m-%d %H:%M:%S").ok()?;
    let timestamp = Local.from_local_datetime(&naive).single()?;

    Some(LogEntry {
        timestamp,
        message: message.to_string(),
    })
}
