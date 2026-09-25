# Make it your browser

Nibble is MIT licensed, which means you can take it, rename it, and ship it as your own
browser. Because it is a shell over the engine Windows already ships, there is no Chromium to
check out or build: this repository is the whole product, and a full build takes seconds
rather than hours.

This page is the list of places the name, the artwork and the URLs live, in the order that
breaks something if you skip it.

## Before you start

- **Keep the licences.** `LICENSE`, `Icons-LICENSE.txt`, `Silkscreen-OFL.txt`,
  `Monocraft-OFL.txt` and `THIRD-PARTY-NOTICES.txt` travel with the icons and fonts inside the
  app; MIT and the two font licences require that they stay with any copy you distribute.
- **Ask yourself what your build is for.** If you want extensions or a patched engine, this is
  the wrong base: WebView2 cannot run Chrome extensions, and you cannot change the engine. If
  you want a small, fast, good-looking browser you can shape, it is the right one.

## The five things that actually break if you miss them

| Where | What | Why |
|---|---|---|
| `src/Nibble/Nibble.csproj` | the file name (it is the assembly name) | the pack URIs below are resolved by assembly name |
| `src/Nibble/Services/Theme.cs`, `Services/Themes.cs`, `Services/Pages.cs` | `pack://application:,,,/Nibble;component/...` | must match the assembly name, or styles and brand art fail to load at runtime |
| `installer/Nibble.iss` | `AppId={{8A1D2B6C-...}}` | **give it a new GUID.** Two products sharing one AppId overwrite each other's uninstall entry |
| `src/Nibble/Services/Store.cs` | `%AppData%\Nibble` | a different folder, so your browser and Nibble never share a profile |
| `src/Nibble/Services/Updater.cs` | `FeedUrl` | points at *this* repository: leave it and your browser updates itself into Nibble |

## The rest, file by file

**Identity**

- `src/Nibble/app.manifest` — `assemblyIdentity name="Nibble.app"`.
- `src/Nibble/MainWindow.xaml` — the window `Title` and the wordmark image in the header.
- `src/Nibble/MainWindow.xaml.cs` — user-visible strings, the menu's version row, and the
  releases URL used by the "updated to" toast.
- `src/Nibble/OnboardingWindow.xaml(.cs)` — the first-run wizard's "Set up Nibble" title and
  copy, `N I B B L E` in the header.
- `src/Nibble/ThemesWindow.xaml(.cs)` — the theme shop's title and "by Nibble" fallback.
- `src/Nibble/Assets/newtab.html` — the home page's title, the footer line, the tips, the
  hand-pinned `DECKRISE` shortcut (that one is the author's own site: replace it), and the
  private-window wording.
- `src/Nibble/Assets/error.html`, `Assets/Themes/*/theme.json` (`"author"`).

**Windows integration**

- `src/Nibble/Services/BrowserRegistration.cs` — the ProgID (`NibbleHTML`),
  `Software\Clients\StartMenuInternet\Nibble`, and the description Windows shows in the
  default-apps list.
- `src/Nibble/Services/SingleInstance.cs` — the mutex and pipe names, so your build and
  Nibble can run side by side during development.
- `src/Nibble/Services/Pages.cs` — the `nibble.pages` virtual host for the built-in pages.

**Artwork** (`tools/Branding` regenerates the icons from `assets/nibble-logo.png`; the page
mark and wordmark are `src/Nibble/Assets/nibble-mark.png` and `nibble-wordmark.png`)

- `assets/nibble.png`, `assets/nibble-logo.png`, `src/Nibble/Assets/nibble.ico`
- `tools/SocialCard.ps1` draws the link-preview card and `tools/Shots.ps1` the README
  screenshots — both have the name in their text.

**Installer** (`installer/Nibble.iss`)

- `AppName`, `AppVersion`, `AppPublisher`, `AppUrl`, `AppExeName`, the output file name, the
  licence and notes pages, and the shortcut names.

**Build and CI**

- `tools/package.ps1` — the paths and file names it publishes.
- `tools/InstallerTest.ps1`, `tools/UpdateTest.ps1` — the uninstall key (the AppId above) and
  the exe path, so the round-trip tests keep working under the new name.
- `.github/workflows/build.yml` — the artifact name and the release's asset names.

## Checking your work

```
powershell -File tools/package.ps1     # builds the browser and your installer
powershell -File tools/verify.ps1      # 39 assertions + the published-exe facts
powershell -File tools/SmokeTest.ps1 -Exe dist\YOURBROWSER.exe -Profile scratch\smoke
powershell -File tools/InstallerTest.ps1 -Setup dist\YOURBROWSER-1.0.0-Setup.exe
```

The smoke test walks the wizard, the menu, the theme shop, a private window and the find bar;
the installer test installs, runs, uninstalls and checks the profile survives. Between them
they catch the mistakes this list is about — a stale pack URI shows up as a missing logo, a
stale AppId as a broken uninstall, a stale feed URL as an update into somebody else's browser.

Finally, search for what is left:

```
rg -i "nibble" --glob '!dist' --glob '!scratch'
```

Comments and prose still say Nibble, which is fine — the list above is the code that has to
change for it to be yours.

## Shipping it

Two things worth knowing before you hand your build to anybody:

- **Sign it.** An unsigned build is refused outright on Windows 11 machines with Smart App
  Control on, and warned about everywhere else. See the README's "Before you ship it to
  strangers".
- **If you keep the updater, keep it pointed at your own releases** and publish the same
  `YOURBROWSER-<version>-Setup.exe` asset naming, or turn updates off in `Settings.Updates`.
