# StreamRecorder

Portable Windows application written in Rust for recording audio streams without transcoding.

## Current Scope

- Native Windows GUI with a station list, station editor, schedule management, and a toggleable log view.
- Portable configuration stored in `Config/app.toml`.
- HTTP/HTTPS and HLS recording with automatic reconnect handling.
- Basic format detection for MP3, AAC, OGG, FLAC, WMA, WAV, and MPEG-TS streams.
- One schedule rule per station.
- Optional RAW AAC to M4A remuxing through external `MP4Box.exe`.
- Separate settings window for startup, topmost, tray, sleep prevention, recording folder, file naming template, remux, language, and updates.
- Runtime localization based on `locale/streamrecorder.pot`, `locale/en.po`, and `locale/pl.po`.
- Tray integration and `streamrecorder_guard.exe` crash monitoring with guarded relaunch support.

## Build

```powershell
cargo build
```

The project pins the local `x86_64-pc-windows-msvc` toolchain and MSVC linker through `.cargo/config.toml`.

## Portable Layout

- `Config/app.toml`: application settings and station list
- `Config/streamrecorder.log`: application log
- `My recordings/`: default recording output folder

## Updates

Update checks use GitHub Releases after `owner/repo` is configured in the app settings. Supported release assets are downloaded and installed through a temporary PowerShell script.
