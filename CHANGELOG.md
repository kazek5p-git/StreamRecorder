# Changelog

All notable changes to this project will be documented in this file.

## [0.2.0-alpha1] - 2026-03-31

This is the first public preview of the C# + WinForms rewrite. The stable Rust release line remains unchanged.

### Added

- New WinForms frontend with native Windows menus, dialogs, tray behavior, and separate log window.
- New C# core for config, recording, scheduler, updater, log bus, and file name template handling.
- Global schedule editor with independent start/stop entries, station selection, and `HH:mm:ss` precision.
- Crash recovery guard for the rewrite when `Restart on crash` is enabled.
- Runtime localization layer for Polish and English across the WinForms UI and core messages.
- Packaging script for framework-dependent WinForms preview ZIP files with `README.html`.
- Automated parity runners for recording, reconnect, settings, scheduler, updater, and crash recovery.

### Changed

- The rewrite now uses `Config/app.toml` as its portable configuration store.
- Unknown stream formats now log detailed probe context before continuing as `.bin`.
- RAW AAC remuxing uses locally detected `MP4Box.exe`.
- The rewrite expects `Microsoft .NET Desktop Runtime 8` on `Windows x64` instead of shipping a self-contained runtime.

### Verified

- Recording parity for MP3, AAC, HLS AAC, HLS MP3, and HTTPS MP3 streams.
- Reconnect handling during stream interruption.
- Settings persistence, startup registration, and custom file name template output.
- Schedule add/edit/delete plus timed start/stop execution.
- GitHub updater check, download, portable apply, and restart flow.
- Crash guard restart behavior.
