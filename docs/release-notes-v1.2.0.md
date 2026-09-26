**Tabs that go where you put them, links that open behind you, sign-in that works, and sound
that keeps playing.**

## Drag a tab out

A tab can be dragged now. Sideways reorders the strip; **letting go below the strip — or
outside the window — turns that tab into its own window**, placed under the pointer. Drop it
on another Nibble window instead and *that* window takes it. A ghost of the tab follows the
pointer and goes paler the moment letting go would make a window rather than a tab. Escape
calls the whole thing off.

Under the hood it is followed through the window's own message hook instead of WPF's mouse
events, because the interesting half of a drag happens over the engine's child window, where
WPF stops hearing about the mouse.

## Signing in with Google works

*"Unable to process request due to missing initial state"* was the window.opener problem, and
it is fixed. Sign-in flows are written against `window.opener` and shared session storage, and
neither survives being turned into a tab. Nibble was already routing window-shaped requests to
a real window, but it built that window before showing it — and the engine only finishes
starting once its window exists, so `window.open` never returned and the flow died in silence.

The window is now shown first and handed to the page the moment the engine is ready. Measured
with a probe page that opens a window and asks it what it can see: `window.open` returned in
**141 ms**, the popup sees its opener, session storage survives, and its message reaches the
opener with a source window attached. **6 of 6 checks.**

## Closing a lot of tabs takes one gesture

- The close mark is bigger: a **14 px glyph on a 26 px button**, up from 12 px on 20 px, with a
  red hover tint. Middle-click still closes.
- **Right-click any tab** for: close tab · **close other tabs** · **close tabs to the right** ·
  duplicate · pin · move to a new window. The bulk actions show how many tabs they would close
  before you click. In the test, one click on *close other tabs* takes three tabs down to one.

## Opening a link in a new tab leaves you where you are

Ctrl-clicking a link, middle-clicking it, or choosing **Open link in new tab** from its menu now
opens that tab *behind you*, and leaves you reading what you were reading. A plain click on the
same link still takes you to the tab it opens — which is the difference the engine never tells a
host about, so the page names the gesture and Nibble acts on the name.

The link menu is part of this. The engine's own menu offers no way to open a link in a tab at
all, so Nibble **puts Open link in new tab at the top of it**, and **Open link in new window**
below it opens a real window.

The probe clicks with a real mouse — Chromium ignores window messages posted at it — and reads
back both the window title and what each page reports about being shown. **15 of 15 checks:**
the tab opened by a ctrl-click, a middle-click or the menu is never shown, the page you were on
never goes hidden, a plain click still brings its tab to the front, and the whole run costs
exactly one tab per click.

## Sound keeps playing when you look away

Chromium only calls a page audible while non-silent samples are on their way to the output
device, and the sleeping-tab timer skipped its own work only on that instant. A tab with sound
coming out of it is now left alone, and stays exempt for twenty seconds after the last sound —
so a pause, a buffer or a quiet passage cannot get a video put to sleep mid-play.

Measured with the nap timer set to 30 seconds: a backgrounded tab playing a 220 Hz tone
advanced its audio **49.0 s of a possible 49.0 s**, never stopped reporting, and was still
reporting at the end of a 50-second stay in the background.

## Two bugs the drag test found on the way

- **A second window no longer opens with the whole session in it.** Ctrl+N, a link that asked
  for its own window, and a tab dragged out each used to restore last session's tabs and *then*
  add the page they were opened for — a dragged-out tab arrived with every old tab behind it.
  Only the first window of a launch restores the session now.
- **Closing two windows one at a time used to forget one of them.** The session is written by
  the last window to close, so the tabs of a window closed a moment earlier were already gone.
  Each window now hands its tabs over on the way out; the saved session ends up with everything
  whichever order the windows are closed in.

## Install

**[Download `Nibble-1.2.0-Setup.exe`](https://github.com/DallenLarson/nibble/releases/latest)**
— per-user, no administrator prompt. `Nibble.exe` on its own is there too.

Already on 1.1.0 or later? This arrives on its own the next time Nibble starts. On anything
older, one manual download gets you to the version that updates itself.
