# StreamRecorder

Portable aplikacja Windows w Rust do nagrywania strumieni audio bez transkodowania.

## Aktualny zakres

- Natywne GUI Windows z listą stacji, edycją stacji, harmonogramem i przełączanym widokiem logu.
- Portable config w `Config/app.toml`.
- Nagrywanie HTTP/HTTPS oraz HLS z automatycznym ponawianiem połączenia.
- Wykrywanie podstawowych formatów: MP3, AAC, OGG, FLAC, WMA, WAV, MPEG-TS.
- Harmonogram jednej reguły na stację.
- Opcjonalny remux RAW AAC do M4A przez zewnętrzny `MP4Box.exe`.
- Osobne okno ustawień z opcjami startup/topmost/tray/sleep/folder/szablon nazw/remux/aktualizacje.
- Runtime tłumaczeń oparty o `locale/streamrecorder.pot`, `locale/pl.po` i `locale/en.po`.
- Tray oraz `streamrecorder_guard.exe` z automatycznym przejęciem uruchomienia, gdy monitoring awarii jest włączony.

## Build

```powershell
cargo build
```

Projekt wymusza lokalnie toolchain `x86_64-pc-windows-msvc` oraz linker MSVC przez `.cargo/config.toml`.

## Struktura portable

- `Config/app.toml`: ustawienia i lista stacji
- `Config/streamrecorder.log`: log aplikacji
- `My recordings/`: domyślny folder nagrań

## Aktualizacje

Sprawdzanie aktualizacji korzysta z GitHub Releases po ustawieniu `owner/repo` w konfiguracji, pobiera obsługiwane assety release i uruchamia instalację przez tymczasowy skrypt PowerShell.
