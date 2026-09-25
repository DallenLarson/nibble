/* Nibble · "Deep Water" — the water itself.

   A one-dimensional height field: each column is a spring pulling towards the average
   of its neighbours, with damping, integrated every frame. Droplets add impulses where
   they land and the pointer stirs it, so the surface behaves like water rather than
   replaying an animation. Two of these are stacked (a slower, shallower back layer and
   a busy front one) for depth. */

(() => {
  "use strict";

  const TENSION = 0.0205;   // how hard a column pulls back towards its neighbours
  const DAMPING = 0.978;    // energy lost per step, so ripples fade in a couple of seconds
  const SPREAD = 0.185;     // how much of the pull reaches the neighbours
  const COLUMNS = 128;

  const state = { layers: [], raf: 0, listeners: [], drops: [], last: 0, breeze: 0 };

  /** One height field plus the canvas it draws itself on. */
  function makeLayer({ height, colour, depth, amplify, baseline }) {
    const heights = new Float32Array(COLUMNS);
    const velocities = new Float32Array(COLUMNS);
    const next = new Float32Array(COLUMNS);
    return { heights, velocities, next, height, colour, depth, amplify, baseline, canvas: null, ctx: null };
  }

  function step(layer) {
    const { heights, velocities, next } = layer;
    for (let i = 0; i < COLUMNS; i++) {
      const left = heights[i === 0 ? 0 : i - 1];
      const right = heights[i === COLUMNS - 1 ? COLUMNS - 1 : i + 1];
      // spring towards the mean of the neighbours, then integrate with damping
      velocities[i] += ((left + right) / 2 - heights[i]) * TENSION;
      velocities[i] *= DAMPING;
      next[i] = heights[i] + velocities[i];
      next[i] += ((left + right) / 2 - next[i]) * SPREAD * 0.1;
    }
    for (let i = 0; i < COLUMNS; i++) {
      heights[i] = next[i];
      velocities[i] *= 0.999;
    }
  }

  /**
   * A splash: the column under the impact goes down while its neighbours come up by the
   * same total, so the impulse carries no net volume. Without that the surface can never
   * level out again - the displaced water has nowhere to go, and the page keeps a
   * permanent dent.
   */
  function impulse(layer, at, strength) {
    const i = Math.max(1, Math.min(COLUMNS - 2, Math.round(at)));
    layer.velocities[i] -= strength;
    layer.velocities[i - 1] += strength * 0.5;
    layer.velocities[i + 1] += strength * 0.5;
  }

  function resize(layer) {
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const width = layer.canvas.clientWidth || window.innerWidth;
    const heightPx = layer.canvas.clientHeight || window.innerHeight;
    layer.canvas.width = Math.round(width * dpr);
    layer.canvas.height = Math.round(heightPx * dpr);
    layer.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    layer.width = width;
    layer.heightPx = heightPx;
  }

  function draw(layer) {
    const { ctx, heights, width, heightPx } = layer;
    const base = heightPx * layer.baseline;
    ctx.clearRect(0, 0, width, heightPx);

    ctx.beginPath();
    ctx.moveTo(0, heightPx);
    for (let i = 0; i <= COLUMNS; i++) {
      const x = (i / COLUMNS) * width;
      const y = base + heights[Math.min(i, COLUMNS - 1)] * layer.amplify;
      if (i === 0) ctx.lineTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.lineTo(width, heightPx);
    ctx.closePath();

    const gradient = ctx.createLinearGradient(0, base - 40, 0, heightPx);
    gradient.addColorStop(0, layer.colour[0]);
    gradient.addColorStop(1, layer.colour[1]);
    ctx.fillStyle = gradient;
    ctx.fill();

    // the bright crest line
    ctx.beginPath();
    for (let i = 0; i <= COLUMNS; i++) {
      const x = (i / COLUMNS) * width;
      const y = base + heights[Math.min(i, COLUMNS - 1)] * layer.amplify;
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.strokeStyle = layer.crest;
    ctx.lineWidth = 2;
    ctx.stroke();
  }

  function frame(layer) {
    step(layer);
    draw(layer);
  }

  function loop(now) {
    state.raf = requestAnimationFrame(loop);
    for (const layer of state.layers) frame(layer);

    // occasional droplets landing in the front layer
    if (state.layers.length && now - state.last > 900 + Math.random() * 1600) {
      state.last = now;
      const front = state.layers[state.layers.length - 1];
      state.drops.push({ column: Math.random() * COLUMNS, y: -20, layer: front, speed: 5 + Math.random() * 4 });
    }

    // and a slow swell, so a page nobody is touching still has life in the water
    if (state.layers.length && now - state.breeze > 2600 + Math.random() * 2200) {
      state.breeze = now;
      impulse(state.layers[state.layers.length - 1], Math.random() * COLUMNS, 0.3 + Math.random() * 0.25);
    }
    for (const drop of state.drops) {
      drop.y += drop.speed;
      const surface = drop.layer.baseline * drop.layer.heightPx;
      if (drop.y >= surface - 8) {
        impulse(drop.layer, drop.column, 0.55 + Math.random() * 0.35);
        drop.dead = true;
      }
    }
    state.drops = state.drops.filter((d) => !d.dead);
  }

  function onPointer(event) {
    const front = state.layers[state.layers.length - 1];
    const back = state.layers[0];
    if (!front) return;
    const column = (event.clientX / (front.width || window.innerWidth)) * COLUMNS;
    impulse(front, column, 0.12);
    if (back && back !== front) impulse(back, column, 0.06);
  }

  function onDown(event) {
    const front = state.layers[state.layers.length - 1];
    if (!front) return;
    impulse(front, (event.clientX / (front.width || window.innerWidth)) * COLUMNS, 1.5);
  }

  function onResize() {
    for (const layer of state.layers) resize(layer);
  }

  function mount() {
    const back = makeLayer({
      height: 58,
      colour: ["rgba(12,74,110,.75)", "rgba(6,42,68,.9)"],
      amplify: 10,
      baseline: 0.34
    });
    back.crest = "rgba(125,211,252,.35)";

    const front = makeLayer({
      height: 58,
      colour: ["rgba(20,116,168,.9)", "rgba(3,26,44,.96)"],
      amplify: 16,
      baseline: 0.58
    });
    front.crest = "rgba(186,230,253,.75)";

    state.layers = [back, front];
    for (const layer of state.layers) {
      const canvas = document.createElement("canvas");
      canvas.className = "water-canvas";
      canvas.style.height = layer.height + "vh";
      canvas.style.zIndex = "0";
      document.body.appendChild(canvas);
      layer.canvas = canvas;
      layer.ctx = canvas.getContext("2d");
      resize(layer);
      // start with a couple of ripples so it is alive from the first frame
      impulse(layer, COLUMNS * 0.3, 0.8);
      impulse(layer, COLUMNS * 0.72, 0.6);
    }

    addEventListener("resize", onResize);
    addEventListener("pointermove", onPointer, { passive: true });
    addEventListener("pointerdown", onDown, { passive: true });
    state.listeners.push(
      ["resize", onResize], ["pointermove", onPointer], ["pointerdown", onDown]
    );

    cancelAnimationFrame(state.raf);
    state.raf = requestAnimationFrame(loop);
  }

  function unmount() {
    cancelAnimationFrame(state.raf);
    state.raf = 0;
    for (const [type, handler] of state.listeners) removeEventListener(type, handler);
    state.listeners.length = 0;
    for (const layer of state.layers) layer.canvas?.remove();
    state.layers.length = 0;
    state.drops.length = 0;
  }

  /** Exposed so the simulation can be tested without a browser. */
  window.NibbleWater = { step, impulse, COLUMNS, TENSION, DAMPING, SPREAD };
  window.NibblePageTheme = { mount, unmount };
  if (window.__nibbleThemeApi) mount();
})();
