# Nibble 2.1.1

Keep your tabs intact after recoverable engine interruptions, and see downloads progressing from the toolbar.

## Install or update

Download **Nibble-2.1.1-Setup.exe** below and run it to update your existing installation. Your browsing profile is kept. For portable use, download **Nibble.exe**.

## Fixes and improvements

- A WebView2 utility-process exit no longer replaces every tab with "This page stopped".
  WebView2 automatically recovers these shared helper processes; Nibble now preserves the
  loaded pages and their state. GPU failures, subframe failures, and temporary renderer
  unresponsiveness also leave the main page intact.
- Open **Downloads** to see live percentages, received/total bytes, and progress bars.
  Downloads without a known total show an activity indicator and bytes received.
- Completed downloads show **Open file**. Interrupted downloads show their failure reason
  and cannot accidentally open an incomplete file.
- Process-failure logs now include the reason, exit code, and process description.

## Validation and limits

- Command, find-bar, private-page, and water-physics suites passed locally.
- Browser integration checks passed with real WebView2 downloads: known and unknown sizes,
  live menu updates, successful and interrupted transfers, and reopening the downloads menu.
- Injected process-failure kinds into the shell's handler to verify that recoverable events
  preserve page state and main-renderer exits still show a recovery page. The underlying
  intermittent utility-process crash has not been reproduced or prevented by this change.
- Inspected a rendered Downloads panel and verified the single-file browser build.

Run `./tools/verify.ps1 -BrowserIntegration` on Windows with WebView2 installed.
