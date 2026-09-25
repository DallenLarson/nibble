# Contributing to Nibble

Thanks for wanting to help. Nibble is small on purpose — one 2.2 MB shell over the WebView2
engine Windows already ships — and the bar for changes is "does this earn its place?".

## Getting it running

You need Windows 10/11, the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0),
and the WebView2 runtime (already present on most machines; the app tells you if it is not).

```
dotnet build src/Nibble/Nibble.csproj -c Release
dotnet publish src/Nibble/Nibble.csproj -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
dist\Nibble.exe
```

`tools/package.ps1` does exactly that and then builds the installer, if you have
[Inno Setup 6](https://jrsoftware.org/isdl.php) installed.

**Heads-up:** on a machine with Smart App Control switched on, Windows refuses to run a
freshly built unsigned binary. Either sign your build, or turn Smart App Control off in
Windows Security. This is also why the .NET test suites have to be published as single-file
executables on such a machine — freshly built DLLs are blocked too.

## Tests

```
powershell -File tools/verify.ps1
```

That runs everything: the command/palette suite (27 assertions), the find-bar logic (16), the
private home page's behaviour (13), the water simulation's physics (11), plus a build-fact
check against the published exe. On a machine that blocks freshly built DLLs it publishes the
suites to `scratch/tests/` first and runs those.

There is also a full end-to-end walkthrough — first-run wizard, menu, theme shop, private
window, find bar, closing — which needs a running build:

```
powershell -File tools/SmokeTest.ps1 -Exe dist\Nibble.exe -Profile scratch\smoke-profile
```

And the updater, which is the one piece of the shell that runs an installer it downloaded
itself — so it has a test that builds a fake "newer release" and watches Nibble update into it:

```
powershell -File tools/UpdateTest.ps1        # needs an installed Nibble and Inno Setup 6
```

**Cutting a release:** bump `<Version>` in `src/Nibble/Nibble.csproj`, the version in
`src/Nibble/app.manifest`, `AppVersion` in `installer/Nibble.iss`, add a
`docs/release-notes-v<version>.md`, then tag `v<version>` — the workflow builds the installer
and publishes the release, and every installed copy picks it up on its next launch.

## What the code looks like

- `src/Nibble/Controls/` — the pixel-art primitives: `PixelPanel` (stair-stepped corners),
  `VectorIcon` + `IconData` (every icon path), `Juice` (all motion), `PixelLoader`.
- `src/Nibble/Services/` — everything that talks to the world: `Store` (JSON profile),
  `Themes` (the theme shop's catalogue), `Pages` (the built-in pages and theme assets),
  `AdBlocker`, `Urls`, `Migrator`, `SingleInstance`, `BrowserRegistration`.
- `src/Nibble/MainWindow.xaml(.cs)` — the shell, and by far the biggest file. It is one
  partial-free class in nineteen labelled sections; search for the banner comment
  (`// =====`) and the name: `startup`, `tabs`, `navigation`, `page -> shell messages`,
  `omnibox`, `shortcuts`, `popups`, `personalization`, `command palette`,
  `command implementations`, `clear data and reset`, `toasts`, `native hit testing`,
  `toolbar handlers`, `helpers`, `private windows`, `site permissions`,
  `find in page (Ctrl+F) and print (Ctrl+P)`, `window placement memory`.
  The native hit-testing, popup placement and window-lifetime rules carry comments
  explaining *why*, because each of them was a bug once.
- `src/Nibble/Assets/` — the new tab and error pages, the fonts, and `Themes/<id>/`.

House rules:

- **Theme everything through resources.** Colours come from `Themes/Light.xaml` and
  `Dark.xaml` (plus a theme's overrides); nothing hard-codes a brush.
- **Animate only `RenderTransform` and `Opacity`** so motion stays on the compositor.
- **Measure, don't assert.** If you claim a fix, say how you checked it. The probes in
  `tools/` exist for that; add one if the existing ones cannot see your case.
- **A page must never hold the browser hostage.** Closing a window is unconditional.
