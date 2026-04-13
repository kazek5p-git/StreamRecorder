# WinForms Rewrite Status

## Current Stage

The C# + WinForms rewrite now builds, packages, and passes the main parity checks needed for a first public preview. It is ready for a GitHub pre-release, but it is still not intended to replace the stable Rust branch as the default release line.

## Accessibility / NVDA

### Done

- Native WinForms controls are used across the main window, dialogs, menus, list views, and tray menu.
- Key controls expose explicit accessible names.
- Main window refresh no longer recreates the station list every tick.
- Log is a separate window with native `Escape` support.
- Settings, schedule dialogs, and other modal forms use native `OK` / `Cancel` behavior.
- Focus is restored to the main window after closing modal dialogs.
- Tray restore and context menu behavior were smoke-tested.
- Live NVDA checks were already done for the older Rust GUI and informed the rewrite layout.

### Still recommended before stable

- Full live NVDA pass on the WinForms preview build from top to bottom.
- Longer manual session focused on tray focus restoration, menu narration, and context menus.

## Functional Parity vs. Rust

### Covered in the rewrite

- portable config and log files
- station add/edit/delete
- start/stop recording
- HTTP, HTTPS, and HLS recording
- reconnect behavior
- basic stream format detection
- fallback recording for `Unknown` streams with detailed logging
- optional MP4Box remux for RAW AAC
- settings persistence
- GitHub update checks and portable update application
- tray integration
- startup registration
- sleep prevention
- runtime PL/EN localization
- external JSON-based translations with dynamic language discovery from `locales/*.json`
- crash recovery with `RestartOnCrash`
- redesigned global scheduler with:
  - add/edit/delete
  - explicit station target
  - start/stop actions
  - `HH:mm:ss`

### Verified in parity runs

- recording parity for MP3, AAC, HLS AAC, HLS MP3, and HTTPS MP3
- reconnect parity
- settings persistence and startup registration
- scheduler add/edit/delete and execution
- updater check, download, install, and restart flow
- crash guard behavior

### Still outside full stable parity

- no full long-run real-world regression pass yet
- no completed full live NVDA pass on the rewrite itself yet
- the rewrite release line is still preview-only, not the default stable branch

## Packaging / Release Readiness

### Done

- rewrite requirements are documented in:
  - [README.md](/C:/Users/Kazek/Documents/StreamRecorder/README.md)
  - [README.md](/C:/Users/Kazek/Documents/StreamRecorder/dotnet/README.md)
- packaging script exists:
  - [package_winforms_release.ps1](/C:/Users/Kazek/Documents/StreamRecorder/scripts/package_winforms_release.ps1)
- packaging produces a portable `net48` ZIP with:
  - `StreamRecorder.exe`
  - required assemblies
  - empty `Config`
  - empty `My recordings`
  - `README.html`

### Recommended next milestone after pre-release

- manual install/run verification on a clean profile with only `.NET Framework 4.8`
- broader user testing of the WinForms preview
- final decision about when the rewrite becomes the default stable release line
