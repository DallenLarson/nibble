# Nibble changelog

## 1.2.1 — the caret goes where you asked for it

**A new tab hands the address bar the caret, so Ctrl+T and typing is one motion.** A new tab
used to open with the keyboard on the page: the first letter went nowhere until you reached
for the address bar and clicked it. Now the page hands it over the moment it has painted, and
the list of recent sites stays closed until the first keystroke instead of dropping over the
page the tab opened on — typing still brings it up, one character in.

It hangs off the page's own *ready* message rather than the click that opened the tab, because
the engine takes the keyboard for its own window while a page paints, and *ready* is the last
thing that happens after that. Nibble does this only for a tab it opened, in the window you are
already in: a restored session, a tab opened behind you, a full-screen window with no address
bar on screen, and a page you have clicked into all keep the keyboard exactly where you left
it. Measured with real key presses and the UI Automation focused element — **16 of 16 checks**
in `tools/NewTabFocusProbe.ps1`, two of which check the check itself: typing does open the
recent-sites list, and clicking into the page does keep the caret there.

**Ctrl+F and Ctrl+K take the keyboard on a real website.** On any page you had clicked into,
both of them opened and then did nothing at all: every letter typed after them went into the
page, leaving the find bar empty reporting nothing and the command bar empty behind it. Ctrl+L
worked the whole time, which was the clue — the engine's surface is a child window of the
shell, and while it holds the keyboard WPF cannot move focus into a popup at all. Asking is
refused in silence, and `Focus()` returns true while it happens, so nothing looked wrong. Nibble
now parks the keyboard on a small element inside the shell and lets the popup take it from
there, which is what actually gives the page's keyboard up. Measured with real keystrokes and
the UI Automation focused element against a page served over http: **19 of 19 checks** in
`tools/FindProbe.ps1`, and **8 of those 19 fail** on the build immediately before this change.

## 1.2.0 — tabs that go where you put them, links that open behind you, and sign-in that works

**Drag a tab out of the strip and it becomes its own window.** Drag it sideways and the strip
reorders around it, with a ghost of the tab following the pointer and a paler release once the
pointer is over the page rather than the strip. Drop it on another Nibble window and that
window takes it instead. Escape calls the whole thing off. A drag is followed through the
window's own message hook rather than WPF mouse events, because the interesting half of a drag
happens over the engine's child window, where WPF stops hearing about the mouse.

**A page that calls `window.open` gets a real window again.** This is the fix for signing in
with Google through Firebase and being told *"unable to process request due to missing initial
state"*. Sign-in flows are written against `window.opener` and shared storage, and neither
survives being turned into a tab. Nibble already handed window-shaped requests to a real
window — but it built that window **before showing it**, and the engine only finishes starting
once its window exists. So `window.open` never returned, the flow died where it stood, and the
page reported nothing at all. The window is now shown first and handed over the moment the
engine is ready. Measured with a probe page that opens a window and then asks it what it can
see: `window.open` returned in 141 ms, the popup sees its opener, session storage survives the
trip, and its message arrives at the opener with a source window attached — 6 of 6 checks.

**Opening a link in a new tab no longer moves you.** Ctrl-click, a middle-click and the link
menu all open the tab behind you, leaving you on the page you were reading; a plain click on
the same link still takes you to the tab it opened. The link menu also has an *Open link in new
tab* row at the top, which the engine's own menu does not offer at all, and its *Open link in
new window* row opens a real window. Writing this turned up the matching mistake, made while
this same release was being written: the test for whether a page had asked for a *window* read
the engine's defaults - a menu bar, a toolbar, a status bar, all of which it reports for an
ordinary "new tab" request too - as the shape of a window, so **a plain click on a
`target="_blank"` link opened a window instead of a tab**. Measured with a probe that clicks
the links with a real mouse - Chromium ignores window messages posted at it - and reads back
both the window title and what each
page reports about being shown: 15 of 15 checks. The three quiet opens never once showed their
tab or hid the page you were on, and the run cost exactly one tab per click.

**Tabs are easier to close.** The close mark went from a 12 px glyph on a 20 px button to a
14 px glyph on a 26 px button, with a red hover tint. Right-clicking a tab now opens a tab
menu: close tab, **close other tabs**, **close tabs to the right** (each showing how many it
would take with it), duplicate, pin, and move to a new window. The probe closes three tabs
down to one with a single click on *close other tabs*.

