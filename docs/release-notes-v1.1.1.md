**1.1.1 fixes full screen.** The bottom of the page was cut off: the window was the size of the
monitor, but the Windows taskbar is always-on-top, so the last 60 px of the screen belonged to
Windows instead of the page.

## What changed

Full screen is now genuinely above everything — the window goes topmost, and the shell is told
it is full-screen so Explorer takes the taskbar away rather than leaving it behind the page.
Measured on a 1920×1080 screen: before, the pixels at the bottom of the screen belonged to
`MSTaskSwWClass` (the taskbar); after, Nibble sits in front of the taskbar in the z-order
(index 6 against 206).

Leaving full screen (F11 or Escape) puts everything back exactly as it was: not topmost, the
window at the rectangle it had before, and the tabs and toolbar back at their usual heights.

Nothing else changed. If you are on 1.1.0 or 1.1.1 already, this arrives on its own the next
time Nibble starts; on anything older, one manual download gets you to the version that
updates itself.

## Install

**[Download `Nibble-1.1.1-Setup.exe`](https://github.com/DallenLarson/nibble/releases/latest)**
— per-user, no administrator prompt. `Nibble.exe` on its own is there too.

Needs Windows 10/11 64-bit, the Microsoft Edge WebView2 Runtime and the .NET 8 Desktop Runtime.
**Still unsigned** — on a machine with Smart App Control on, Windows refuses to run unsigned
binaries, and that includes an installer this app downloaded for itself.
