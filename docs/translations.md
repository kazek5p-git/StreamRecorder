# Translation Guide

The WinForms rewrite uses editable JSON language files instead of hardcoded translations inside the executable.

## Where translations live

- In the source tree: `dotnet/locales/`
- In a packaged build: `locales/` next to `StreamRecorder.exe`

Each language is one JSON file:

- `en.json`
- `pl.json`
- `de.json`
- `cs.json`

The file name becomes the language code used by the app.

## Adding a new language

1. Copy `dotnet/locales/en.json` to a new file such as `dotnet/locales/de.json`.
2. Change `LanguageName` to the human-readable name you want to show in the Settings window.
3. Translate the remaining values.
4. Build or package the app, or copy the new file into `locales/` next to an existing `StreamRecorder.exe`.

Example:

```json
{
  "LanguageName": "Deutsch",
  "AppTitle": "StreamRecorder",
  "FileMenu": "&Datei",
  "HelpMenu": "&Hilfe"
}
```

## Deploying a translation without rebuilding

You can add a new language to an already compiled build:

1. Open the app folder.
2. Open `locales`.
3. Copy in a new file such as `de.json`.
4. Open Settings in StreamRecorder.
5. Reopen the language list if it was already open, select the new language, and save.

No executable changes are required.

## Rules for translators

- Keep all keys exactly as they are.
- Do not remove `LanguageName`.
- Save the file as `UTF-8`.
- Do not use `ANSI`, legacy Windows code pages, or other non-Unicode encodings.
- Keep placeholder markers such as `{0}`, `{1}`, `{2}` unchanged.
- Keep access-key markers such as `&File` if the string uses them.
- Keep escape sequences such as `\n` unchanged when present.
- The JSON must remain a flat `key -> string` object.

## Fallback behavior

- If a language file is missing a key, StreamRecorder falls back to its internal defaults.
- For custom languages this effectively means English fallback.
- If a JSON file is invalid, the app ignores it and falls back to defaults instead of crashing.

## Recommended workflow

For contributors:

1. Copy `en.json`.
2. Translate values one by one.
3. Save the file as `UTF-8`.
4. Run the app and switch to the new language in Settings.
5. Check menus, dialogs, tray text, and log messages for truncated or missing text.

For end users:

1. Download or create a `*.json` language file.
2. Put it into the `locales` folder next to `StreamRecorder.exe`.
3. Select it in Settings.

## Notes for maintainers

- Source translations live in `dotnet/locales/`.
- The WinForms project copies `dotnet/locales/*.json` into the build output automatically.
- If new UI text is added to the code, add the same key to `en.json` first, then update the other language files.