**Sound keeps playing.** Chromium only calls a page audible while non-silent samples are on
their way to the output device, so a tab with sound coming out of it is now skipped by the
sleeping-tab timer — and stays skipped for twenty seconds after the last sound, so a pause, a
buffer or a quiet passage does not hand a video to the freezer mid-sentence. Measured with the
nap timer at 30 s: a backgrounded tab playing a 220 Hz tone advanced its audio 49.0 s of a
possible 49.0 s, its reports never stopped, and it was still reporting at the end of a 50 s
window in the background.

**Two bugs the new drag test found on the way.**

- **A second window no longer opens with the whole session in it.** Ctrl+N, a link that asked
  for its own window, and a popped-out tab each used to restore last session's tabs and *then*
  add the page they were opened for — so a dragged-out tab arrived with every old tab behind
  it, and a fresh window was never fresh. Only the first window of a launch restores the
  session now; windows opened afterwards start on exactly what they were opened for.
- **Closing two windows one at a time used to forget one of them.** The session is written by
  the last window to close, so the tabs of a window closed a moment earlier were already gone
  by then. Each window now hands its tabs over on the way out, and the file ends up with the
  lot whichever order they are closed in (proved by closing both windows and reading what the
  browser saved).

**New probes, all kept in `tools/`**: `PopupProbe.ps1` (a window opened by a page, asked what
it sees), `TabDragProbe.ps1` (a real drag: the pointer is moved, the button messages are
posted, and the result is read out of the session file), `TabMenuProbe.ps1` (the tab menu, and
the size of the close mark), `MediaProbe.ps1` (whether audio keeps advancing in a background
tab) and `AutoProbe.ps1` (the same window question without a click).

## 1.1.2 — smaller, and easier to call your own

**The exe is 52,787 bytes smaller: 2,228,347 → 2,175,560 bytes.** The page mark and the
wordmark were embedded in the assembly twice — once as WPF resources, because the XAML draws
them, and again as manifest resources, because `Pages.ReadEmbeddedBytes` pulled them out for
the built-in pages. The pages now read the same WPF resource, so the second copy is gone. Same
bytes end up next to the extracted pages (verified: `mark.png` 5,294 and `wordmark.png` 48,514,
identical to the source), and the whole browser is now **under 2.2 MB by any measure** — 2.08
MiB.

**How the project describes itself changed**, because the size and the licence are the two
strongest things it has:

- The repository description now leads with *free, open-source, under 2.2 MB*, and says plainly
  that it is a shell over the Chromium engine Windows already ships rather than a Chromium
  build.
- The README opens the same way, and gained two sections: **Why it is this small** (what
  borrowing the engine buys, and what it costs — no extensions, no patching the engine
  yourself) and **Make it your browser**.
- **`docs/rebranding.md`** is new: every place the name, artwork and URLs live, in the order
  that breaks something if you skip it. It calls out the five that matter — the assembly name,
  the pack URIs, the installer's AppId (give it a *new* GUID), the profile folder, and the
  update feed, which otherwise updates your fork into Nibble.
- The link-preview card (`assets/social-preview.png`) was redrawn for the new wording.

Nothing about how the browser behaves changed.

## 1.1.1 — full screen keeps its bottom

**The bug:** in full screen the bottom of the page was cut off. The window was the size of the
monitor, but the taskbar is an always-on-top window, so the last 60 px of the screen belonged
to Windows rather than to the page. Measured on a 1920x1080 screen with the window at
0,0 1920x1080: `WindowFromPoint` at 960,1050 and 960,1075 returned `MSTaskSwWClass` — the
taskbar — and the window's extended style had no `WS_EX_TOPMOST` bit.

**The fix:** full screen is now genuinely above everything. The window goes `Topmost`, and the
shell is told it is full-screen (`ITaskbarList2::MarkFullscreenWindow`), which is what makes
Explorer take the taskbar away instead of leaving it behind the page. Measured after: the
extended style reads `0x40108`, and walking the z-order puts Nibble at index 6 and the taskbar
at 206 — *in front of the taskbar*.

Two details worth keeping:

- **The z-order belongs to WPF.** Setting `WS_EX_TOPMOST` from the native side did not stick —
  WPF rewrites the window's extended style when it syncs the window, and the bit read back
  false a moment later. The window's own `Topmost` property is what holds.
- **Leaving full screen puts everything back.** Topmost cleared, the window at the rectangle it
  had before (198,-3 1525x1025 in the measurement) and the chrome rows back at their heights —
  a window that stayed on top of everything would be a worse bug than the one being fixed.

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
