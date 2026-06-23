/* Idle Builder balancing dashboard — pure client-side.
 * Loads a CSV exported from Unity Data Explorer, detects the event type by its columns,
 * and renders the relevant balancing charts with Chart.js. No server, no keys.
 *
 * Column naming: Unity's CSV export may prefix custom event params (e.g. "prestige_completed.run_count")
 * or expose them bare ("run_count"). getCol() tolerates both, plus case differences.
 */

const charts = [];

document.getElementById('file').addEventListener('change', (e) => {
  const file = e.target.files[0];
  if (!file) return;
  setStatus(`Parsing ${file.name}…`);
  Papa.parse(file, {
    header: true,
    dynamicTyping: true,
    skipEmptyLines: true,
    complete: (res) => render(file.name, res.data),
    error: (err) => setStatus(`Parse error: ${err.message}`),
  });
});

function setStatus(msg) { document.getElementById('status').textContent = msg; }

function clearCharts() {
  charts.forEach((c) => c.destroy());
  charts.length = 0;
  document.getElementById('charts').innerHTML = '';
  document.getElementById('meta').innerHTML = '';
  document.getElementById('empty').hidden = true;
}

// Resolve a column whose name may be bare, prefixed by "<event>.", and/or differently cased.
function getCol(row, name) {
  if (name in row) return row[name];
  const lname = name.toLowerCase();
  for (const k of Object.keys(row)) {
    const lk = k.toLowerCase();
    if (lk === lname || lk.endsWith('.' + lname)) return row[k];
  }
  return undefined;
}

function hasCols(rows, names) {
  if (!rows.length) return false;
  return names.every((n) => getCol(rows[0], n) !== undefined);
}

function num(v) { const n = typeof v === 'number' ? v : parseFloat(v); return isFinite(n) ? n : null; }

function render(fileName, rows) {
  clearCharts();
  rows = rows.filter((r) => r && Object.keys(r).length);
  if (!rows.length) { setStatus('Empty CSV'); document.getElementById('empty').hidden = false; return; }

  let kind = 'unknown';
  if (hasCols(rows, ['networth_before', 'run_count'])) kind = 'prestige_completed';
  else if (hasCols(rows, ['entropy_per_sec']) || hasCols(rows, ['prestige_count', 'networth'])) kind = 'player_snapshot';
  else if (hasCols(rows, ['building_id'])) kind = 'building_placed';

  setStatus(`${fileName} — ${rows.length} rows`);
  document.getElementById('intro').hidden = true;
  document.getElementById('meta').innerHTML =
    `<span class="pill">${rows.length} rows</span> <span class="pill">detected: ${kind}</span>`;

  if (kind === 'prestige_completed') renderPrestige(rows);
  else if (kind === 'player_snapshot') renderSnapshot(rows);
  else if (kind === 'building_placed') renderBuildings(rows);
  else document.getElementById('empty').hidden = false;
}

// ── Card + chart helpers ────────────────────────────────────────────────────

function addCard(title, note) {
  const card = document.createElement('div');
  card.className = 'card';
  const h = document.createElement('h2'); h.textContent = title; card.appendChild(h);
  const canvas = document.createElement('canvas'); card.appendChild(canvas);
  if (note) { const n = document.createElement('div'); n.className = 'note'; n.textContent = note; card.appendChild(n); }
  document.getElementById('charts').appendChild(card);
  return canvas;
}

const palette = { accent: '#5ec8f8', accent2: '#f8a85e', grid: '#262b35', ink: '#9aa3b2' };

function baseOpts(xTitle, yTitle) {
  return {
    responsive: true,
    plugins: { legend: { labels: { color: palette.ink } } },
    scales: {
      x: { title: { display: !!xTitle, text: xTitle, color: palette.ink }, ticks: { color: palette.ink }, grid: { color: palette.grid } },
      y: { title: { display: !!yTitle, text: yTitle, color: palette.ink }, ticks: { color: palette.ink }, grid: { color: palette.grid } },
    },
  };
}

function lineChart(canvas, labels, data, label, xTitle, yTitle, color = palette.accent) {
  charts.push(new Chart(canvas, {
    type: 'line',
    data: { labels, datasets: [{ label, data, borderColor: color, backgroundColor: color + '33', tension: 0.2, pointRadius: 2 }] },
    options: baseOpts(xTitle, yTitle),
  }));
}

