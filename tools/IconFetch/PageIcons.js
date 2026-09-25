// Prints the Lucide path table used by the built-in pages (Assets/newtab.html,
// Assets/error.html) so they render the same smooth icons as the shell.
const wanted = {
  search: "search",
  globe: "globe",
  play: "play",
  code: "code",
  bolt: "zap",
  chat: "message-circle",
  music: "music",
  pin: "map-pin",
  star: "star",
  sparkle: "sparkles",
  retry: "refresh-cw",
  back: "arrow-left",
  external: "external-link",
  copy: "copy",
  offline: "cloud-off"
};

const num = (attrs, key, fallback = 0) => (attrs[key] !== undefined ? Number(attrs[key]) : fallback);

function attrsOf(tag) {
  const out = {};
  for (const [, k, v] of tag.matchAll(/([a-zA-Z-]+)="([^"]*)"/g)) out[k] = v;
  return out;
}

function toPath(element) {
  const name = element.match(/^<([a-zA-Z]+)/)[1].toLowerCase();
  const a = attrsOf(element);
  if (name === "path") return a.d;
  if (name === "line") return `M${a.x1} ${a.y1}L${a.x2} ${a.y2}`;
  if (name === "polyline" || name === "polygon") {
    const pts = a.points.trim().split(/[\s,]+/);
    let d = `M${pts[0]} ${pts[1]}`;
    for (let i = 2; i < pts.length; i += 2) d += `L${pts[i]} ${pts[i + 1]}`;
    return name === "polygon" ? `${d}Z` : d;
  }
  if (name === "circle" || name === "ellipse") {
    const cx = num(a, "cx"), cy = num(a, "cy");
    const rx = name === "circle" ? num(a, "r") : num(a, "rx");
    const ry = name === "circle" ? num(a, "r") : num(a, "ry");
    return `M${cx - rx} ${cy}a${rx} ${ry} 0 1 0 ${rx * 2} 0a${rx} ${ry} 0 1 0 ${-rx * 2} 0`;
  }
  if (name === "rect") {
    const x = num(a, "x"), y = num(a, "y"), w = num(a, "width"), h = num(a, "height");
    const r = num(a, "rx", num(a, "ry", 0));
    return r > 0
      ? `M${x + r} ${y}h${w - r * 2}a${r} ${r} 0 0 1 ${r} ${r}v${h - r * 2}a${r} ${r} 0 0 1 ${-r} ${r}h${-(w - r * 2)}a${r} ${r} 0 0 1 ${-r} ${-r}v${-(h - r * 2)}a${r} ${r} 0 0 1 ${r} ${-r}z`
      : `M${x} ${y}h${w}v${h}h${-w}z`;
  }
  return "";
}

const entries = [];
for (const [key, slug] of Object.entries(wanted)) {
  const response = await fetch(`https://raw.githubusercontent.com/lucide-icons/lucide/main/icons/${slug}.svg`);
  if (!response.ok) throw new Error(`${response.status} for ${slug}`);
  const svg = await response.text();
  const body = svg.slice(svg.indexOf(">", svg.indexOf("<svg")) + 1, svg.lastIndexOf("</svg>"));
  const parts = [...body.matchAll(/<([a-zA-Z]+)([^>]*)\/?>/g)].map((m) => toPath(m[0])).filter(Boolean);
  const d = parts
    .map((part, i) => (i === 0 ? part : part.replace(/^m\s*([-\d.]+)[\s,]+([-\d.]+)\s*/, "M$1 $2l")))
    .join(" ");
  entries.push(`    ${key}: "${d.replace(/\s+/g, " ")}"`);
}

process.stdout.write(`  const ICONS = {\n${entries.join(",\n")}\n  };\n`);
