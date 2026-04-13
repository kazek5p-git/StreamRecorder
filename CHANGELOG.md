# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

## [0.2.0-alpha2] - 2026-04-13

### Added

- Preliminary `mms://` / `MMSH` recording support for Windows Media streams, including WMA detection and framed stream parsing.
- Translation guide for community-contributed JSON language files and deployment in packaged builds.
- ICY fallback handling for servers that respond with `ICY 200 OK` instead of a standard HTTP status line.

### Changed

- The schedule entry dialog now uses tab-focusable weekday selection fields instead of a single weekday drop-down.
- The WinForms rewrite now uses external `locales/*.json` files for user-editable translations and dynamic language discovery.
- The WinForms rewrite now targets `.NET Framework 4.8` instead of `.NET 8`.
- Empty-list and context-menu accessibility in the WinForms GUI were improved for screen reader users.
- Station URL validation now accepts stream paths without an explicit port, such as `radio.example/stream`.
- The legacy Rust/NWG application was moved into `legacy/rust/` to keep the main repository layout focused on the active rewrite.

### Verified

- WinForms build and tests on `.NET Framework 4.8`
- NVDA smoke testing of the WinForms GUI and tray behavior
- ICY/HTTP MP3 recording against `http://109.169.23.84:22510/`
- `mms://pompuj.mywire.org` recording with WMA detection

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
