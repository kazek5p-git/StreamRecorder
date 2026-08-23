# StreamRecorder WinForms Rewrite Checklist

## Rewrite Rules

- Keep the Rust application untouched until the C# rewrite reaches feature parity for basic workflows.
- Rewrite the backend first, then build the WinForms frontend on top of the new C# core.
- Avoid custom-drawn controls unless a native WinForms control is provably insufficient.
- Prefer predictable Win32 and WinForms behavior over visual novelty.

## Accessibility Baseline

- `Tab` and `Shift+Tab` must work without raw keyboard hooks for all main dialogs.
- `Escape` must close modal dialogs such as `Add/Edit station`, `Settings`, `Log`, and future schedule dialogs.
- Menu bar must be a standard Windows menu: `Alt`, `Left/Right`, `Down`, `Enter`, and `Esc` must behave natively.
- Context menus must work from both mouse right-click and the keyboard `Application` key.
- NVDA focus must always follow the active window after tray restore, dialog open, dialog close, and log window toggles.
- Never rely on color alone for recording state or errors.

## Tray Behavior

- Left click on the tray icon restores the main window and moves focus into the station list.
- Right click on the tray icon opens a real context menu.
- Minimize-to-tray must be a setting, not hard-wired behavior.
- When closing from the tray or during update, shutdown must be explicit and predictable.

## Main Window

- Keep the main window simple: station list, `Add station`, `Show log`, status bar.
- Station actions such as `Start`, `Stop`, `Edit`, `Delete`, and future `Schedules` should live in the context menu.
- The station list must not rebuild in a way that resets NVDA to the first row during refreshes.
- Column widths must be wide enough that visible users do not lose station names like `Radio Andrychów`.

## Dialog Rules

- `Add/Edit station` should use a standard tab control with native arrow-key behavior.
- `Tab` from the tab strip enters the active page.
- `Shift+Tab` from the tab strip goes to the previous logical control in wrapped order.
- Text boxes must preserve native editing keys such as `Left`, `Right`, `Ctrl+A`, `Ctrl+C`, and standard text input.

## Scheduler Redesign

- Replace the old per-station schedule tab with a separate schedules window.
- Use a list-based model with `Add`, `Edit`, and `Delete`.
- A schedule entry should target a station from the main station list.
- A schedule entry should support explicit action selection:
  - `Start recording`
  - `Stop recording`
- Time precision should support `HH:mm:ss`.
- The design must support multiple independent schedule entries, for example:
  - Thursday 07:00 start station Y
  - Friday 08:00 start station X
- Hourly recording plans must be configured per station rather than globally.
- Hourly plans must offer all-hours and selected-hours modes with 24 independent
  native check boxes from `00:00` through `23:00`.
- An active hourly plan must take precedence over ordinary schedule entries for
  the same station.

## Early Core Migration Order

1. Domain models and app configuration
2. Logging and file naming
3. Station persistence and settings persistence
4. Recording engine and stream probing
5. Scheduler redesign
6. Updater and release integration
7. WinForms shell and dialogs
