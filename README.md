# StreamRecorder

This repository currently contains two code lines:

- the active C# + WinForms rewrite in `dotnet/`
- the archived legacy Rust application in `legacy/rust/`

## Current Focus

The active release line is the WinForms rewrite. Version 1.0.0 is the first stable release of this line.

The current development work also includes an initial separate playback path
for HTTP and HTTPS stations. It uses BASS for audio output and bundles the
BASS_AAC decoder required by AAC/AAC+ Shoutcast stations. HLS and MMS playback
are still separate future work.

Main documentation:

- `dotnet/README.md`
- `docs/winforms-rewrite-status.md`
- `docs/translations.md`
- `CHANGELOG.md`

## Permanent Download Link

Every WinForms release publishes four assets: versioned and fixed-name
portable ZIP files, plus versioned and fixed-name installers. The permanent
links for the latest stable release are:

https://github.com/kazek5p-git/StreamRecorder/releases/latest/download/StreamRecorder.zip

https://github.com/kazek5p-git/StreamRecorder/releases/latest/download/StreamRecorder-setup.exe

The versioned assets use the matching release version, for example
`StreamRecorder-1.0.0.zip` and `StreamRecorder-1.0.0-setup.exe`.

## WinForms Rewrite Requirements

- `Microsoft .NET Framework 4.8`
- Official Microsoft download page: https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48

## Community Translations

The rewrite supports user-editable JSON language files.

- source translations live in `dotnet/locales/`
- packaged builds load translations from `locales/` next to `StreamRecorder.exe`
- translation workflow is documented in `docs/translations.md`

## MP4Box / GPAC

RAW AAC to M4A remuxing can use a locally available `MP4Box.exe`.
Release packages may include a lightweight GPAC-MP4Box MINI build in `Tools/GPAC/MP4Box.exe`.

Current target bundled build:

- `GPAC-MP4Box-26.02-rev0-g118e60a9_Win_GCC161.7z`
- `GPAC-MP4Box v26.02-rev0-g118e60a9-ab-suite`
- built on May 02, 2026 with GCC 16.1.0
- MINI build with encoders, decoders, audio output, and video output disabled

Lightweight Windows builds can be found from community sources such as:

- https://www.rarewares.org/files/mp4/MP4Box-GPAC-v.26.03-DEV-rev102-gfc485902-ab-suite-x64.zip
- https://forum.doom9.org/showthread.php?t=184719&page=25

Official GPAC project links:

- http://gpac.io/
- https://github.com/gpac/gpac

## Repository Layout

- `dotnet/` - active C# core, WinForms frontend, tests, and tools
- `docs/` - shared project documentation
- `scripts/` - active helper scripts for the rewrite and accessibility/parity testing
- `legacy/rust/` - earlier Rust/NWG implementation, packaging script, and PO-based localization files

## Legacy Rust Line

The older portable Rust version is still kept in the repository for reference and archival maintenance, but it is no longer the main development focus.

Its documentation and build files now live under:

- `legacy/rust/README.md`
- `legacy/rust/Cargo.toml`
- `legacy/rust/scripts/package_release.ps1`
