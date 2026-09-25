**Nibble is a tiny, tasty web browser for Windows.** A 2.2 MB shell over the Chromium engine
Windows already has (Microsoft Edge WebView2): Apple-clean chrome with a pixel streak —
stair-stepped corners, one accent colour you choose, hairline separators, springy motion, no
telemetry, no accounts, no services.

![The Nibble home page](https://raw.githubusercontent.com/DallenLarson/nibble/main/screenshots/05-new-tab.png)

## Install

1. Download **`Nibble-1.0.0-Setup.exe`** below and run it. It is a per-user install — no
   administrator prompt, files under `%LocalAppData%\Programs\Nibble`, Start-menu entries for
   Nibble and for a private window, and a real uninstaller in Apps & features.
2. Your profile lives in `%AppData%\Nibble`. Installing and uninstalling never touch it.

`Nibble.exe` on its own is the whole browser, if you would rather not install anything.

### The one thing to know before you download

**This build is not code-signed yet.** Depending on your machine:

- **SmartScreen** ("Windows protected your PC") is a warning: *More info → Run anyway*.
- **Smart App Control**, on a clean Windows 11 install, **refuses to run unsigned binaries
  outright and offers no override.** If you hit that, this build is not for your machine
  until there is a certificate behind it.

## What's in it

- **Tabs that behave** — they stretch when there are few, show real favicons, sleep when idle
  and come back on the next launch, with pinned tabs, duplicate-tab detection and middle-click
  close. Window buttons sit level with the tabs and light up red / yellow / green on hover.
- **Ctrl+K for everything** — 34 commands, tab / history / bookmark / closed-tab search, a
  calculator, unit conversion, `@tabs youtube`, `> clear history`.
- **Find and print** — Ctrl+F with a live match counter, F3 to step, Ctrl+P.
- **A first run that asks three questions** — your accent colour, your name and clock, your
  search engine — and a new tab page with a pixel clock, your own shortcuts and a scene that
  reacts to the theme.
- **Themes, including a shop** — *Grass Block* (a Minecraft-flavoured look, textures generated
  by `tools/Textures`, no Mojang art) and *Deep Water* (a real 128-column wave simulation you
  can stir with the pointer). Install, export and share themes as one file.
- **Private windows** — a genuine off-the-record engine profile: no history, no session, no
  suggestions, nothing on disk, and an honest line on the page about what private browsing
  cannot hide.
- **A tracker shield** — 141 ad/tracker hosts, per-tab counts, running bytes saved.
- **Migration** — bookmarks and your search engine from Chrome, Edge, Brave or Firefox.

## Requirements

- Windows 10 or 11, 64-bit
- **Microsoft Edge WebView2 Runtime** — ships with Windows 10/11 and Edge
- **.NET 8 Desktop Runtime** — the installer checks for both and offers the download links if
  either is missing

## Verified in this build

Measured on the published binary, not asserted: 67 assertions across four suites (command
parsing, the find bar, the private home page, the water simulation), a 25-step end-to-end
smoke test of the wizard and every surface, and a 16-check installer round-trip (silent
install → traces → run → silent uninstall → traces gone, profile kept). Private-window
isolation was measured against a local cookie server, and the shell measures 2.2 MB with no
debug symbols, driving Microsoft's signed, Edge-Update-maintained engine.

## Known limitations

- Unsigned, as above. No updater yet either: new versions are something you check for.
- No extensions — WebView2 cannot run them; that needs a full Chromium of Nibble's own.
- Migration is bookmarks and the search engine only. History, cookies, passwords and autofill
  are not imported, and there is deliberately no password manager or autofill.
- The theme shop lists what ships plus themes you install from a file; there is no online
  catalogue yet.

MIT licensed. Source, screenshots and the build scripts are in the repository; the changelog
lists exactly what changed, including the bugs this release pass caught.
