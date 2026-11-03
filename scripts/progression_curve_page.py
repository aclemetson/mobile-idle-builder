"""HTML template for scripts/progression_curve.py. Charts are inline SVG drawn by a small script, so the page
needs nothing but itself (opens from disk, publishes as an artifact)."""
import json

CATEGORY_GROUP = {
    "Element": "Elements", "Isotope": "Elements", "Particle": "Particles & nucleons", "Nucleon": "Particles & nucleons",
    "Subatomic": "Particles & nucleons", "Lepton": "Particles & nucleons", "Quark": "Particles & nucleons",
}


def _group(item):
    c = (item or {}).get("category", "")
    if c in CATEGORY_GROUP:
        return CATEGORY_GROUP[c]
    return "Chemistry & biology"


def write_page(path, gd, order, anomalies, status, buildings, jump):
    items = {i["id"]: i for i in gd["items"]}
    rows = []
    for o in order:
        rows.append({
            "n": o["n"], "id": o["id"], "name": o["name"], "block": o.get("block", ""), "cost": o["cost"], "payback": o["payback_min"],
            "rate": o["best_rate_before"], "rateRecipe": o["best_recipe_before"], "top": o["top_value"],
            "items": [{"id": r["out"], "name": items.get(r["out"], {}).get("display_name", r["out"]),
                       "v": r["value"], "g": _group(items.get(r["out"])), "gain": r["gain"], "recipe": r["id"]}
                      for r in o["recipes"] if r["value"] > 0],
        })
    blocks = [{"id": b["id"], "name": b["name"], "target": b["target_hours"], "anchor": bool(b.get("anchor"))}
              for b in sorted(gd.get("research_blocks") or [], key=lambda b: b.get("order", 0))]
    payload = {"rows": rows, "blocks": blocks, "status": status, "jump": jump, "buildings": buildings,
               "anomalies": [{"kind": k, "what": w, "why": y} for k, w, y in anomalies],
               "version": gd.get("_meta", {}).get("version", "")}
    html = TEMPLATE.replace("/*DATA*/null", json.dumps(payload, separators=(",", ":")))
    path.write_text(html, encoding="utf-8", newline="\n")


