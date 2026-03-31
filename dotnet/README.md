# StreamRecorder WinForms Rewrite

This directory contains the in-progress C# + WinForms rewrite of StreamRecorder.

## Current State

The rewrite is already usable as a preview build. It includes:

- WinForms main window with station list, context menu, tray integration, log window, settings, and global schedules
- HTTP, HTTPS, and HLS recording
- reconnect handling
- stream format detection for MP3, AAC, OGG, FLAC, WMA, WAV, and MPEG-TS
- detailed fallback logging for unknown stream formats
- optional RAW AAC to M4A remuxing through a locally available `MP4Box.exe`
- runtime language switching between Polish and English
- portable `Config/app.toml`
- GitHub release update checks
- crash recovery through an internal guard mode when `Restart on crash` is enabled

This build is still intended as a preview and not yet as a replacement for the stable Rust release.

## Requirements

- `Microsoft .NET Desktop Runtime 8`
- `Windows x64`
- Official Microsoft download page: https://dotnet.microsoft.com/en-us/download/dotnet/8.0
- On the Microsoft page, choose `Windows x64` under `.NET Desktop Runtime 8`

The framework-dependent rewrite build is intentionally small and expects the desktop runtime to be installed on the system.

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

The packaging script creates a framework-dependent portable ZIP with:

- `StreamRecorder.exe`
- required `.NET` assemblies
- empty `Config`
- empty `My recordings`
- `README.html`

## Notes

- The rewrite uses `Config/app.toml`, not INI.
- The preview build should be tested with keyboard navigation, tray behavior, updater flow, and real-world recording before a stable release replaces the Rust branch.
