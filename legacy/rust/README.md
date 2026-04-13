# StreamRecorder Legacy Rust Line

This directory contains the earlier Rust + native-windows-gui implementation of StreamRecorder.

It is preserved for reference and archival maintenance, but active development has moved to the WinForms rewrite in `dotnet/`.

## Features

- Native Windows GUI with station list, editor dialogs, scheduler, and log window
- Portable configuration stored in `Config/app.toml`
- HTTP, HTTPS, HLS, and reconnect-aware recording
- Optional RAW AAC to M4A remuxing through `MP4Box.exe`
- PO-based localization files in `locale/`
- Guarded relaunch support through `streamrecorder_guard.exe`

## Build

```powershell
cargo build
```

The project pins the local `x86_64-pc-windows-msvc` toolchain and MSVC linker through `.cargo/config.toml`.

## Portable Layout

- `Config/app.toml`
- `Config/streamrecorder.log`
- `My recordings/`
- `locale/`

## Packaging

```powershell
pwsh -File .\scripts\package_release.ps1
```

The script rebuilds the release binaries, copies the required portable files, and generates a `README.html` for the ZIP package.
