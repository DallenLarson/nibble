**1.0.2 fixes the flickering pointer** — hovering a toolbar button made the cursor alternate
between the hand and the arrow, which reads as the mouse glitching.

## What was wrong

Windows asks a window what the cursor should look like (`WM_SETCURSOR`) *before* it delivers
the mouse move that says where the pointer now is. WPF answers from its own cached position,
so for that one message it was answering about the pixel the pointer had just left: over a
button it said arrow, then hand, then arrow again.

Measured on the shipping build with the pointer parked on one button: `real=100,67
cached=76,67 hit=PixelPanel cursor=ARROW`, and the cursor sampled at 10 ms read
`hand arrow hand arrow …`. A link inside the page — whose cursor the Chromium engine draws
itself — never flickered, which is what pointed at the shell rather than at the pointer.

## What changed

- **The shell answers the cursor question itself**, from the pointer's real position, and does
  not let WPF's stale answer be applied on top. Resize edges and anything unusual are still
  WPF's business.
- **Text fields show the I-beam.** The address bar, find bar, command palette and the wizard's
  name field all showed a plain arrow, because the style that replaces a `TextBox`'s template
  takes the I-beam with it.
- `tools/CursorProbe.ps1` is the regression test, and `tools/` is otherwise unchanged.

Nothing about pages, tabs, themes or the installer changed. Your profile is untouched.

## Install

**[Download `Nibble-1.0.2-Setup.exe`](https://github.com/DallenLarson/nibble/releases/latest)**
— per-user, no administrator prompt. `Nibble.exe` on its own is there too.

Needs Windows 10/11 64-bit, the Microsoft Edge WebView2 Runtime (ships with Windows) and the
.NET 8 Desktop Runtime. **Still unsigned** — on a machine with Smart App Control switched on,
Windows refuses to run unsigned binaries and offers no override. A signing certificate is
still the next thing on the list.
