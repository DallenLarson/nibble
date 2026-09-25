# Nibble changelog

## 1.1.0 — Nibble updates itself

**A Nibble that is installed keeps itself current.** It asks its own release feed (GitHub's
latest release for this repository), and if there is a newer version it downloads that
release's installer and applies it **at the next launch** — not while somebody is reading
something, and not by pulling the window out from under them. On that next start the update is
applied before any window exists: the installer replaces the build, starts the new one, and the
new build says so once — *"Updated to Nibble 1.1.0 — you were on 1.0.2"* — with the release
page one click away.

- **Nothing else changes about a start.** The check runs about eight seconds after the window
  is up, at most once every six hours, and after the update has been applied it stays quiet
  until a newer release exists.
- **The menu says where it stands**: `Check for updates` with the last check behind it, or
  `Update to Nibble x.y.z — installs when you restart`. The toast that says an update is ready
  has a *restart now* action for people who would rather not wait.
- **`--check-updates`** does the whole thing without a window and writes what happened to
  `%AppData%\Nibble\updates\last-check.json`, which is what the test and any script use.
- **A copy that cannot install cannot loop.** The attempt is recorded before the installer is
  started, and the same version is not tried again for six hours; installers that are not newer
  than what is running are deleted on the next launch.

**Honest limits**, in the README too: the download is trusted because it came from this
repository over TLS, and nothing in an unsigned build can *prove* the installer is ours — that
needs the code-signing certificate, which is still the first thing on the launch checklist. The
size the feed reports is checked and a file that does not match is thrown away. On a machine
with Smart App Control on, the downloaded installer is refused like any other unsigned binary.
And this is the one request Nibble makes without being asked: **Menu → Check for updates**
shows it, and `Settings.Updates` turns it off completely (the profile keeps
`"Updates": false`).

The shell's `WM_SETCURSOR` handling from 1.0.2 is unchanged, and so is everything else.

## 1.0.2 — the pointer stops flickering over buttons

**The bug:** hovering a toolbar button made the pointer flicker between the hand and the
arrow, which reads as the mouse "glitching" or "freaking out". It was WPF's, not the
control's: Windows sends `WM_SETCURSOR` asking what the client area should show, and WPF
answers from its *cached* mouse position — which is one message behind, because the
`WM_MOUSEMOVE` that would refresh it has not been delivered yet. So for one message WPF was
answering about the pixel the pointer had just left.

Measured, with the pointer parked on one button and the app logging every message it was
sent: `real=100,67  cached=76,67  hit=PixelPanel  cursor=ARROW` — a fresh hit test at where
the pointer actually was said "button", while WPF's answer said "arrow". Sampling the cursor
at 10 ms over the same button gave `hand arrow hand arrow …`; over a link in the page, which
the engine draws itself, it never changed.

**The fix:** the shell answers `WM_SETCURSOR` for its client area from where the pointer
really is (`MainWindow.AnswerSetCursorFromPointer`), and marks the message handled so WPF's
stale answer cannot be applied on top. Everything unknown — resize edges, and any cursor a
future control asks for — is still left to WPF. After the fix the same parked measurement is
a single unbroken run of `hand`, and the engine's own cursors are untouched (a link in the
page still shows the hand).

Also in this release:

- **Text fields show the I-beam.** A `TextBox` only draws it because its default template says
  so, and Nibble's `OmniBox` style replaces that template — the address bar, find bar, command
  palette and the wizard's name field were all showing a plain arrow. They now set
  `Cursor=IBeam` explicitly.
- `tools/CursorProbe.ps1` is the regression test: it parks the real pointer on every control
  and reports any control whose cursor changes while the pointer is still on it. It samples
  4 px inside an edge, not 2 px — a control's stair-stepped outline and margin mean 2 px is
  the boundary, where alternating between a control and its parent is correct.

## 1.0.1 — the installer can no longer sit and wait

**The bug:** on a machine where `Nibble.exe` cannot start — no .NET 8 Desktop Runtime, or
Smart App Control refusing an unsigned binary — Setup hung on *"Adding Nibble to the browser
list…"* and never came back. That step ran `Nibble.exe --register-browser` with Inno's
`waituntilterminated`, so Setup was waiting on a process that would never finish, and
`runhidden` hid the dialog that would have said why.

**The fix:** the browser entries are now written as plain `[Registry]` work by the installer
itself. Nothing in Setup launches the browser any more, so no missing runtime or policy can
stall an install. Registry work cannot hang, needs no engine, and uninstalls cleanly — the
same keys, with `uninsdeletekey` / `uninsdeletevalue` doing at uninstall what
`--unregister-browser` used to be asked to do, which also removes a second place the
uninstaller could have waited forever.

Also in this release:

- **The version is shown in full.** The menu and the page footer read `Nibble 1.0.1`, not
  `Nibble 1.0` — a patch release nobody can identify is a bug report nobody can place.
- **"Launch Nibble" is only offered when it can run.** The finish-page checkbox is hidden
  when the .NET 8 Desktop Runtime is missing, instead of opening a browser that cannot start.
