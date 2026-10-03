# Nibble 2.1.0

A smoother browsing update with PDF viewing, a personal notebook, and a calendar built into the home page.

## Install or update

Download **Nibble-2.1.0-Setup.exe** below and run it to update your existing installation. Your browsing profile is kept. For portable use, download **Nibble.exe**.

## Highlights

- Fewer false connection errors when following links or being redirected.
- Use Up/Down and Enter to choose address-bar suggestions.
- Read PDFs in a tab, including local files opened with Ctrl+O.
- Write autosaving notes and manage calendar events with start and end times.
- Notes and events stay on your computer; private windows keep them only until the window closes.

## Fixes

Navigation completions are now matched to their navigation IDs. A canceled or superseded load,
a download handoff, or a document that already rendered no longer gets replaced by Nibble's
generic connection error. The old asynchronous text-length heuristic is gone: images, PDFs,
and short pages are valid documents too. Real current-navigation failures still report an error;
certificate checking and the engine's error pages remain enabled.

Up/Down selects visible suggestions in the URL bar; Enter opens the selected item. The popup
row template now uses its selection background, and arrow keys reopen a closed suggestion list.

PDFs use WebView2's built-in viewer. Nibble no longer disables runtime component extensions with
the broad `--disable-extensions` flag; user extensions remain disabled through the supported
environment option. Ctrl+O and the Open PDF menu item open local files. Clicking a completed PDF
download opens it in a Nibble tab. Servers that require attachment downloads still download first.

## Notes and calendar

The home page links to both tools. Notes save as you type, with titles and plain text. The calendar
supports month navigation and creating, editing, and deleting events with start/end dates, local
times, and details. Deleting an item asks for confirmation.

Normal windows store data in `planner.json` in the profile directory. Updates replace that file
atomically, and unreadable files are not silently overwritten. Private windows use isolated
in-memory planners; closing the window discards them. Other websites cannot access the planner
message API. Full profile reset or deleting the profile also removes saved notes and events.
There is no cloud sync, recurrence, or reminder notification service in this version.

## Validation

- Existing verification suites plus regression tests for cancellation, stale navigation events,
  downloads, HTTP error pages, local paths, planner persistence, time validation, and isolation.
- `tools/BrowserTests` exercises the actual shell and WebView2 with an isolated test profile:
  visible arrow-key selection, note persistence, calendar events, local/HTTP PDFs, clicked redirects,
  overlapping navigations, download handoffs, private planners, and rejection of untrusted messages.
- Notes, calendar, narrow calendar layout, and rendered PDF screenshots inspected.
- Google results and the Google Docs entry point checked with a signed-out profile. Docs reached
  Google's sign-in page. Authenticated editing of the user's documents has not been verified;
  these changes cannot resolve an actual network outage or server-side refusal.

Run `./tools/verify.ps1 -BrowserIntegration` on Windows with WebView2 installed. To include public
Google checks, run the published BrowserTests executable with `--web`.