TEMPLATE = r"""<title>Progression Curve</title>
<meta name="claude:viewport-fit-sides" content="cover">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Chakra+Petch:wght@500;600&family=IBM+Plex+Sans:wght@400;500;600&family=IBM+Plex+Mono:wght@400;500&display=swap">
<style>
/* Layout: one reading column of stacked chart panels, summary first, tables last. */
:root {
  --bg: #f6f7f9; --panel: #ffffff; --ink: #14161a; --ink-2: #4c5361; --ink-3: #7a8190;
  --rule: #dfe3ea; --grid: #eceff3; --accent: #2a78d6;
  --s1: #2a78d6; --s2: #eb6834; --s3: #1baf7a; --s-other: #9aa1ad;
  --good: #1f8a4c; --warn: #b7791f; --crit: #c53030; --band: rgba(31,138,76,.10);
  --f-display: "Chakra Petch", "Segoe UI", system-ui, sans-serif;
  --f-body: "IBM Plex Sans", "Segoe UI", system-ui, sans-serif;
  --f-mono: "IBM Plex Mono", ui-monospace, "Cascadia Mono", Consolas, monospace;
}
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) {
  --bg: #111317; --panel: #1a1d22; --ink: #eceef2; --ink-2: #b4bac6; --ink-3: #868d9a;
  --rule: #2c313a; --grid: #23272e; --accent: #3987e5;
  --s1: #3987e5; --s2: #d95926; --s3: #199e70; --s-other: #6c7380;
  --good: #3fb36f; --warn: #d69e2e; --crit: #f0605d; --band: rgba(63,179,111,.12); color-scheme: dark } }
:root[data-theme="dark"] {
  --bg: #111317; --panel: #1a1d22; --ink: #eceef2; --ink-2: #b4bac6; --ink-3: #868d9a;
  --rule: #2c313a; --grid: #23272e; --accent: #3987e5;
  --s1: #3987e5; --s2: #d95926; --s3: #199e70; --s-other: #6c7380;
  --good: #3fb36f; --warn: #d69e2e; --crit: #f0605d; --band: rgba(63,179,111,.12); color-scheme: dark }
body { background: var(--bg); color: var(--ink); font: 15px/1.55 var(--f-body); }
.wrap { max-width: 980px; margin: 0 auto; padding-block: 28px 64px;
  padding-left: max(16px, env(safe-area-inset-left, 0px)); padding-right: max(16px, env(safe-area-inset-right, 0px));
  display: grid; gap: 22px; }
h1 { font: 600 clamp(26px, 5vw, 36px)/1.1 var(--f-display); margin: 0; letter-spacing: .01em; text-wrap: balance; }
h2 { font: 600 19px/1.25 var(--f-display); margin: 0; text-wrap: balance; }
p { margin: 0; max-width: 68ch; color: var(--ink-2); }
.eyebrow { font: 500 12px var(--f-mono); letter-spacing: .08em; text-transform: uppercase; color: var(--ink-3); }
header { display: grid; gap: 10px; }
.stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 12px; }
.stat { background: var(--panel); border: 1px solid var(--rule); border-radius: 8px; padding: 12px 14px; display: grid; gap: 2px; }
.stat b { font: 600 22px var(--f-display); font-variant-numeric: tabular-nums; }
.stat span { font-size: 13px; color: var(--ink-2); }
.panel { background: var(--panel); border: 1px solid var(--rule); border-radius: 10px; padding: 18px; display: grid; gap: 12px; min-width: 0; }
.legend { display: flex; flex-wrap: wrap; gap: 8px 16px; font-size: 13px; color: var(--ink-2); }
.legend i { display: inline-block; width: 10px; height: 10px; border-radius: 2px; margin-right: 6px; vertical-align: -1px; }
svg { width: 100%; height: auto; display: block; overflow: visible; }
svg text { fill: var(--ink-3); font: 11px var(--f-mono); }
svg text.band-name { fill: var(--ink-2); font: 500 10px var(--f-body); letter-spacing: .02em; }
.ctrl { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; font-size: 14px; color: var(--ink-2); }
.ctrl input { accent-color: var(--accent); }
.ctrl output { font: 500 14px var(--f-mono); color: var(--ink); min-width: 2ch; }
.scroll { overflow-x: auto; min-width: 0; }
table { border-collapse: collapse; width: 100%; font-size: 13px; }
th, td { text-align: left; padding: 6px 8px; border-bottom: 1px solid var(--grid); white-space: nowrap; }
th { font: 500 12px var(--f-mono); color: var(--ink-3); text-transform: uppercase; letter-spacing: .05em; }
td.num { text-align: right; font-family: var(--f-mono); font-variant-numeric: tabular-nums; }
.pill { display: inline-block; font: 500 11px var(--f-mono); padding: 1px 7px; border-radius: 99px; border: 1px solid currentColor; }
.pill.free { color: var(--warn); } .pill.wall { color: var(--crit); } .pill.ok { color: var(--good); }
.pill.loses, .pill.late { color: var(--crit); }
.tip { position: fixed; pointer-events: none; z-index: 20; background: var(--panel); color: var(--ink); border: 1px solid var(--rule);
  border-radius: 6px; padding: 8px 10px; font-size: 13px; box-shadow: 0 6px 18px rgba(0,0,0,.18); max-width: 280px; }
.tip b { font-family: var(--f-display); }
.tip .m { font-family: var(--f-mono); color: var(--ink-2); }
.foot { font-size: 13px; color: var(--ink-3); }
code { font-family: var(--f-mono); font-size: .92em; }
@media (prefers-reduced-motion: reduce) { * { transition: none !important; } }
</style>

<div class="wrap">
  <header>
    <div class="eyebrow">Quantum Foundry · economy</div>
    <h1>Progression Curve</h1>
    <p>Every research node in the order a player unlocks it, with what it costs, what it unlocks, and how fast the best building can pay for it. Generated from <code>Assets/Data/game_data.json</code> by <code>scripts/progression_curve.py</code>; rerun it after any balance change.</p>
  </header>
  <section class="stats" id="stats"></section>

  <section class="panel">
    <div class="eyebrow">Phases</div>
    <h2>Research blocks: target time against the model</h2>
    <p>Each block is a game phase. Its target is how long a player should spend clearing it; the model is the payback of its research for the number of buildings set below. Edit blocks and targets in the research tree editor.</p>
    <div class="scroll"><table id="blocks"><thead><tr><th>Block</th><th style="text-align:right">Research</th><th style="text-align:right">Target</th><th style="text-align:right">Model</th><th style="text-align:right">Reached at</th><th style="text-align:right">Costs</th></tr></thead><tbody></tbody></table></div>
  </section>

  <section class="panel">
    <div class="eyebrow">Pacing</div>
    <h2>Minutes for one building to pay for each research</h2>
    <p>Research cost divided by the best earning rate available just before it. A healthy curve rises smoothly. A node far above the trend of the ones before it is a wall; one far below it is effectively free.</p>
    <div class="legend"><span><i style="background:var(--good)"></i>On trend</span><span><i style="background:var(--crit)"></i>Wall</span><span><i style="background:var(--warn)"></i>Free</span></div>
    <svg id="payback" viewBox="0 0 900 300" role="img" aria-label="Payback minutes per research, log scale"></svg>
  </section>

  <section class="panel">
    <div class="eyebrow">Time</div>
    <h2>Estimated playtime to reach each unlock</h2>
    <div class="ctrl"><label for="nb">Buildings working the best recipe</label>
      <input id="nb" type="range" min="1" max="40" step="1"><output id="nbv"></output></div>
    <svg id="time" viewBox="0 0 900 260" role="img" aria-label="Cumulative hours by unlock"></svg>
  </section>

  <section class="panel">
    <div class="eyebrow">Earning</div>
    <h2>Best value added per building-second</h2>
    <p>The highest (output − inputs) ÷ craft time among every recipe unlocked so far. A healthy game climbs this steadily; one cliff means one recipe outgrows everything after it.</p>
    <svg id="rate" viewBox="0 0 900 260" role="img" aria-label="Best earning rate by unlock, log scale"></svg>
  </section>

  <section class="panel">
    <div class="eyebrow">Values</div>
    <h2>Sell value of every item, by the research that unlocks it</h2>
    <div class="legend" id="vlegend"></div>
    <svg id="values" viewBox="0 0 900 320" role="img" aria-label="Item sell values by unlock order, log scale"></svg>
  </section>

  <section class="panel">
    <div class="eyebrow">Flags</div>
    <h2>What the curve says needs fixing</h2>
    <div class="scroll"><table id="flags"><thead><tr><th>Kind</th><th>What</th><th>Why</th></tr></thead><tbody></tbody></table></div>
  </section>

  <section class="panel">
    <div class="eyebrow">Table</div>
    <h2>Unlock order</h2>
    <div class="scroll"><table id="order"><thead><tr><th>#</th><th>Research</th><th style="text-align:right">Cost</th><th style="text-align:right">Payback</th><th style="text-align:right">Hours</th><th style="text-align:right">Best e/s</th><th style="text-align:right">Top new item</th></tr></thead><tbody></tbody></table></div>
  </section>

  <p class="foot" id="foot"></p>
</div>
<div class="tip" id="tip" hidden></div>

<script>
const D = /*DATA*/null;
const NS = "http://www.w3.org/2000/svg";
const SUF = [[1e18,"Qi"],[1e15,"Q"],[1e12,"T"],[1e9,"B"],[1e6,"M"],[1e3,"k"]];
function fmt(v) {
  if (!isFinite(v)) return "∞";
  const a = Math.abs(v);
  for (const [d, s] of SUF) if (a >= d) return (v / d).toFixed(a / d >= 100 ? 0 : 1).replace(/\.0$/, "") + s;
  if (a >= 10 || a === 0) return Math.round(v).toLocaleString();
  if (a >= 0.01) return v.toFixed(2);
  return v.toExponential(0);
}
function minutes(m) {
  if (!isFinite(m)) return "∞";
  if (m < 1 / 60) return "<1 s";
  if (m < 1) return Math.round(m * 60) + " s";
  if (m < 120) return m.toFixed(m < 10 ? 1 : 0) + " min";
  return (m / 60).toFixed(m < 6000 ? 1 : 0) + " h";
}
function el(tag, attrs, parent) {
  const e = document.createElementNS(NS, tag);
  for (const k in attrs) e.setAttribute(k, attrs[k]);
  if (parent) parent.appendChild(e);
  return e;
}
const tip = document.getElementById("tip");
function showTip(ev, html) {
  tip.innerHTML = html; tip.hidden = false;
  const r = tip.getBoundingClientRect(), x = ev.clientX + 14, y = ev.clientY + 14;
  tip.style.left = Math.min(x, innerWidth - r.width - 8) + "px";
  tip.style.top = Math.min(y, innerHeight - r.height - 8) + "px";
}
function hideTip() { tip.hidden = true; }
document.addEventListener("scroll", hideTip, { passive: true });

// One frame per chart: x = unlock index, y = log10 or linear.
function frame(svg, opts) {
  svg.innerHTML = "";
  const W = 900, H = +svg.getAttribute("viewBox").split(" ")[3];
  const L = 58, R = 12, T = 12, B = 30;
  const n = D.rows.length;
  const x = i => L + (i - 0.5) * (W - L - R) / n;
  let y, ticks;
  if (opts.log) {
    const lo = Math.floor(Math.log10(opts.min)), hi = Math.ceil(Math.log10(opts.max));
    y = v => T + (H - T - B) * (1 - (Math.log10(Math.max(v, opts.min)) - lo) / (hi - lo));
    const step = Math.max(1, Math.ceil((hi - lo) / 6));
    ticks = []; for (let p = lo; p <= hi; p += step) ticks.push(Math.pow(10, p));
  } else {
    y = v => T + (H - T - B) * (1 - v / opts.max);
    ticks = []; const s = niceStep(opts.max / 5); for (let v = 0; v <= opts.max + 1e-9; v += s) ticks.push(v);
  }
  for (const t of ticks) {
    el("line", { x1: L, x2: W - R, y1: y(t), y2: y(t), stroke: "var(--grid)", "stroke-width": 1 }, svg);
    el("text", { x: L - 8, y: y(t) + 4, "text-anchor": "end" }, svg).textContent = opts.fmt ? opts.fmt(t) : fmt(t);
  }
  el("line", { x1: L, x2: W - R, y1: H - B, y2: H - B, stroke: "var(--rule)", "stroke-width": 1 }, svg);
  for (let i = 1; i <= n; i += 5) el("text", { x: x(i), y: H - B + 16, "text-anchor": "middle" }, svg).textContent = i;
  el("text", { x: W - R, y: H - 2, "text-anchor": "end" }, svg).textContent = "unlock #";
  // Research blocks: alternate a faint band behind each block's run of unlocks, with its name on top.
  let start = 1, k = 0;
  for (let i = 1; i <= n + 1; i++) {
    const cur = D.rows[i - 1]?.block, prev = D.rows[i - 2]?.block;
    if (i > 1 && (i === n + 1 || cur !== prev)) {
      const x0 = x(start) - (W - L - R) / n / 2, x1 = x(i - 1) + (W - L - R) / n / 2;
      if (k % 2) el("rect", { x: x0, y: T, width: x1 - x0, height: H - T - B, fill: "var(--grid)", opacity: 0.6 }, svg);
      const b = (D.blocks || []).find(b => b.id === prev);
      if (b && x1 - x0 > 30) el("text", { x: (x0 + x1) / 2, y: T + 11, "text-anchor": "middle", class: "band-name" }, svg).textContent = b.name;
      start = i; k++;
    }
  }
  return { x, y, W, H, L, R, T, B, n };
}
function niceStep(raw) { const p = Math.pow(10, Math.floor(Math.log10(raw || 1))); const f = raw / p; return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * p; }
function hitbar(svg, f, i, html) {
  const w = (f.W - f.L - f.R) / f.n;
  const r = el("rect", { x: f.x(i) - w / 2, y: f.T, width: w, height: f.H - f.T - f.B, fill: "transparent" }, svg);
  r.addEventListener("pointermove", ev => showTip(ev, html));
  r.addEventListener("pointerleave", hideTip);
}

function status(r) { return D.status[r.id] || "ok"; }
const COLOR = { ok: "var(--good)", wall: "var(--crit)", free: "var(--warn)" };

function drawPayback() {
  const svg = document.getElementById("payback");
  const vals = D.rows.map(r => r.payback).filter(v => v > 0 && isFinite(v));
  const f = frame(svg, { log: true, min: Math.max(1e-4, Math.min(...vals)), max: Math.max(...vals), fmt: minutes });
  const w = Math.max(3, (f.W - f.L - f.R) / f.n - 4);
  D.rows.forEach(r => {
    const top = f.y(Math.max(r.payback, 1e-4)), base = f.H - f.B;
    el("rect", { x: f.x(r.n) - w / 2, y: Math.min(top, base - 2), width: w, height: Math.max(2, base - top), rx: 2, fill: COLOR[status(r)] }, svg);
  });
  D.rows.forEach(r => hitbar(svg, f, r.n, `<b>${r.n}. ${r.name}</b><br><span class="m">cost ${fmt(r.cost)} · payback ${minutes(r.payback)}<br>best before: ${r.rateRecipe} (${fmt(r.rate)}/s)</span>`));
}

function drawTime(nb) {
  const svg = document.getElementById("time");
  let acc = 0; const pts = D.rows.map(r => { acc += (isFinite(r.payback) ? r.payback : 0) / nb; return acc / 60; });
  const f = frame(svg, { log: false, max: Math.max(1, ...pts) * 1.05, fmt: v => v.toFixed(v < 10 ? 1 : 0) + " h" });
  const d = pts.map((h, i) => (i ? "L" : "M") + f.x(i + 1).toFixed(1) + " " + f.y(h).toFixed(1)).join(" ");
  el("path", { d, fill: "none", stroke: "var(--s1)", "stroke-width": 2, "stroke-linejoin": "round" }, svg);
  const last = pts.length - 1;
  el("circle", { cx: f.x(last + 1), cy: f.y(pts[last]), r: 4, fill: "var(--s1)", stroke: "var(--panel)", "stroke-width": 2 }, svg);
  D.rows.forEach((r, i) => hitbar(svg, f, r.n, `<b>${r.n}. ${r.name}</b><br><span class="m">reached after ${pts[i].toFixed(2)} h with ${nb} building${nb > 1 ? "s" : ""}</span>`));
  document.getElementById("nbv").value = nb;
  document.getElementById("statTime").textContent = pts[last].toFixed(1) + " h";
}

function drawRate() {
  const svg = document.getElementById("rate");
  const vals = D.rows.map(r => r.rate).filter(v => v > 0);
  const f = frame(svg, { log: true, min: Math.min(...vals), max: Math.max(...vals) });
  const d = D.rows.map((r, i) => (i ? "L" : "M") + f.x(r.n).toFixed(1) + " " + f.y(r.rate).toFixed(1)).join(" ");
  el("path", { d, fill: "none", stroke: "var(--s1)", "stroke-width": 2, "stroke-linejoin": "round" }, svg);
  D.rows.forEach(r => hitbar(svg, f, r.n, `<b>${r.n}. ${r.name}</b><br><span class="m">best before it: ${fmt(r.rate)} e/s<br>${r.rateRecipe}</span>`));
}

const GROUPS = ["Elements", "Chemistry & biology", "Particles & nucleons"];
const GCOL = { "Elements": "var(--s1)", "Chemistry & biology": "var(--s2)", "Particles & nucleons": "var(--s3)" };
function drawValues() {
  const svg = document.getElementById("values");
  const all = D.rows.flatMap(r => r.items.map(it => ({ ...it, n: r.n, node: r.name })));
  const f = frame(svg, { log: true, min: Math.max(1, Math.min(...all.map(a => a.v))), max: Math.max(...all.map(a => a.v)) });
  document.getElementById("vlegend").innerHTML = GROUPS.map(g => `<span><i style="background:${GCOL[g]}"></i>${g}</span>`).join("");
  const seen = {};
  for (const a of all) {
    const k = a.n + "|" + a.id; if (seen[k]) continue; seen[k] = 1;
    const c = el("circle", { cx: f.x(a.n), cy: f.y(a.v), r: 4.5, fill: GCOL[a.g] || "var(--s-other)", stroke: "var(--panel)", "stroke-width": 1.5 }, svg);
    c.addEventListener("pointermove", ev => showTip(ev, `<b>${a.name}</b> · ${fmt(a.v)} e<br><span class="m">${a.recipe}${a.gain != null ? " · ×" + a.gain.toFixed(2) + " per craft" : ""}<br>unlock ${a.n}: ${a.node}</span>`));
    c.addEventListener("pointerleave", hideTip);
  }
}

function tables() {
  const fb = document.querySelector("#flags tbody");
  const KIND = { "wall": "wall", "free": "free", "loses value": "loses", "late but cheap": "late" };
  fb.innerHTML = D.anomalies.map(a => `<tr><td><span class="pill ${KIND[a.kind] || ""}">${a.kind}</span></td><td><code>${a.what}</code></td><td>${a.why}</td></tr>`).join("")
    || `<tr><td colspan="3">Nothing flagged.</td></tr>`;
  let acc = 0;
  document.querySelector("#order tbody").innerHTML = D.rows.map(r => {
    acc += (isFinite(r.payback) ? r.payback : 0) / D.buildings;
    const s = status(r);
    return `<tr><td class="num">${r.n}</td><td>${r.name} <span class="pill ${s}">${s === "ok" ? "on trend" : s}</span></td><td class="num">${fmt(r.cost)}</td><td class="num">${minutes(r.payback)}</td><td class="num">${(acc / 60).toFixed(2)}</td><td class="num">${fmt(r.rate)}</td><td class="num">${r.top ? fmt(r.top) : "–"}</td></tr>`;
  }).join("");
}

function stats() {
  const counts = { ok: 0, wall: 0, free: 0 }; D.rows.forEach(r => counts[status(r)]++);
  const firstFree = D.rows.find(r => status(r) === "free" && r.n > 5);
  document.getElementById("stats").innerHTML = [
    [D.rows.length, "research nodes"],
    [counts.free, "effectively free"],
    [counts.wall, "walls"],
    ['<span id="statTime">–</span>', "to unlock everything"],
    [firstFree ? firstFree.name : "–", "first node that is free"],
  ].map(([b, s]) => `<div class="stat"><b>${b}</b><span>${s}</span></div>`).join("");
  document.getElementById("foot").textContent =
    `game_data.json ${D.version} · a node is a wall or free when its payback is over ${D.jump}x (or under 1/${D.jump}) the trend of the nodes before it · value added assumes inputs are supplied by the rest of the chain.`;
}

let nb = D.buildings;
try { const s = +localStorage.getItem("pc.buildings"); if (s >= 1 && s <= 40) nb = s; } catch (e) {}
const slider = document.getElementById("nb"); slider.value = nb;
slider.addEventListener("input", () => { nb = +slider.value; try { localStorage.setItem("pc.buildings", nb); } catch (e) {} drawTime(nb); blockTable(nb); });
function blockTable(nb) {
  const tb = document.querySelector("#blocks tbody"); if (!tb) return;
  let acc = 0; const reach = {};
  D.rows.forEach(r => { acc += (isFinite(r.payback) ? r.payback : 0) / nb; reach[r.block] = acc / 60; });
  tb.innerHTML = (D.blocks || []).map(b => {
    const rs = D.rows.filter(r => r.block === b.id);
    const h = rs.reduce((s, r) => s + (isFinite(r.payback) ? r.payback : 0), 0) / nb / 60;
    const ratio = h / b.target, st = b.anchor ? "ok" : ratio > 1.25 ? "wall" : ratio < 0.8 ? "free" : "ok";
    const costs = rs.length ? `${fmt(Math.min(...rs.map(r => r.cost)))}–${fmt(Math.max(...rs.map(r => r.cost)))}` : "–";
    return `<tr><td>${b.name}${b.anchor ? ' <span class="pill ok">tutorial</span>' : ""}</td><td class="num">${rs.length}</td><td class="num">${b.target} h</td>`
         + `<td class="num"><span class="pill ${st}">${h.toFixed(1)} h</span></td><td class="num">${(reach[b.id] ?? 0).toFixed(1)} h</td><td class="num">${costs}</td></tr>`;
  }).join("") || `<tr><td colspan="6">No research blocks in game_data.json.</td></tr>`;
}
stats(); drawPayback(); drawTime(nb); drawRate(); drawValues(); tables(); blockTable(nb);
</script>
"""
