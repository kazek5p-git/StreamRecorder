# StreamRecorder

Portable Windows application written in Rust for recording audio streams without transcoding.

## Current Scope

- Native Windows GUI with a station list, station editor, schedule management, and a toggleable log view.
- Portable configuration stored in `Config/app.toml`.
- HTTP/HTTPS and HLS recording with automatic reconnect handling.
- Basic format detection for MP3, AAC, OGG, FLAC, WMA, WAV, and MPEG-TS streams.
- One schedule rule per station.
- Optional RAW AAC to M4A remuxing through a locally available `MP4Box.exe`.
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

## GPAC / MP4Box

StreamRecorder looks for `MP4Box.exe` in the portable app folder first and can also detect standard GPAC installations in Windows program directories.

If you want a lightweight Windows build of MP4Box/GPAC instead of the full official package, use:

- https://www.free-codecs.com/mp4box-gpac_download.htm

## Release Packaging

Use the packaging script to create the portable ZIP artifact:

```powershell
pwsh -File .\scripts\package_release.ps1
```

The script rebuilds the release binaries, copies the required portable files, and generates a user-facing `README.html` from this document for the ZIP package.

## Manual Test Streams

These URLs are useful for manual regression testing. Their availability depends on the broadcaster.

- MP3: `http://s1.slotex.pl:7424/stream/1/`
- MP3: `http://sluchaj.radiopark.com.pl:8055/fest`
- AAC: `http://s1.slotex.pl:7298/;`
- HLS AAC: `http://ls.tkchopin.pl/norda/nordafm_aac_128/playlist.m3u8`
