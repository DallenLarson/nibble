**A smaller Nibble, and a clearer description of what it is.**

## Smaller

The exe is **52,787 bytes smaller** — 2,228,347 → **2,175,560 bytes (2.08 MiB)** — so the whole
browser is now under 2.2 MB by any measure. The page mark and wordmark were being stored inside
the assembly twice; they are stored once now. Nothing else about the binary changed.

## Described the way it deserves

The project now says what it is up front: **a free, open-source web browser for Windows in
under 2.2 MB, and not a Chromium build** — a shell over the Chromium engine Windows already
ships, which is why it is this small and why building it takes seconds instead of hours. The
README explains what borrowing the engine buys and what it costs (no Chrome Web Store
extensions, and the engine is Microsoft's to patch, not ours).

## Easier to call your own

Nibble is MIT licensed and built to be forked. New [`docs/rebranding.md`](https://github.com/DallenLarson/nibble/blob/main/docs/rebranding.md)
lists every place the name, artwork and URLs live — and the five that break something if you
miss them: the assembly name, the WPF pack URIs, the installer's AppId, the profile folder, and
the update feed (leave that pointing at this repository and your browser updates itself into
Nibble).

## Install

**[Download `Nibble-1.1.2-Setup.exe`](https://github.com/DallenLarson/nibble/releases/latest)**
— per-user, no administrator prompt. `Nibble.exe` on its own is there too.

Already on 1.1.0 or later? This arrives on its own the next time Nibble starts. On anything
older, one manual download gets you to the version that updates itself.
