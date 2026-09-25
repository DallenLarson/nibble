// Pulls the real setPrivate() out of newtab.html and runs it against stubs, so the
// private-window page behaviour is checked even when the app itself cannot be launched.
// Run: node tools/PrivatePageTest.js

const fs = require("fs");
const path = require("path");

let failures = 0;
function check(label, actual, expected) {
  const ok = JSON.stringify(actual) === JSON.stringify(expected);
  if (!ok) failures++;
  console.log(`${ok ? "PASS" : "FAIL"}  ${label}  -> ${JSON.stringify(actual)}${ok ? "" : " (expected " + JSON.stringify(expected) + ")"}`);
}

const html = fs.readFileSync(path.join(__dirname, "..", "src", "Nibble", "Assets", "newtab.html"), "utf8");
const marker = "function setPrivate(on) {";
const start = html.indexOf(marker);
if (start < 0) { console.error("setPrivate() not found in newtab.html"); process.exit(1); }

let depth = 0, end = start;
for (let i = html.indexOf("{", start); i < html.length; i++) {
  if (html[i] === "{") depth++;
  else if (html[i] === "}") { depth--; if (depth === 0) { end = i + 1; break; } }
}
const source = html.slice(start, end);
console.log(`extracted ${source.length} chars of setPrivate()\n`);

function makeEnvironment() {
  const classes = new Set();
  const tip = { textContent: "" };
  const chip = { hidden: true };
  const rendered = [];
  const TIPS = ["TIP ONE", "TIP TWO"];
  const document = {
    body: { classList: { toggle: (name, on) => (on ? classes.add(name) : classes.delete(name)) } },
    getElementById: () => null,
  };
  const state = { classes, tip, chip, TIPS, document, rendered };
  state.privateChip = chip;
  state.tip = tip;
  state.tipIndex = 5;
  return state;
}

function run(source, env, on) {
  const fn = new Function(
    "document", "privateChip", "tip", "TIPS", "tipIndex", "privateMode", "renderTiles",
    `${source}\nsetPrivate(${on});\nreturn { tipIndex, privateMode, tips: TIPS.slice(), tipNow: tip.textContent };`);
  return fn(env.document, env.chip, env.tip, env.TIPS, env.tipIndex, false,
    (items, withDefaults) => env.rendered.push([items, withDefaults]));
}

// ---- private window ----
const priv = makeEnvironment();
const afterOn = run(source, priv, true);
check("private: body gets the .private class", priv.classes.has("private"), true);
check("private: chip is shown", priv.chip.hidden, false);
check("private: tiles are re-rendered with nothing of yours and no filler",
  JSON.stringify(priv.rendered), "[[[],false]]");
check("private: the tip list is replaced with the honest one", afterOn.tips.length, 1);
check("private: the tip says nothing is written to disk", /NOTHING IS WRITTEN TO DISK/.test(afterOn.tipNow), true);
check("private: the tip starts over", afterOn.tipIndex, 0);
check("private: privateMode is set", afterOn.privateMode, true);

// ---- normal window ----
const normal = makeEnvironment();
const afterOff = run(source, normal, false);
check("normal: no .private class", normal.classes.has("private"), false);
check("normal: chip stays hidden", normal.chip.hidden, true);
check("normal: tiles are left alone", normal.rendered.length, 0);
check("normal: tip list untouched", afterOff.tips.length, 2);
check("normal: privateMode cleared", afterOff.privateMode, false);

console.log(failures === 0 ? "\nALL PRIVATE PAGE TESTS PASSED" : `\n${failures} PRIVATE PAGE TEST(S) FAILED`);
process.exit(failures === 0 ? 0 : 1);
