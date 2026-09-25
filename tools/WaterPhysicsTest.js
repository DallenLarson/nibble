// Runs the shipped water simulation (Assets/Themes/water/theme.js) in a stub browser and
// checks the physics: a flat surface stays flat, an impulse makes a ripple, the ripple
// spreads and then settles, and nothing ever goes to NaN or blows up.
// Run: node tools/WaterPhysicsTest.js

const fs = require("fs");
const path = require("path");

let failures = 0;
function check(label, ok, detail) {
  if (!ok) failures++;
  console.log(`${ok ? "PASS" : "FAIL"}  ${label}${detail !== undefined ? "  -> " + detail : ""}`);
}

const source = fs.readFileSync(
  path.join(__dirname, "..", "src", "Nibble", "Assets", "Themes", "water", "theme.js"), "utf8");

// A browser, just enough of one: the file only touches these at load time.
const sandboxWindow = {};
const sandbox = {
  window: sandboxWindow,
  document: { createElement: () => ({ style: {}, getContext: () => ({}), appendChild() {}, remove() {} }), body: {} },
  performance: { now: () => 0 },
  requestAnimationFrame: () => 0,
  cancelAnimationFrame: () => {},
  addEventListener: () => {},
  removeEventListener: () => {}
};
new Function("window", "document", "performance", "requestAnimationFrame", "cancelAnimationFrame",
  "addEventListener", "removeEventListener", source)(
  sandbox.window, sandbox.document, sandbox.performance, sandbox.requestAnimationFrame,
  sandbox.cancelAnimationFrame, sandbox.addEventListener, sandbox.removeEventListener);

const water = sandboxWindow.NibbleWater;
check("the simulation exposes its physics", !!water && typeof water.step === "function" && typeof water.impulse === "function");
if (!water) { console.log("\nwater physics: COULD NOT LOAD"); process.exit(1); }

const N = water.COLUMNS;
const layer = () => ({
  heights: new Float32Array(N),
  velocities: new Float32Array(N),
  next: new Float32Array(N)
});

function run(l, steps) { for (let i = 0; i < steps; i++) water.step(l); }
function peak(l) { let m = 0; for (const h of l.heights) m = Math.max(m, Math.abs(h)); return m; }
function finite(l) { for (const h of l.heights) if (!Number.isFinite(h)) return false; return true; }
function spreadWidth(l) {
  let first = -1, last = -1;
  for (let i = 0; i < N; i++) if (Math.abs(l.heights[i]) > 0.01) { if (first < 0) first = i; last = i; }
  return first < 0 ? 0 : last - first + 1;
}

// 1. flat water stays flat
const still = layer();
run(still, 600);
check("a flat surface stays flat", peak(still) < 0.5, `peak=${peak(still).toFixed(3)}`);
check("a flat surface stays finite", finite(still));

// 2. an impulse makes a ripple that spreads outward
const ripple = layer();
water.impulse(ripple, N / 2, 1.0);
run(ripple, 10);
const earlyWidth = spreadWidth(ripple);
check("an impulse lifts the surface", peak(ripple) > 0.05, `peak=${peak(ripple).toFixed(3)}`);
check("the ripple is local at first", earlyWidth > 0 && earlyWidth < N / 2, `width=${earlyWidth}`);
run(ripple, 90);
const lateWidth = spreadWidth(ripple);
check("the ripple spreads outward", lateWidth > earlyWidth, `${earlyWidth} -> ${lateWidth}`);

// 3. it settles rather than ringing forever, and never explodes
// A splash must level out again: the impulse is volume-neutral, so there is no dent left
// behind. (It used to leave a permanent one - measured peak 1.07 after a thousand steps,
// never falling below 0.7.)
run(ripple, 390);
check("the ripple settles again", peak(ripple) < 0.05, `peak=${peak(ripple).toFixed(4)}`);
check("nothing blows up", finite(ripple) && peak(ripple) < 10, `peak=${peak(ripple).toFixed(4)}`);

// 4. repeated stirring (a mouse dragged across the page) stays stable
const stirred = layer();
for (let i = 0; i < 400; i++) {
  if (i % 4 === 0) water.impulse(stirred, (i * 7) % N, 0.12);
  water.step(stirred);
}
check("constant stirring stays bounded", finite(stirred) && peak(stirred) < 10, `peak=${peak(stirred).toFixed(3)}`);

// 5. a big splash (a click on the water) still behaves
const splash = layer();
water.impulse(splash, N / 3, 1.5);
run(splash, 1200);
check("a splash calms down", finite(splash) && peak(splash) < 0.01, `peak=${peak(splash).toFixed(4)}`);

// 6. volume: whatever goes down comes back up somewhere else
const volume = layer();
let sum = 0;
for (let i = 0; i < N; i++) sum += volume.velocities[i];
water.impulse(volume, N / 2, 1.0);
let after = 0;
for (let i = 0; i < N; i++) after += volume.velocities[i];
check("a splash adds no net volume", Math.abs(after) < 1e-6, `net=${after.toExponential(2)}`);

console.log(failures === 0 ? "\nALL WATER PHYSICS TESTS PASSED" : `\n${failures} WATER PHYSICS TEST(S) FAILED`);
process.exit(failures === 0 ? 0 : 1);
