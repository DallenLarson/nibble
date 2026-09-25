**Nibble updates itself now.** Install it once and it keeps itself current: it checks this
repository's latest release, downloads that release's installer, and applies it at the next
launch.

## How it behaves

- The check happens about eight seconds after a window is up, at most once every six hours, and
  says nothing unless there is something to install.
- When there is, Nibble downloads the installer in the background and tells you: *"Nibble 1.1.1
  is ready — restart Nibble to install it"*, with a **restart now** action if you would rather
  not wait. Otherwise it installs itself the next time you start Nibble, before any window
  appears, and comes back on the new build.
- **Menu → Check for updates** shows where it stands (`checked 2 h ago`, or *Update to Nibble
  x.y.z — installs when you restart*).
- Nothing in your profile is touched by an update: settings, bookmarks, history, tabs and
  cookies all stay.

## Turning it off

This is the one request Nibble makes without being asked, so it is visible and switchable. The
update check asks `https://api.github.com/repos/DallenLarson/nibble/releases/latest` — a plain
HTTPS GET for a public release, no identifiers, no analytics. `Settings.Updates` in
`%AppData%\Nibble\settings.json` turns it off; `"Updates": false` and Nibble never asks again.

## What it cannot do (yet)

**The download is only as trustworthy as the release page.** These builds are unsigned: nothing
in one can prove an installer is really ours, only that it came from this repository over TLS.
That is what a code-signing certificate fixes, and it is the next thing on the list. The size
the release reports is checked, and anything that does not match is thrown away.

On a machine with **Smart App Control** switched on, the downloaded installer is refused like
any other unsigned binary — the update check will work, the install will not. Turn Smart App
Control off (one-way) or wait for signed builds.

## Install

**[Download `Nibble-1.1.0-Setup.exe`](https://github.com/DallenLarson/nibble/releases/latest)**
— per-user, no administrator prompt. Existing installs of 1.0.2 and later will pick this up on
their own; anything older needs this one download first.

Needs Windows 10/11 64-bit, the Microsoft Edge WebView2 Runtime and the .NET 8 Desktop Runtime.
