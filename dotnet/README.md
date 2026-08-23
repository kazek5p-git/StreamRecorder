# StreamRecorder WinForms Rewrite

This directory contains the C# + WinForms release line of StreamRecorder.

## Current State

The stable 1.0.1 build includes:

- WinForms main window with station list, context menu, tray integration, log window, settings, and global schedules
- HTTP, HTTPS, and HLS recording
- preliminary `mms://` / `MMSH` recording for Windows Media streams
- reconnect handling
- optional time-based recording splitting configured in Settings
- stream format detection for MP3, AAC, OGG, FLAC, WMA, WAV, and MPEG-TS
- detailed fallback logging for unknown stream formats
- optional RAW AAC to M4A remuxing through a locally available `MP4Box.exe`
- runtime language switching from editable `locales/*.json` files
- portable `Config/app.toml`
- GitHub release update checks
- crash recovery through an internal guard mode when `Restart on crash` is enabled
- first playback preview for HTTP and HTTPS stations through the bundled BASS backend
- optional per-station ICY stream title capture to timestamped UTF-8 text files next to recordings
- optional global CUE sheet generation from ICY stream titles

This release line replaces the archived Rust application as the active version.

## Requirements

- `Microsoft .NET Framework 4.8`
- Official Microsoft download page: https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48

The rewrite depends on the standard .NET Framework installation that is already present on many Windows systems.

## Build

```powershell
dotnet build .\dotnet\StreamRecorder.Rewrite.sln -c Release
```

## Test

```powershell
dotnet test .\dotnet\StreamRecorder.Rewrite.sln -c Release
```

## Run

```powershell
dotnet run --project .\dotnet\src\StreamRecorder.WinForms\StreamRecorder.WinForms.csproj -c Release
```

## Package

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package_winforms_release.ps1
```

The packaging script creates a portable ZIP for the `net48` build with:

- `StreamRecorder.exe`
- required `.NET` assemblies
- empty `Config`
- `README.html`

On the first run, the default recording folder is created as
`%USERPROFILE%\Documents\StreamRecorder`. The portable package does not create
a recording folder next to `StreamRecorder.exe`. A manually selected recording
path remains unchanged, and existing recordings are not moved automatically.

The package is written using both the versioned filename
`StreamRecorder-1.0.1.zip` and the fixed filename `StreamRecorder.zip`. Use
this permanent link for the latest stable release:

https://github.com/kazek5p-git/StreamRecorder/releases/latest/download/StreamRecorder.zip

The installer is published in the same way: `StreamRecorder-1.0.1-setup.exe`
and the fixed `StreamRecorder-setup.exe`. Its permanent link is:

https://github.com/kazek5p-git/StreamRecorder/releases/latest/download/StreamRecorder-setup.exe

GitHub's `latest` endpoint intentionally ignores pre-releases. During preview
testing, use the same fixed asset name with the tag of the selected release:

`https://github.com/kazek5p-git/StreamRecorder/releases/download/<RELEASE_TAG>/StreamRecorder.zip`

For a normal Windows installation, build the Inno Setup installer with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build_winforms_installer.ps1 -Version 1.0.1
```

The installer offers English and Polish at startup and installs by default into
the current user's profile, so StreamRecorder can write its configuration and
recordings without administrator privileges.

Installed builds store configuration and logs in
`%LOCALAPPDATA%\StreamRecorder\Config`, independently of the application
installation directory. On the first launch after upgrading from an older
build, an existing `Config\app.toml` beside the executable is copied to the
user-data directory without deleting the original. Portable builds continue to
use `Config\app.toml` beside the executable.

## Stream title capture

To save station metadata titles, select a station, open its context menu, and
enable `Save stream titles`. During recording, StreamRecorder requests ICY
metadata when the station supports it and writes title changes with local
timestamps to a UTF-8 text file next to the corresponding recording, using the
same base name and the `.txt` extension. Recording segments created by the
split-recording option receive their own title file.

The option is stored per station and can be changed while recording. Disabling
it stops writing new titles without stopping the audio recording.

## CUE sheets

Enable `Create CUE sheets from stream titles` in Settings to create a `.cue`
file next to each recording segment that contains stream-title changes as
relative `MM:SS:FF` indexes. This setting is independent of the per-station
text capture option, so either format or both formats can be enabled. CUE
support is generally strongest for MP3, FLAC, OGG, and WAV files; other formats
are still written with a `BINARY` CUE file type and may have limited player
support.

If `third_party/GPAC/MP4Box.exe` exists, the packaging script also copies the extracted GPAC files into `Tools/GPAC/` inside the ZIP and adds `THIRD-PARTY-NOTICES.txt`.

If the BASS files and the `BASS_AAC` add-on exist under `third_party/BASS/`,
the package also contains both architectures under `Tools/BASS/`. The
application uses the matching DLL for the current process architecture and
loads `bass_aac.dll` automatically for AAC/AAC+ streams. The playback stage
accepts HTTP and HTTPS stream URLs; HLS, MMS, and other formats requiring
additional BASS plugins are not included in this stage.

## Translations

The rewrite no longer keeps translations hardcoded inside the executable.

- Source translations live in `dotnet/locales/`
- Built applications load translations from `locales/` next to `StreamRecorder.exe`
- New languages can be added by dropping in a new JSON file such as `de.json`
- The language list in Settings is built dynamically from the JSON files found in `locales`

Detailed instructions are documented in:

- `docs/translations.md`

## MP4Box / GPAC

Release packages may include a lightweight GPAC-MP4Box MINI build in `Tools/GPAC/MP4Box.exe`. StreamRecorder automatically checks this location, so no user configuration is needed when the file is bundled.

Current target bundled build:

- `GPAC-MP4Box-26.02-rev0-g118e60a9_Win_GCC161.7z`
- `GPAC-MP4Box v26.02-rev0-g118e60a9-ab-suite`
- built on May 02, 2026 with GCC 16.1.0
- MINI build with encoders, decoders, audio output, and video output disabled

Official GPAC project links:

- http://gpac.io/
- https://github.com/gpac/gpac

If you want a lightweight Windows build of `MP4Box.exe` instead of the full GPAC package, use one of these community sources:

- https://www.rarewares.org/files/mp4/MP4Box-GPAC-v.26.03-DEV-rev102-gfc485902-ab-suite-x64.zip
- https://forum.doom9.org/showthread.php?t=184719&page=25

## Playback / BASS

The context menu can start and stop a separate audio connection for the
selected station. Playback does not reuse the recording connection, so a
station can be recorded and listened to at the same time. Only one station is
played at a time; starting another station stops the previous preview.

The output device is selected in Settings. The default option uses the normal
Windows audio device. The BASS 2.4 library is free for non-commercial use
under its upstream terms; commercial distribution requires the appropriate
Un4seen license. The full license is kept in `third_party/BASS/bass.txt` and
is copied into the release package.

## Notes

- The rewrite uses TOML, not INI: portable builds use `Config/app.toml`, while installed builds use `%LOCALAPPDATA%\StreamRecorder\Config\app.toml`.
- The stable build should continue to be tested with keyboard navigation, tray behavior, updater flow, and real-world recording.
