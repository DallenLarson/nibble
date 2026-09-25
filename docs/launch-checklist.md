# Before Nibble 1.0 goes public

The browser and the installer are done and verified; this is what is left around them.
Ordered by what blocks a real person from using it.

## Already in place, so it does not get re-litigated

- 1.0.0 in the project file, the app manifest, the menu and the page footer.
- `tools/package.ps1` builds `dist/Nibble.exe` (2.2 MB) and `dist/Nibble-1.0.0-Setup.exe`.
- `tools/verify.ps1` passes: 67 assertions across four suites, plus the published-exe facts.
- `tools/SmokeTest.ps1` walks the first run and every surface: 25 steps, 0 failures, empty log.
- `tools/InstallerTest.ps1`: silent install, 16 traces checked, the installed browser runs,
  silent uninstall, traces gone, profile kept.
- `screenshots/` holds eleven images regenerated from the shipping build by `tools/Shots.ps1`.
- LICENSE, the three third-party licences, CONTRIBUTING, CHANGELOG, issue templates and CI.

## Blocking

- [ ] **Sign the binaries.** `Nibble.exe`, `Nibble-1.0.0-Setup.exe` and the `unins000.exe` the
      installer writes are all unsigned. On a Windows 11 machine with Smart App Control
      switched on, Windows refuses to run them — not a warning, a block, and there is no
      "run anyway". Measured on this machine: the setup and the installed browser run, the
      fresh uninstaller is refused
      (`Microsoft-Windows-CodeIntegrity/Operational`, policy `{0283ac0f-…}`). An OV
      certificate covers all three; an EV certificate also buys SmartScreen reputation faster.
      `ISCC.exe` takes a `/S` sign tool, so signing can become a step in `tools/package.ps1`.
- [ ] **Fill in the publisher.** `installer/Nibble.iss` has `AppPublisher` "Nibble
      contributors" and `AppUrl` "https://github.com/"; `LICENSE` says "Nibble contributors"
      with no year. Set all three to the real name and URL before a release goes out.
- [ ] **Put the download in the README.** The README explains how to build the installer but
      points at no download, because there is no release yet. Tag 1.0.0, attach
      `Nibble-1.0.0-Setup.exe` and the portable `Nibble.exe`, and link the release from the
      top of the README.
- [ ] **Check the name.** "Nibble" is a common word: check the Microsoft Store name is free,
      and do a trademark search in your jurisdiction, before you print it on anything.
- [ ] **Publish the repository.** Nothing is committed yet: `git init`, commit, push, so the
      open-source claim in the README is real and the build is reproducible from a public tag.

## Before people start reporting bugs

- [ ] **Decide the release channel.** GitHub Releases with the installer and the portable exe
      is the simplest; winget (`winget install Nibble`) is a one-line submission once you have
      a stable release URL; the Microsoft Store solves signing, install and updates at once.
- [ ] **Add an updater, or say there is none.** Today nothing tells a user a new version
      exists. Either wire an update check (Squirrel/NetSparkle, or an MSIX), or put "check the
      releases page" in the README where people will see it.
- [ ] **Test on a clean machine.** Windows 10 and 11, a machine without the .NET 8 Desktop
      Runtime, a machine without WebView2, and a 150% display. The installer checks for the
      first two and offers the download links rather than failing; that check was wrong until
      this pass (it read registry keys the official .NET installer writes and Visual Studio,
      winget and zip installs do not) and is now fixed — but it has only been seen on a
      machine that *has* both runtimes.
- [ ] **Sign-off on the privacy claim.** "No telemetry" is true in the code — no analytics, no
      crash reporting, no account. A Store listing still needs a privacy policy URL, so write
      the two-paragraph version and link it.

## Nice to have before the first release

- [ ] **A short demo clip** (15–20 seconds): instant startup, Ctrl+K, the theme shop, the
      water theme's waves. This is the thing people share.
- [ ] **A verdict on extensions.** Chrome Web Store support is impossible on WebView2; it
      needs a full Chromium of Nibble's own. Write the decision down so it stops being a
      question in every thread.
- [ ] **Migrate more than bookmarks.** History, cookies, passwords, autofill and open tabs
      all need SQLite and DPAPI work. Bookmarks and the search engine already come across.
- [ ] **Update or delete these working notes** (`docs/launch-checklist.md`) once the release
      is out, and the placeholder paragraph in the README that points at them.

## Deliberately not in 1.0

Passwords, autofill, sync, extensions, a password manager, a shopping assistant, a news feed,
an AI sidebar. Each one is a decision to make later, not an oversight.