function barChart(canvas, labels, data, label, xTitle, yTitle, color = palette.accent) {
  charts.push(new Chart(canvas, {
    type: 'bar',
    data: { labels, datasets: [{ label, data, backgroundColor: color + 'cc' }] },
    options: baseOpts(xTitle, yTitle),
  }));
}

function histogram(values, bins = 12) {
  const v = values.filter((x) => x != null);
  if (!v.length) return { labels: [], counts: [] };
  const min = Math.min(...v), max = Math.max(...v);
  const width = (max - min) / bins || 1;
  const counts = new Array(bins).fill(0);
  v.forEach((x) => { const i = Math.min(bins - 1, Math.floor((x - min) / width)); counts[i]++; });
  const labels = counts.map((_, i) => `${Math.round(min + i * width)}`);
  return { labels, counts };
}

// ── Per-event renderers ─────────────────────────────────────────────────────

function renderPrestige(rows) {
  const sorted = [...rows].sort((a, b) => (num(getCol(a, 'run_count')) || 0) - (num(getCol(b, 'run_count')) || 0));
  const runs = sorted.map((r) => num(getCol(r, 'run_count')));

  lineChart(addCard('Net worth at prestige, per run', 'Is per-run power growing too fast / slow across prestiges?'),
    runs, sorted.map((r) => num(getCol(r, 'networth_before'))), 'networth_before', 'run #', 'net worth');

  lineChart(addCard('Prestige currency earned, per run', 'Is the prestige reward formula paying out at the right rate?'),
    runs, sorted.map((r) => num(getCol(r, 'prestige_currency_earned'))), 'prestige_currency_earned', 'run #', '✦ earned', palette.accent2);

  const runLen = histogram(rows.map((r) => num(getCol(r, 'playtime_run_sec'))));
  barChart(addCard('Run length distribution', 'How long is a run? Grind or trivial?'),
    runLen.labels, runLen.counts, 'runs', 'seconds (bin start)', 'count');

  const bc = rows.map((r) => num(getCol(r, 'building_count'))).filter((x) => x != null);
  if (bc.length) {
    lineChart(addCard('Building count at prestige, per run', 'Are builds growing or plateauing across runs?'),
      runs, sorted.map((r) => num(getCol(r, 'building_count'))), 'building_count', 'run #', 'buildings');
  }
}

function renderSnapshot(rows) {
  const sorted = [...rows].sort((a, b) => (num(getCol(a, 'playtime_total_sec')) || 0) - (num(getCol(b, 'playtime_total_sec')) || 0));
  const t = sorted.map((r) => num(getCol(r, 'playtime_total_sec')));

  lineChart(addCard('Income rate over playtime', 'entropy_per_sec = NetWorth growth rate. How steep is income scaling?'),
    t, sorted.map((r) => num(getCol(r, 'entropy_per_sec'))), 'entropy_per_sec', 'session seconds', 'entropy/sec');

  lineChart(addCard('Net worth over playtime', 'Within-run wealth curve.'),
    t, sorted.map((r) => num(getCol(r, 'networth'))), 'networth', 'session seconds', 'net worth', palette.accent2);

  const pc = sorted.map((r) => num(getCol(r, 'prestige_currency')));
  if (pc.some((x) => x != null)) {
    lineChart(addCard('Prestige currency growth', 'Meta-currency accumulation across the session.'),
      t, pc, 'prestige_currency', 'session seconds', '✦');
  }

  const bc = sorted.map((r) => num(getCol(r, 'building_count')));
  if (bc.some((x) => x != null)) {
    lineChart(addCard('Building count over playtime', 'Build growth / grid saturation.'),
      t, bc, 'building_count', 'session seconds', 'buildings');
  }
}

function renderBuildings(rows) {
  const counts = {};
  rows.forEach((r) => { const id = getCol(r, 'building_id'); if (id != null && id !== '') counts[id] = (counts[id] || 0) + 1; });
  const entries = Object.entries(counts).sort((a, b) => b[1] - a[1]);
  barChart(addCard('Buildings placed, by type', 'Which buildings are over/under-used?'),
    entries.map((e) => e[0]), entries.map((e) => e[1]), 'placements', 'building_id', 'count');
}
