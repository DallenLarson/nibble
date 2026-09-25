// Mirrors the three snippets Nibble runs inside a page, against a fake document, so the
// counting and jump logic is proven before it ever meets a real browser.
// Run: node tools/FindLogicTest.js

let failures = 0;

function check(label, actual, expected) {
  const ok = actual === expected;
  if (!ok) failures++;
  console.log(`${ok ? "PASS" : "FAIL"}  ${label}  -> ${JSON.stringify(actual)}${ok ? "" : " (expected " + JSON.stringify(expected) + ")"}`);
}

function environment(text, findAnswer = true) {
  const seen = [];
  const selection = { cleared: false, removeAllRanges() { this.cleared = true; } };
  const document = { body: { innerText: text } };
  const window = {
    find: (...args) => { seen.push(args); return findAnswer; },
    getSelection: () => selection,
  };
  return { document, window, seen, selection };
}

function run(env, snippet) {
  return Function("document", "window", `return ${snippet}`)(env.document, env.window);
}

// ---- the exact pieces MainWindow builds ----
const jsonTerm = (term) => JSON.stringify(term);

const countScript = (term) =>
  "(() => {" +
  `const term = ${jsonTerm(term)};` +
  "const body = document.body ? document.body.innerText : '';" +
  "if (!term || !body) return '0';" +
  "const hay = body.toLowerCase(), needle = term.toLowerCase();" +
  "let count = 0, at = 0;" +
  "while ((at = hay.indexOf(needle, at)) !== -1) { count++; at += needle.length; }" +
  "return String(count);" +
  "})();";

const jumpScript = (term, backwards) =>
  "(() => { try {" +
  `return window.find(${jsonTerm(term)}, false, ${backwards ? "true" : "false"}, true, false, true, false) ? '1' : '0';` +
  "} catch (e) { return '0'; } })();";

const clearScript =
  "(() => { const s = window.getSelection(); if (s) s.removeAllRanges(); return '1'; })();";

// ---- counting ----
const page = "GOOD EVENING\nNIBBLE\nTIP - CTRL+K FOR THE COMMAND PALETTE\nthe nibble has nibbles";
check("counts a word three times, case-insensitively", run(environment(page), countScript("nibble")), "3");
check("counts an exact-case match", run(environment(page), countScript("NIBBLE")), "3");
check("counts a phrase", run(environment(page), countScript("GOOD EVENING")), "1");
check("zero when the term is absent", run(environment(page), countScript("zebra")), "0");
check("zero when the term is empty", run(environment(page), countScript("")), "0");
check("zero when there is no body text", run(environment(""), countScript("nibble")), "0");
check("a quote in the term does not break the script", run(environment('he said "nibble" twice: nibble'), countScript('"nibble"')), "1");
check("a backslash in the term does not break the script", run(environment("C:\\nibble\\nibble"), countScript("\\nibble")), "2");

// ---- jumping ----
const forward = environment(page);
check("forward jump reports a hit", run(forward, jumpScript("nibble", false)), "1");
check("forward jump asks for wrap, not backwards",
  JSON.stringify(forward.seen[0]), JSON.stringify(["nibble", false, false, true, false, true, false]));

const backward = environment(page);
check("backward jump reports a hit", run(backward, jumpScript("nibble", true)), "1");
check("backward jump asks the engine to go backwards",
  JSON.stringify(backward.seen[0]), JSON.stringify(["nibble", false, true, true, false, true, false]));

check("a failed jump reports no hit", run(environment(page, false), jumpScript("nibble", false)), "0");

const broken = environment(page);
broken.window.find = () => { throw new Error("no find here"); };
check("a runtime without find degrades to no hit", run(broken, jumpScript("nibble", false)), "0");

// ---- clearing the highlight ----
const cleared = environment(page);
check("clearing runs", run(cleared, clearScript), "1");
check("clearing drops the selection", cleared.selection.cleared, true);

console.log(failures === 0 ? "\nALL FIND LOGIC TESTS PASSED" : `\n${failures} FIND LOGIC TEST(S) FAILED`);
process.exit(failures === 0 ? 0 : 1);
