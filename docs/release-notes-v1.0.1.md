**1.0.1 is a fix for one bug in the installer:** on a machine where `Nibble.exe` cannot start,
Setup hung on *"Adding Nibble to the browser list…"* and never finished.

## What was wrong

That step ran `Nibble.exe --register-browser` and waited for it to exit. If the browser could
not start — no .NET 8 Desktop Runtime, or Smart App Control refusing an unsigned binary —
there was nothing to wait for, and the installer stayed on that line. Worse, the windows-style
dialog that would have explained it was launched hidden, so it looked like a freeze with no
cause.

## What to do if you are stuck on 1.0.0

1. **Cancel the installer.** Everything except the browser-list entry is already installed.
2. Nibble itself will be in your Start menu. If it does not open, this machine is missing the
   **.NET 8 Desktop Runtime** — install that first, then run this installer again.
3. If Nibble opens but Windows does not list it under default apps, use **Menu → Set as
   default browser**, which does the same registration from inside the app.

## What changed

- **The installer writes the browser entries itself** (plain registry work) instead of
  launching the browser and waiting. No missing runtime, policy or wedged process can stall
  an install now, and uninstalling removes the same entries the same way.
- **The version shows in full** — the menu and the page footer read `Nibble 1.0.1` instead of
  `Nibble 1.0`, so a bug report can name the build.
- **"Launch Nibble" is hidden** when the .NET 8 Desktop Runtime is missing, rather than
  offering to open something that cannot start.
- `--register-browser` / `--unregister-browser` exit immediately, for installers and scripts
  (a portable copy of Nibble still uses them).

Nothing about the browser itself changed.

## Install

**[Download `Nibble-1.0.1-Setup.exe`](https://github.com/DallenLarson/nibble/releases/latest)**
— per-user, no administrator prompt. `Nibble.exe` on its own is there too.

Needs Windows 10/11 64-bit, the Microsoft Edge WebView2 Runtime (ships with Windows) and the
.NET 8 Desktop Runtime. The installer checks for both and offers the download links.

**Still unsigned.** On a machine with Smart App Control switched on, Windows refuses to run
unsigned binaries and offers no override — see the 1.0.0 notes for the details. A signing
certificate is the next thing on the list.
