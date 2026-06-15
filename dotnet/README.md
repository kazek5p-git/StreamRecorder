# StreamRecorder WinForms Rewrite

This directory contains the in-progress C# + WinForms rewrite of StreamRecorder.

## Current State

The rewrite is already usable as a preview build. It includes:

- WinForms main window with station list, context menu, tray integration, log window, settings, and global schedules
- HTTP, HTTPS, and HLS recording
- preliminary `mms://` / `MMSH` recording for Windows Media streams
- reconnect handling
- stream format detection for MP3, AAC, OGG, FLAC, WMA, WAV, and MPEG-TS
- detailed fallback logging for unknown stream formats
- optional RAW AAC to M4A remuxing through a locally available `MP4Box.exe`
- runtime language switching from editable `locales/*.json` files
- portable `Config/app.toml`
- GitHub release update checks
- crash recovery through an internal guard mode when `Restart on crash` is enabled

This build is still intended as a preview and not yet as a replacement for the stable Rust release.

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
- empty `My recordings`
- `README.html`

## Translations

The rewrite no longer keeps translations hardcoded inside the executable.

- Source translations live in `dotnet/locales/`
- Built applications load translations from `locales/` next to `StreamRecorder.exe`
- New languages can be added by dropping in a new JSON file such as `de.json`
- The language list in Settings is built dynamically from the JSON files found in `locales`

Detailed instructions are documented in:

- `docs/translations.md`

## MP4Box / GPAC

If you want a lightweight Windows build of `MP4Box.exe` instead of the full GPAC package, use one of these community sources:

- https://www.rarewares.org/files/mp4/MP4Box-GPAC-v.26.03-DEV-rev102-gfc485902-ab-suite-x64.zip
- https://forum.doom9.org/showthread.php?t=184719&page=25

## Notes

- The rewrite uses `Config/app.toml`, not INI.
- The preview build should be tested with keyboard navigation, tray behavior, updater flow, and real-world recording before a stable release replaces the Rust branch.
