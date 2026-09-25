# Security

Nibble is a small shell around the engine Windows already has: Microsoft Edge WebView2.
That split decides who owns what, so here it is plainly.

## What Nibble is responsible for

- The profile in `%AppData%\Nibble` — settings, history, bookmarks, session, theme files and
  the engine's own `WebView2` folder.
- Private windows, which run an off-the-record engine profile and write nothing about
  themselves to disk.
- The tracker shield: a host-name blocklist, applied to the requests a page makes.
- Site permissions: camera, microphone, location and notifications are refused unless the
  person turns them on.
- Browser registration and the installer: everything lands under `HKCU` — no admin rights,
  nothing machine-wide.
- The built-in pages (`newtab.html`, `error.html`) and the fonts and icons that ship with the
  app. Third-party licences are in `Icons-LICENSE.txt`, `Silkscreen-OFL.txt` and
  `Monocraft-OFL.txt`.

## What it is not

- **The engine is not Nibble's.** Rendering, the sandbox, TLS, the network stack and the V8
  JIT are Microsoft's WebView2, updated by Edge Update. A WebView2 vulnerability belongs to
  Microsoft (their security inbox is `secure@microsoft.com`, or the MSRC portal), though a
  report here is still welcome if Nibble's flags or settings are what make it reachable.
- **The shield is not a firewall.** It matches host names and blocks requests to networks on
  that list. It does not block first-party trackers, does not rewrite pages, and is not a
  substitute for a VPN or for a browser hardened against a hostile network.
- **No extensions, no password manager, no autofill** — there is no code path for them to be
  abused through.
- **Private windows do not hide you from the network.** Your router and ISP still see which
  hosts you reach.

## Reporting something

Use GitHub's private reporting: **Security → Report a vulnerability** on this repository
(Security Advisories). That keeps the details out of public issues until there is a fix.

Useful in a report:

- what you did, what happened, and what you expected;
- the Nibble version (the bottom row of the menu), and the Windows version;
- whether it needs a normal window, a private one, or the installer;
- a proof of concept, if the finding is about the shell rather than the engine.

Please give it a little time before writing publicly. This is a small project without a
bug-bounty budget; fixes are best-effort, credited to you in the changelog unless you would
rather stay anonymous.

## What Nibble never does

No telemetry, no analytics, no crash reporting service, no accounts, no "anonymous usage"
ping. The only network requests are the pages you ask for; the only update path is Windows and
Edge Update keeping the shared engine current. The shield's counters live in
`%AppData%\Nibble\stats.json` and never leave the machine. **Menu → Copy diagnostics** puts
versions, flags and paths on the clipboard — deliberately no history, no bookmarks, no URLs.
