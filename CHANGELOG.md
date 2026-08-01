# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Fixed

- Windows Task Scheduler synchronization now treats a missing `\StreamRecorder` task folder as a normal first-run state instead of logging `0x80070002`.
- Closing StreamRecorder while a recording is active now shuts down recording sessions more deterministically and avoids late UI callbacks after the main window handle is gone.

## [0.2.0-alpha10] - 2026-07-31

### Added

- Recording schedules can now optionally be synchronized with Windows Task Scheduler so scheduled starts can launch StreamRecorder minimized to the tray.

### Changed

- Schedule entries now use a single recording window with start and end times instead of separate start/stop action entries.
- Legacy action-based schedule entries are migrated to the new start/end schedule model when loading `app.toml`.
- Windows scheduled tasks wording was simplified in the settings and log messages.
- Successful Windows scheduled tasks synchronization is now written to the log.

## [0.2.0-alpha9] - 2026-07-05

### Changed

- Release packaging can now include an optional local `third_party/GPAC` MP4Box bundle as `Tools/GPAC` in the portable ZIP.
- Documentation now records the target lightweight GPAC-MP4Box MINI build for bundled release packages.

## [0.2.0-alpha8] - 2026-07-05

### Added

- Optional time-based recording splitting with hours, minutes, and seconds configured in Settings.
- The system tray tooltip now includes the number of currently recording streams.

### Fixed

- Recording byte counter updates no longer trigger immediate full UI refreshes, reducing screen reader lag during active recording.
- Stream reads and HTTP response waits now have inactivity timeouts so interrupted connections can reconnect and `Stop` can finish instead of staying on `Stopping`.

## [0.2.0-alpha7] - 2026-06-15

### Fixed

- MMSH request headers now build the `xClientGUID` value without throwing before the connection attempt.

### Changed

- The README now lists the Doom9 MP4Box thread as an additional lightweight MP4Box source alongside the existing RareWares archive.

## [0.2.0-alpha6] - 2026-06-15

### Changed

- Schedule entries can now target multiple weekdays in one task, such as Monday through Friday at the same time.
- The schedule entry action selector now uses radio buttons instead of a drop-down list for better screen reader feedback.
- The schedule list now displays all selected weekdays for each entry.

### Fixed

- Existing single-day schedule entries continue to load correctly after the multi-day schedule model change.

## [0.2.0-alpha5] - 2026-04-19

### Fixed

- Closing the log window no longer blocks `Exit` or `Alt+F4` from shutting down the main application.
- Minimizing to tray now removes the main window from `Alt+Tab`.
- First-entry accessibility for the station context menu was improved for NVDA on the first keyboard open.
- WinForms release packaging now includes the `locales` folder with `en.json` and `pl.json` instead of shipping an empty directory.

### Changed

- The main menu startup priming was refined so the first `Alt` activation remains readable without breaking normal open/close menu toggling.

## [0.2.0-alpha4] - 2026-04-15

### Changed

- The main WinForms window now exposes `Schedules` as a dedicated button instead of a station context-menu action.
- The station list no longer relies on the previous empty-list placeholder workaround after the `.NET Framework 4.8` rewrite fixes improved default screen reader behavior.

### Fixed

- Adding the first station and deleting the last station no longer crash or stall the WinForms main window after the empty-list accessibility cleanup.
- Station context-menu refresh noise was reduced for screen readers, improving NVDA readability while moving through menu items.

## [0.2.0-alpha3] - 2026-04-13

### Fixed

- Polish localization in the WinForms rewrite now falls back to embedded UTF-8 JSON resources instead of garbled hardcoded strings.

### Changed

- The translation guide now explicitly requires `UTF-8` for community language files.

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
