**Ctrl+T and then typing is one motion: the new tab hands the address bar the caret.**

**Ctrl+F and Ctrl+K take the keyboard on a real website, so finding text and running a
command work on the pages you actually visit.**

## The caret is already where you were going to type

A new tab used to open with the keyboard on the page, so the first letter you typed went
nowhere until you reached up and clicked the address bar. Now it opens with the caret in the
address bar, selected and ready — type straight away and the search is already under way.

The list of recent sites stays closed until the first keystroke, so nothing drops over the home
page the tab opened on. One character in, and it is there as usual.

Two things make this worth doing carefully rather than just calling `Focus()`:

- **It waits for the page to paint.** The engine takes the keyboard for its own window while a
  page loads, so the hand-off hangs off the page's own *ready* message — the last thing that
  happens after that.
- **It only happens for a tab you opened.** A restored session, a tab opened behind you, a
  full-screen window with no address bar on screen, and a page you have clicked into all keep
  the keyboard exactly where you left it. Closing the last tab and opening a window fresh both
  count as opening a tab; being handed a link from another app does not.

Measured with real key presses and real button messages, reading back the UI Automation focused
element and the address bar's own value: **16 of 16 checks** in `tools/NewTabFocusProbe.ps1`.
Two of those checks check the check itself — typing does open the recent-sites list, and
clicking into the page does keep the caret there — so a pass means a keystroke really landed,
not that a flag was set.

## Find and the command bar now take the keyboard on a real page

On any website you had clicked into, Ctrl+F and Ctrl+K opened and then did nothing. Every letter
typed afterwards went into the page instead: the find bar sat there empty reporting nothing, and
the command bar sat behind an empty query. Ctrl+L worked the whole time, and that was the clue.

The engine's page surface is a child window of the shell, and while it holds the keyboard WPF
cannot move focus into a popup at all — the request is refused without an error and `Focus()`
still returns true, so nothing looked wrong from the inside. Nibble now parks the keyboard on a
small element inside the shell and hands it to the popup from there, which is what actually
gives the page's keyboard up. The same route covers the command bar, which had the same hole
for exactly the same reason.

Measured with real keystrokes and the UI Automation focused element against a page served over
http: **19 of 19 checks** in `tools/FindProbe.ps1`, which types a word, walks the matches forward
and back, checks a word that is not there, and checks that Esc hands the keyboard back to the
page. On the build immediately before this change, **8 of those 19 fail**.

## Install

**[Download `Nibble-1.2.1-Setup.exe`](https://github.com/DallenLarson/nibble/releases/latest)**
— per-user, no administrator prompt. `Nibble.exe` on its own is there too.

Already on 1.1.0 or later? This arrives on its own the next time Nibble starts. On anything
older, one manual download gets you to the version that updates itself.
