# Nibble 2.0.0

This update fixes unstable hover animation, new-tab typing, search initialization, and windows
extending behind the taskbar when maximized.

## Changes

- Buttons keep a transparent, stationary input surface. Only their non-interactive visual layer
  squashes or stretches. Press release runs only for an actual press, not every mouse leave.
- Tab entrance and selection motion applies to the icon. The tab body and close target stay put.
- Home-page search fields, suggestion rows, and shortcut links keep fixed input rectangles.
- New tabs give the URL bar focus immediately, without waiting for WebView2 or the page's ready
  message. Delayed focus recovery preserves an existing query and respects page clicks.
- Engine initialization now awaits all configuration before navigating. The most recent query
  submitted before readiness is retained and navigated once initialization completes.
- Suggestion popups animate when opened, rather than restarting their entrance on every letter.
- Normal maximization uses the current monitor's work area in physical pixels and retains the
  minimum window size. Both the maximize button and Windows snap use this native sizing path.
  F11 and video full-screen continue to use the entire monitor.

## Validation

- Release build and repository verification suites.
- Regression checks exercise all six shipping button templates at compressed, resting and
  expanded scales, checking all four corners, the centre, and points outside the input bounds.
- Query escaping for all four search engines and queries submitted before engine readiness.
- Desktop smoke checks with an isolated profile: Ctrl+T focus, address-bar search, home-page
  search loading Google results, and maximization with the bottom of the page visible.

Multi-monitor arrangements, mixed DPI, and taskbars on other edges still need manual desktop
coverage.
