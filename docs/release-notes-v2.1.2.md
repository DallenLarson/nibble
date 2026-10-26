# Nibble 2.1.2

Mute noisy tabs directly from the tab strip.

## Install or update

Download **Nibble-2.1.2-Setup.exe** below and run it to update your existing installation.
Your browsing profile is kept. For portable use, download **Nibble.exe**.

## What's new

- Right-click a tab and choose **Mute tab**. Right-click again for **Unmute tab**.
- Mute a background tab without switching away from the page you're using.
- Muted tabs show a crossed-out speaker, including when the page is currently silent.
- Muting continues when you navigate within that tab. The existing command-palette mute
  action stays synchronized with the tab indicator.

## Validation

Browser integration checks exercise the tab-menu handler and real WebView2 mute state:
muting a background tab without changing the active tab, preserving mute across navigation,
unmuting through the menu, and updating indicator state through the existing mute command.
The existing browser regression suite also passed.

Run `./tools/verify.ps1 -BrowserIntegration` on Windows with WebView2 installed.
