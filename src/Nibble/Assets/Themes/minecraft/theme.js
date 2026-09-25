/* Nibble · "Grass Block" — the moving parts.

   Adds a few floating blocks to the sky and gives a shortcut a small "block placed"
   thump when you open it. Everything here is generated art (see work/tools/Textures),
   so no Mojang assets travel with the theme. */

(() => {
  "use strict";

  const state = { blocks: [], timers: [], raf: 0 };

  // Paths are relative to the *page*, not to this script, so the theme folder is spelled out.
  const HERE = "themes/minecraft/";
  const BLOCK_KINDS = [
    { file: HERE + "blocks/grass_top.png", size: 44 },
    { file: HERE + "blocks/stone.png", size: 38 },
    { file: HERE + "blocks/cobble.png", size: 34 },
    { file: HERE + "blocks/grass_side.png", size: 40 },
    { file: HERE + "blocks/planks.png", size: 30 },
    { file: HERE + "blocks/stone_bricks.png", size: 32 }
  ];

  // Scattered in the upper half, avoiding the middle where the clock and search sit.
  const SPOTS = [
    [0.08, 0.18], [0.17, 0.42], [0.29, 0.12], [0.72, 0.14], [0.83, 0.34], [0.92, 0.2]
  ];

  function mount(api) {
    const host = document.createElement("div");
    host.id = "mcBlocks";
    host.setAttribute("aria-hidden", "true");
    Object.assign(host.style, { position: "fixed", inset: "0", pointerEvents: "none", zIndex: "1" });
    document.body.appendChild(host);
    state.blocks.push(host);

    SPOTS.forEach((spot, i) => {
      const kind = BLOCK_KINDS[i % BLOCK_KINDS.length];
      const el = document.createElement("i");
      el.className = "mc-block";
      const size = kind.size;
      Object.assign(el.style, {
        width: size + "px",
        height: size + "px",
        left: (spot[0] * 100).toFixed(2) + "%",
        top: (spot[1] * 100).toFixed(2) + "%",
        backgroundImage: "url('" + kind.file + "')",
        animationDelay: (-i * 0.9).toFixed(2) + "s",
        animationDuration: (5 + i * 0.6).toFixed(2) + "s"
      });
      host.appendChild(el);
      state.blocks.push(el);
    });

    // A thump when a shortcut is opened, like a block landing.
    api.tiles.addEventListener("click", thump, true);
    state.blocks.push({ removeEventListener: () => api.tiles.removeEventListener("click", thump, true) });
  }

  function thump(event) {
    const tile = event.target.closest && event.target.closest(".tile");
    if (!tile) return;
    tile.animate(
      [
        { transform: "translateY(0) scale(1, 1)" },
        { transform: "translateY(3px) scale(1.04, .92)", offset: 0.35 },
        { transform: "translateY(-2px) scale(.98, 1.03)", offset: 0.7 },
        { transform: "translateY(0) scale(1, 1)" }
      ],
      { duration: 260, easing: "cubic-bezier(.22,1.35,.28,1)" }
    );
  }

  function unmount() {
    for (const item of state.blocks) {
      try {
        if (item.removeEventListener) item.removeEventListener();
        else if (item.remove) item.remove();
      } catch (e) { /* already gone */ }
    }
    state.blocks.length = 0;
    for (const t of state.timers) clearTimeout(t);
    state.timers.length = 0;
    cancelAnimationFrame(state.raf);
  }

  window.NibblePageTheme = { mount, unmount };
  if (window.__nibbleThemeApi) mount(window.__nibbleThemeApi);
})();
