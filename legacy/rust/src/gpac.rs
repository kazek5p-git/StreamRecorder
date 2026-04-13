use crate::config::AppPaths;
use std::env;
use std::fs;
use std::path::PathBuf;

pub fn resolve_mp4box_path(paths: &AppPaths) -> Option<PathBuf> {
    let mut candidates = Vec::new();

    candidates.push(paths.root_dir.join("Tools").join("GPAC").join("MP4Box.exe"));
    candidates.push(paths.root_dir.join("Tools").join("MP4Box.exe"));
    candidates.push(paths.root_dir.join("MP4Box.exe"));

    candidates.extend(program_files_candidates());

    candidates.into_iter().find(|path| path.exists())
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
