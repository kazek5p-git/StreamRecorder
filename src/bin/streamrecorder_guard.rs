use std::env;
use std::path::PathBuf;
use std::process::{Command, ExitStatus};
use std::thread;
use std::time::Duration;

fn main() {
    let (exe, child_args) = resolve_target();
    let mut restart_count = 0u32;

    loop {
        let status = Command::new(&exe).args(&child_args).status();
        match status {
            Ok(status) if is_clean_exit(&status) => break,
            Ok(status) => {
                restart_count += 1;
                eprintln!(
                    "streamrecorder_guard: {} exited with code {:?}, restart attempt {}",
                    exe.display(),
                    status.code(),
                    restart_count
                );
            }
            Err(error) => {
                restart_count += 1;
                eprintln!(
                    "streamrecorder_guard: failed to launch {}: {}",
                    exe.display(),
                    error
                );
            }
        }

        if restart_count >= 3 {
            eprintln!("streamrecorder_guard: too many restart attempts, stopping");
            break;
        }

        thread::sleep(Duration::from_secs(3));
    }
}

fn resolve_target() -> (PathBuf, Vec<String>) {
    let mut args = env::args().skip(1);
    let mut exe = None;
    let mut child_args = Vec::new();

    while let Some(arg) = args.next() {
        match arg.as_str() {
            "--exe" => {
                if let Some(path) = args.next() {
                    exe = Some(PathBuf::from(path));
                }
            }
            "--" => {
                child_args.extend(args);
                break;
            }
            _ => {}
        }
    }

    (exe.unwrap_or_else(default_target_exe), child_args)
}

fn default_target_exe() -> PathBuf {
    env::current_exe()
        .ok()
        .and_then(|path| {
            path.parent()
                .map(|parent| parent.join("streamrecorder.exe"))
        })
        .unwrap_or_else(|| PathBuf::from("streamrecorder.exe"))
}

fn is_clean_exit(status: &ExitStatus) -> bool {
    status.success() || status.code() == Some(0)
}