- `--register-browser` and `--unregister-browser` now exit the process immediately
  (`Environment.Exit`) rather than going through a WPF shutdown, so an installer or script
  calling them never waits on window teardown. Both flags still work, which is what a portable
  copy of Nibble uses.

## 1.0.0 — first release

The first version worth handing to somebody else. Everything below is in the shipping
build and was verified on it, not just written.

**The browser**

- Frameless, pixel-flavoured chrome with a real Chromium engine (the WebView2 runtime
  Windows already has). One 2.2 MB `Nibble.exe`, no services, no telemetry.
- Tabs that stretch when there are few, show favicons, sleep when idle and restore after a
  restart; pinned tabs; duplicate-tab detection; middle-click close.
- Omnibox with address-vs-search parsing and history suggestions; the command palette
  (`Ctrl+K`) with 34 actions, tab/history/bookmark search, a calculator and unit conversion.
- Find in page (`Ctrl+F`) with a live hit counter, and print (`Ctrl+P`).
- Downloads with progress toasts, a tracker shield with per-tab counts, light/dark themes,
  and a home page with a pixel clock, a hand-drawn scene and your own shortcuts.

**Private windows** — a real off-the-record engine profile: nothing written to disk, no
history, no session, no suggestions from your past. Measured with two differently named
cookies: the private jar is separate both ways, and nothing of it survives on disk.

**Themes and a theme shop** — themes recolour the whole browser and may run their own CSS/JS
on the home page. Two ship:

- **Grass Block** — the Minecraft-flavoured one: stone buttons with chunky outlines, a sky
  with block clouds, floating blocks and a grass-and-dirt horizon. Textures are generated by
  `tools/Textures`; the type is Monocraft (SIL OFL). No Mojang art.
- **Deep Water** — a real water surface on the home page: a 128-column damped spring grid,
  droplets, pointer ripples and click splashes. Splashes are volume-neutral, so it always
  levels out (11 assertions in `tools/WaterPhysicsTest.js`).

**Getting around** — one Nibble per user with command-line hand-off (`Nibble.exe <url>`,
`--private`), remembered window position, a default-browser registration you can trigger
from the menu, and `--register-browser` / `--unregister-browser` for an installer.

**An installer** — `installer/Nibble.iss`, built by `tools/package.ps1`: per-user, no
administrator prompt, Start-menu entries for the browser and for a private window, an entry in
Apps & features, the browser entries written and taken back out again, and an uninstaller that
keeps your profile unless you tell it to delete it. It checks for the .NET 8 Desktop Runtime
and the WebView2 runtime first and offers the download links if either is missing.

**Housekeeping** — first-run wizard, *Personalize Nibble*, *Clear data and reset*,
*Copy diagnostics*, and a data folder you can open in one click.

### Verification in the shipping build

| Suite | Assertions |
|---|---|
| `tools/CommandTests` | 27 — parser, calculator, converter, palette, motion helpers |
| `tools/FindLogicTest.js` | 16 — the find bar's counting and jumping |
| `tools/PrivatePageTest.js` | 13 — the private home page's behaviour |
| `tools/WaterPhysicsTest.js` | 11 — flat water, spreading ripples, settling, volume, stability |
| `tools/SmokeTest.ps1` | 25 steps — first-run wizard, every surface, the last window ending the process, an empty log |
| `tools/InstallerTest.ps1` | 16 checks — silent install, traces written, the installed browser runs, silent uninstall, traces gone, profile kept |

Plus the measured end-to-end checks recorded in the README: private-window isolation with an
offline cookie server, zero pixels of a dropdown over its own button at two window sizes,
and window placement save/restore.

Two bugs the launch pass caught and fixed, both measured on the shipping build:

- **A theme left its colours behind.** Theme overrides are written into the app's resources,
  which sit in front of the palette dictionary, so applying a theme and then going back to
  *Nibble* kept the old chrome colours until the next launch — including in any window opened
  afterwards. The overrides are now lifted off before another palette goes in.
- **The installer claimed .NET 8 was missing when it was not.** The check read only the
  registry keys the *official* .NET installer writes; Visual Studio, winget and zip installs
  leave those out, so a machine with a perfectly good .NET 8 was told to go and install one.
  It now also looks where the runtimes actually live, and never stops an unattended install to
  ask about it.
- **Dark mode kept daylight hills** on the home page: the horizon colour was hard-coded for
  light and only switched for the night easter egg. It follows the palette now.

### Known limitations

- Unsigned, so Smart App Control may refuse it on a clean Windows 11 install. The fix is a
  code-signing certificate or the Microsoft Store.
- Nothing updates it yet: a new release is something the user has to notice. WebView2 keeps
  the engine current, but the shell needs an update check or an MSIX.
- Migration brings bookmarks and the search engine; history, cookies, passwords, autofill
  and open tabs need SQLite and DPAPI work that has not been done.
- No extensions: WebView2 does not run them, and adding that support means shipping a full
  Chromium.
- No password manager and no autofill, by choice, so migrating users sign in again.
- The theme shop is local: what ships plus anything you install from a file.
