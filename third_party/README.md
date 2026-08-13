# Third-Party Release Files

This directory is intentionally used as a local release input area.

Do not commit downloaded third-party binary packages unless the project explicitly decides to vendor them.

## GPAC / MP4Box

To include `MP4Box.exe` in a StreamRecorder release ZIP, extract the desired GPAC-MP4Box package into:

```text
third_party/GPAC/
```

The release packaging script copies this directory into:

```text
Tools/GPAC/
```

inside the portable ZIP. StreamRecorder already probes `Tools/GPAC/MP4Box.exe`, so no application setting is required.

The expected lightweight bundle for upcoming releases is:

```text
GPAC-MP4Box-26.02-rev0-g118e60a9_Win_GCC161.7z
GPAC-MP4Box v26.02-rev0-g118e60a9-ab-suite
Built on May 02, 2026, GCC 16.1.0
MINI build: encoders, decoders, audio output, and video output disabled
```

Keep the GPAC license and any notices from the downloaded package together with `MP4Box.exe`.

Project links:

- http://gpac.io/
- https://github.com/gpac/gpac

## BASS playback library

The first playback implementation uses the BASS 2.4 library for native Windows
audio output. The release input layout is:

```text
third_party/BASS/x64/bass.dll
third_party/BASS/x86/bass.dll
third_party/BASS/bass.txt
```

The packaging script copies both architectures to `Tools/BASS/` in the portable
ZIP. `bass.txt` remains next to the DLLs because it contains the upstream
license and notices. BASS is free for non-commercial use under its upstream
license; commercial distribution requires the appropriate Un4seen license.

The playback stage also bundles the official `BASS_AAC` add-on because it is
required for AAC/AAC+ Shoutcast streams, including `audio/aacp` stations. The
files are stored next to the matching base library:

```text
third_party/BASS/x64/bass_aac.dll
third_party/BASS/x86/bass_aac.dll
third_party/BASS/bass_aac.txt
third_party/BASS/bass_aac-gpl.txt
```

The application loads the add-on automatically before opening a playback
stream. HLS, MMS, and other formats requiring additional BASS plugins remain
outside this stage.
