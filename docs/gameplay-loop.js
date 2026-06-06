// ═════════════════════════════════════════════════════════════════════════
//  gameplay-loop.js  —  Render logic for gameplay-loop.html
//  Requires: Chart.js (CDN), gameplay_loop_data.js (window.LOOP_DATA)
// ═════════════════════════════════════════════════════════════════════════

// ─── SECTION 1: STATE & CONSTANTS ────────────────────────────────────────

const D = window.LOOP_DATA || {};

// Balance tab — shallow copies so user edits in the UI don't mutate the
// source data in LOOP_DATA (sell values are editable live).
let   BAL_ITEMS    = (D.items     || []).map(x => ({ ...x }));
const BAL_RECIPES   = D.recipes   || [];
const BAL_BUILDINGS = D.buildings || [];
const BAL_RESEARCH  = D.research  || [];
const BAL_CONFIG    = { ...(D.config || { assemblerEvPerMass:5, manipulatorEvPerNeutron:8, prestigeBaseValue:500, startingEntropy:250 }) };

// localStorage keys
const GAPS_STORE   = 'qf_gaps_v1';
const SIMPLE_STORE = 'qf_simple_v1';

// Simple Overview working copy (populated on first render)
let _simpleData = null;

// Chart.js instances — kept so charts can be destroyed before rebuilding
const _balCharts = {};

// Step tag display maps (Overview tab)
const TAG_CSS   = { dialogue:'tag-dialogue', hint:'tag-dialogue', gate:'tag-gate', milestone:'tag-milestone', silent:'tag-silent' };
const TAG_LABEL = { dialogue:'💬 dialogue',  hint:'💬 hint',      gate:'🔬 gate',  milestone:'🎉 milestone',  silent:'silent'     };

// Step type display maps (Simple Overview tab)
const STEP_ICON  = { dialogue:'💬', gate:'🔬', building:'🏭', milestone:'🎉', action:'👆' };
const STEP_COLOR = { dialogue:'#58a6ff', gate:'#f0883e', building:'#3fb950', milestone:'#d2a8ff', action:'#79c0ff' };

// ─── SECTION 2: UTILITIES ────────────────────────────────────────────────

function formatDur(totalSeconds) {
  if (totalSeconds < 60) return totalSeconds + 's';
  const m = Math.floor(totalSeconds / 60), s = totalSeconds % 60;
  return s ? `${m}m ${s}s` : `${m}m`;
}

function phaseTotal(phase) {
  return phase.steps.reduce((sum, s) => sum + s.dur, 0);
}

function catColor(cat) {
  if (cat === 'Raw' || cat === 'Nucleon') return '#6e7681';
  if (cat === 'Element')  return '#3fb950';
  if (cat === 'Isotope')  return '#58a6ff';
  if (cat === 'Particle') return '#f0883e';
  return '#c9d1d9';
}

// Renders a single <td> from a cost/reward descriptor: { t, v, bold, sub }
function tableCell(c) {
  if (!c) return '<td>—</td>';
  const style = c.bold ? ' style="font-weight:600"' : '';
  let v = (c.v || '—').replace(/\n/g, '<br>');
  if (c.sub) v += `<br><em style="font-size:11px;">(${c.sub})</em>`;
  return `<td class="${c.t || 'neutral'}"${style}>${v}</td>`;
}

// ─── SECTION 3: TAB SWITCHING ────────────────────────────────────────────

document.querySelectorAll('.tab').forEach(btn => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
    btn.classList.add('active');
    document.getElementById(btn.dataset.tab).classList.add('active');

    if (btn.dataset.tab === 'balance' && !window._balChartsInit) {
      window._balChartsInit = true;
      if (typeof Chart !== 'undefined') updateBalCharts();
    }
    if (btn.dataset.tab === 'prestige' && !window._prestigeInit) {
      window._prestigeInit = true;
      renderPrestigeTab();
    }
    if (btn.dataset.tab === 'simple' && !window._simpleInit) {
      window._simpleInit = true;
      renderSimpleOverview();
    }
  });
});

// ─── SECTION 4: OVERVIEW TAB ─────────────────────────────────────────────

function renderOverviewTab() {
  const container = document.getElementById('overview-phases');
  if (!container || !D.tutorial_phases) return;

  const THEAD_TUTORIAL = `<thead><tr>
    <th style="width:28px">#</th>
    <th style="width:220px">Step</th>
    <th>Player Action</th>
    <th style="width:160px">Cost</th>
    <th style="width:160px">Reward</th>
    <th style="width:90px">Time Est.</th>
    <th style="width:160px">Advances When</th>
  </tr></thead>`;

  const THEAD_POST = `<thead><tr>
    <th style="width:28px">#</th>
    <th style="width:220px">Step</th>
    <th>Player Action</th>
    <th style="width:160px">Cost</th>
    <th style="width:160px">Reward</th>
    <th style="width:90px">Time Est.</th>
    <th style="width:160px">Unlock Condition</th>
  </tr></thead>`;

  let html = D.tutorial_phases.map(ph => buildPhaseCard(ph, THEAD_TUTORIAL)).join('');
  html += buildPostTutorialHeader();
  html += (D.post_tutorial_phases || []).map(ph => buildPostPhaseCard(ph, THEAD_POST)).join('');
  container.innerHTML = html;
}

function buildPhaseCard(ph, thead) {
  const en = ph.entropy_note || {};
  const enStyle = en.color ? `color:${en.color};` : '';
  const enCls   = en.cls ? `phase-entropy ${en.cls}` : 'phase-entropy';
  const rows = ph.steps.map(buildStepRow).join('');
  return `
    <div class="phase" style="border-left:4px solid ${ph.color || '#4a5568'}">
      <div class="phase-header" style="background:${ph.bg || (ph.color || '#4a5568') + '18'}">
        <h2>${ph.label}</h2>
        <span class="phase-meta">${ph.meta || ''}</span>
        <span class="${enCls}" style="${enStyle}">${en.text || ''}</span>
      </div>
      <table>${thead}<tbody>${rows}</tbody></table>
    </div>`;
}

function buildStepRow(s) {
  if (s.type === 'subheader') {
    return `<tr style="background:#0d1117">
      <td colspan="7" style="padding:6px 12px;font-size:11px;font-weight:600;color:#58a6ff;letter-spacing:0.06em;text-transform:uppercase;border-top:1px solid #21262d;">${s.text}</td>
    </tr>`;
  }
  const rowCls    = s.row_cls ? ` class="row-${s.row_cls}"` : '';
  const tagSpan   = s.tag_type ? `<br><span class="tag ${TAG_CSS[s.tag_type] || 'tag-silent'}">${TAG_LABEL[s.tag_type] || s.tag_type}</span>` : '';
  let actionHtml  = s.action || '';
  if (s.dlg_ref)  actionHtml += `<br><em style="font-size:11px;color:#8b949e;">↳ ${s.dlg_ref}</em>`;
  if (s.warning)  actionHtml += `<br><em style="font-size:11px;color:#f0883e;">⚠ ${s.warning}</em>`;
  if (s.note)     actionHtml += `<br><span class="resolved-note">✅ ${s.note}</span>`;
  return `<tr${rowCls}>
    <td class="step-num">${s.num !== undefined ? s.num : '—'}</td>
    <td class="step-id">${s.id || ''}${tagSpan}</td>
    <td>${actionHtml}</td>
    ${tableCell(s.cost)}${tableCell(s.reward)}
    <td class="time">${s.time || '—'}</td>
    <td><code style="font-size:11px;">${s.advance || '—'}</code></td>
  </tr>`;
}

function buildPostTutorialHeader() {
  return `
    <div style="margin:32px 0 8px;padding:8px 0;border-top:1px solid #30363d;">
      <h2 style="font-size:15px;color:#8b949e;font-weight:600;letter-spacing:0.04em;text-transform:uppercase;">Post-Tutorial Progression (Run 2+)</h2>
      <p style="font-size:12px;color:#6e7681;margin-top:4px;">After first prestige, player returns with research discounts and prestige currency. Prestige base = 500e · wall = 5000e net worth.
        <span class="resolved-note" style="display:inline;">✅ Resolved (gap-2): prestige_base_value = 500 set in game_config.</span></p>
    </div>`;
}

function buildPostPhaseCard(ph, thead) {
  const en = ph.entropy_note || {};
  const PT_TAG_STYLE = 'background:#1a2b4a33;color:#79c0ff;border:1px solid #1a2b4a';
  const rows = (ph.steps || []).map(s => {
    const tag = (s.tag_type === 'gate' || s.tag_type === 'building')
      ? `<br><span class="tag" style="${PT_TAG_STYLE}">${s.tag_type === 'gate' ? '🔬 gate' : '🏭 building'}</span>` : '';
    return `<tr${s.tag_type === 'gate' ? ' class="row-gate"' : ''}>
      <td class="step-num">—</td>
      <td class="step-id">${s.id || ''}${tag}</td>
      <td>${s.action || ''}</td>
      ${tableCell(s.cost)}${tableCell(s.reward)}
      <td class="time">${s.time || '—'}</td>
      <td><code style="font-size:11px;">${s.advance || '—'}</code></td>
    </tr>`;
  }).join('');
  return `
    <div class="phase" style="border-left:4px solid ${ph.color}">
      <div class="phase-header" style="border-left:none;background:${ph.bg}">
        <h2>${ph.label}</h2>
        <span class="phase-meta">${ph.meta}</span>
        <span class="phase-entropy" style="color:${en.color}">${en.text}</span>
      </div>
      <table>${thead}<tbody>${rows}</tbody></table>
    </div>`;
}

// ─── SECTION 5: DIALOGUE TAB ─────────────────────────────────────────────

function renderDialogueTab() {
  const container = document.getElementById('dialogue-phases');
  if (!container || !D.dialogues) return;

  const html = D.dialogues.map(group => {
    const entries = group.entries.map(buildDialogueBlock).join('');
    return `<div class="dialogue-phase-label">${group.phaseLabel}</div>${entries}`;
  }).join('');

  container.innerHTML = html;
}

function buildDialogueBlock(entry) {
  const lines = entry.lines.map((line, i) => {
    const flags = (line.flags || []).map(f => {
      let cls = 'flag';
      if (f === 'pause_game')         cls += ' flag-pause';
      else if (f === 'game live')     cls += ' flag-live';
      else if (f.startsWith('highlight:')) cls += ' flag-target';
      else if (f.startsWith('action:'))   cls += ' flag-action';
      return `<span class="${cls}">${f}</span>`;
    }).join('');
    return `
      <div class="dialogue-line">
        <span class="line-num">${i + 1}</span>
        <div class="line-body">
          <div class="line-speaker">The Architect</div>
          <div class="line-text">${line.text}</div>
          <div class="line-flags">${flags}</div>
        </div>
      </div>`;
  }).join('');

  return `
    <div class="dialogue-block">
      <div class="dialogue-header">
        <span class="step-ref">${entry.step_ref}</span>
        <span class="dlg-id">${entry.dlg_id}</span>
        <span class="step-label">${entry.step_label}</span>
      </div>
      <div class="dialogue-lines">${lines}</div>
    </div>`;
}

// ─── SECTION 6: GAPS TAB ─────────────────────────────────────────────────

function loadGapsState() {
  try { return JSON.parse(localStorage.getItem(GAPS_STORE)) || {}; } catch { return {}; }
}

function saveGapsState(state) {
  localStorage.setItem(GAPS_STORE, JSON.stringify(state));
}

function updateGapsBadge() {
  const open  = document.querySelectorAll('#gaps-body tr:not(.resolved)').length;
  const badge = document.getElementById('gap-badge');
  if (badge) badge.textContent = open > 0 ? open : '';
}

function renderGapsTab() {
  const tbody = document.getElementById('gaps-body');
  if (!tbody || !D.design_gaps) return;

  const saved  = loadGapsState();
  const catCls = { Design:'cat-design', Dialogue:'cat-dialogue', Visualization:'cat-viz', Engineering:'cat-eng' };

  tbody.innerHTML = D.design_gaps.map(gap => {
    const checked = saved[gap.id] !== undefined ? saved[gap.id] : true;
    const sol  = saved[`${gap.id}-sol`]  ?? gap.default_sol;
    const date = saved[`${gap.id}-date`] ?? gap.default_date;
    return `
      <tr data-gap="${gap.id}"${checked ? ' class="resolved"' : ''}>
        <td class="gap-check"><input type="checkbox" data-id="${gap.id}"${checked ? ' checked' : ''}></td>
        <td><span class="cat-badge ${catCls[gap.cat] || ''}">${gap.cat}</span></td>
        <td class="gap-issue">${gap.issue}<small>${gap.detail}</small></td>
        <td><div class="editable" contenteditable="true" data-id="${gap.id}-sol" data-placeholder="Proposed solution…">${sol}</div></td>
        <td class="date-cell"><div class="editable" contenteditable="true" data-id="${gap.id}-date" data-placeholder="YYYY-MM-DD">${date}</div></td>
      </tr>`;
  }).join('');

  setupGapsEvents();
}

function setupGapsEvents() {
  const tbody = document.getElementById('gaps-body');
  if (!tbody) return;
  const state = loadGapsState();

  tbody.addEventListener('change', e => {
    const cb = e.target.closest('input[type=checkbox]');
    if (!cb) return;
    cb.closest('tr').classList.toggle('resolved', cb.checked);
    state[`${cb.dataset.id}-done`] = cb.checked;
    saveGapsState(state);
    updateGapsBadge();
  });

  tbody.addEventListener('input', e => {
    const el = e.target.closest('.editable');
    if (!el) return;
    state[el.dataset.id] = el.textContent.trim();
    saveGapsState(state);
  });

  updateGapsBadge();
}

// ─── SECTION 7: BALANCE TAB ──────────────────────────────────────────────

function renderItemsTable() {
  const tbody = document.getElementById('bal-items-body');
  if (!tbody) return;
  let prevSell = null;
  tbody.innerHTML = BAL_ITEMS.map(item => {
    const eff = item.sell;
    let ratio = '—', ratioClass = '';
    if (prevSell !== null && prevSell > 0) {
      const r = eff / prevSell;
      ratio = '×' + r.toFixed(2);
      ratioClass = r >= 2.5 ? 'ratio-ok' : r >= 1.2 ? 'ratio-warn' : 'ratio-crit';
    }
    prevSell = eff;
    return `<tr>
      <td style="font-weight:600;color:${catColor(item.cat)}">${item.label}</td>
      <td style="font-family:monospace;color:#8b949e;font-size:12px;">${item.sym}</td>
      <td style="font-size:12px;color:${catColor(item.cat)}">${item.cat}</td>
      <td style="text-align:center;font-size:12px;">${item.tier}</td>
      <td style="text-align:right;"><input type="number" class="bal-input" data-item="${item.id}" value="${item.sell}" min="0" step="1"></td>
      <td style="text-align:center;color:#8b949e;font-size:12px;">${item.mult}×</td>
      <td class="derived" style="text-align:right;" data-item-eff="${item.id}">${eff}e</td>
      <td class="${ratioClass}" style="text-align:right;" data-item-ratio="${item.id}">${ratio}</td>
    </tr>`;
  }).join('');

  tbody.querySelectorAll('.bal-input[data-item]').forEach(inp => {
    inp.addEventListener('input', () => {
      const item = BAL_ITEMS.find(x => x.id === inp.dataset.item);
      if (item) { item.sell = parseFloat(inp.value) || 0; refreshBalDerived(); }
      if (window._balChartsInit && typeof Chart !== 'undefined') updateBalCharts();
    });
  });
}

function renderRecipesTable() {
  const tbody = document.getElementById('bal-recipes-body');
  if (!tbody) return;
  tbody.innerHTML = BAL_RECIPES.map(r => {
    const item = BAL_ITEMS.find(x => x.id === r.sellId);
    const sell = item ? item.sell : 0;
    const eps  = r.time > 0 ? sell / r.time : 0;
    const evpe = sell > 0   ? r.powerEV / sell : 0;
    const epsClass = eps < 1.0 ? 'ratio-crit' : eps >= 5 ? 'ratio-ok' : 'ratio-warn';
    return `<tr>
      <td style="font-weight:600;color:#e6edf3;">${r.label}</td>
      <td style="font-size:12px;color:#8b949e;">${r.building}</td>
      <td style="font-size:11px;font-family:monospace;color:#6e7681;">${r.inputs}</td>
      <td class="time" style="text-align:right;">${r.time}s</td>
      <td class="power-cost" style="text-align:right;">${r.powerEV}</td>
      <td class="reward" style="text-align:right;">${sell}</td>
      <td class="derived ${epsClass}" style="text-align:right;" data-recipe-eps="${r.id}">${eps.toFixed(2)}</td>
      <td class="derived" style="text-align:right;color:${evpe > 1 ? '#f85149' : '#8b949e'};" data-recipe-evpe="${r.id}">${evpe.toFixed(3)}</td>
    </tr>`;
  }).join('');
}

function renderBuildingsTable() {
  const tbody = document.getElementById('bal-buildings-body');
  if (!tbody) return;
  const fmt = n => n > 0 ? n.toLocaleString() + 'e' : '—';
  tbody.innerHTML = BAL_BUILDINGS.map(b => {
    const [sp2, sp3, sp4] = [0, 1, 2].map(i => b.ups?.[i] ? fmt(b.ups[i].e) : '—');
    const [st2, st3, st4] = [0, 1, 2].map(i => b.sus?.[i] ? fmt(b.sus[i].e) : '—');
    const outBuf = b.buf?.out > 0 ? b.buf.out : '—';
    const inBuf  = b.buf?.in  > 0 ? b.buf.in  : '—';
    return `<tr>
      <td style="font-weight:600;color:#e6edf3;">${b.label}</td>
      <td style="text-align:center;">${b.tier}</td>
      <td style="font-size:12px;color:#8b949e;">${b.cat}</td>
      <td class="cost" style="text-align:right;">${b.cost > 0 ? b.cost.toLocaleString() + 'e' : 'Free'}</td>
      <td class="power-cost" style="text-align:right;">${b.draw}</td>
      <td class="reward" style="text-align:right;">${b.out}</td>
      <td style="color:#8b949e;font-size:12px;">${b.rate}</td>
      <td style="text-align:right;color:#58a6ff;font-size:12px;">${outBuf}</td>
      <td style="text-align:right;color:#8b949e;font-size:12px;">${inBuf}</td>
      <td class="time" style="text-align:right;">${sp2}</td>
      <td class="time" style="text-align:right;">${sp3}</td>
      <td class="time" style="text-align:right;">${sp4}</td>
      <td style="text-align:right;color:#58a6ff;font-size:12px;">${st2}</td>
      <td style="text-align:right;color:#58a6ff;font-size:12px;">${st3}</td>
      <td style="text-align:right;color:#58a6ff;font-size:12px;">${st4}</td>
    </tr>`;
  }).join('');
}

function renderResearchTable() {
  const tbody = document.getElementById('bal-research-body');
  if (!tbody) return;
  tbody.innerHTML = BAL_RESEARCH.map(r => {
    const disc = Math.round(r.cost * (1 - r.discount));
    return `<tr>
      <td style="font-weight:600;color:#e6edf3;">${r.label}</td>
      <td><span class="tag tag-silent">${r.branch}</span></td>
      <td class="cost" style="text-align:right;">${r.cost}e</td>
      <td class="reward" style="text-align:right;">${disc}e <span style="font-size:11px;color:#6e7681;">(−${Math.round(r.discount * 100)}%)</span></td>
      <td style="font-size:12px;color:#8b949e;">${r.prereqs}</td>
      <td style="font-size:12px;color:#c9d1d9;">${r.unlocks}</td>
    </tr>`;
  }).join('');
}

function refreshBalDerived() {
  let prevSell = null;
  BAL_ITEMS.forEach(item => {
    const eff = item.sell;
    const effEl = document.querySelector(`[data-item-eff="${item.id}"]`);
    if (effEl) effEl.textContent = eff + 'e';
    const ratioEl = document.querySelector(`[data-item-ratio="${item.id}"]`);
    if (ratioEl && prevSell !== null && prevSell > 0) {
      const r = eff / prevSell;
      ratioEl.textContent = '×' + r.toFixed(2);
      ratioEl.className = r >= 2.5 ? 'ratio-ok' : r >= 1.2 ? 'ratio-warn' : 'ratio-crit';
    }
    prevSell = eff;
  });
  BAL_RECIPES.forEach(r => {
    const item = BAL_ITEMS.find(x => x.id === r.sellId);
    const sell = item ? item.sell : 0;
    const eps  = r.time > 0 ? sell / r.time : 0;
    const evpe = sell > 0   ? r.powerEV / sell : 0;
    const epsEl  = document.querySelector(`[data-recipe-eps="${r.id}"]`);
    const evpeEl = document.querySelector(`[data-recipe-evpe="${r.id}"]`);
    if (epsEl)  { epsEl.textContent  = eps.toFixed(2);  epsEl.className = 'derived ' + (eps < 1 ? 'ratio-crit' : eps >= 5 ? 'ratio-ok' : 'ratio-warn'); }
    if (evpeEl) { evpeEl.textContent = evpe.toFixed(3); evpeEl.style.color = evpe > 1 ? '#f85149' : '#8b949e'; }
  });
  const wallEl = document.getElementById('cfg-prestige-wall');
  if (wallEl) wallEl.textContent = (BAL_CONFIG.prestigeBaseValue * 10).toLocaleString() + 'e';
}

function updateConfigFromInputs() {
  const get = id => parseFloat(document.getElementById(id)?.value);
  const mass     = get('cfg-ev-mass');
  const neutron  = get('cfg-ev-neutron');
  const prestige = get('cfg-prestige-base');
  const starting = get('cfg-starting-entropy');
  if (!isNaN(mass))     BAL_CONFIG.assemblerEvPerMass        = mass;
  if (!isNaN(neutron))  BAL_CONFIG.manipulatorEvPerNeutron   = neutron;
  if (!isNaN(prestige)) BAL_CONFIG.prestigeBaseValue         = prestige;
  if (!isNaN(starting)) BAL_CONFIG.startingEntropy           = starting;
  refreshBalDerived();
  if (window._balChartsInit && typeof Chart !== 'undefined') updateBalCharts();
}

// Chart helpers
function destroyBalChart(id) {
  if (_balCharts[id]) { _balCharts[id].destroy(); delete _balCharts[id]; }
}

function updateBalCharts() {
  buildValueLadderChart();
  buildGateCostChart();
  buildPowerBudgetChart();
  buildEfficiencyChart();
  buildProgressionChart();
}

function buildValueLadderChart() {
  destroyBalChart('valueLadder');
  const ctx = document.getElementById('chartValueLadder');
  if (!ctx) return;
  const colorMap = { Raw:'#484f58', Nucleon:'#484f58', Element:'#3fb950cc', Isotope:'#58a6ffcc', Particle:'#f0883ecc' };
  _balCharts.valueLadder = new Chart(ctx, {
    type: 'bar',
    data: {
      labels: BAL_ITEMS.map(i => i.label),
      datasets: [{ label:'Sell (e)', data: BAL_ITEMS.map(i => i.sell), backgroundColor: BAL_ITEMS.map(i => colorMap[i.cat] || '#484f58'), borderWidth:0 }]
    },
    options: {
      indexAxis: 'y', responsive: true,
      plugins: { legend: { display:false } },
      scales: {
        x: { type:'logarithmic', ticks:{ color:'#8b949e', font:{size:9}, callback: v => v>=1000?(v/1000)+'k':v }, grid:{color:'#21262d'}, title:{display:true,text:'Entropy (log)',color:'#8b949e',font:{size:9}} },
        y: { ticks:{ color:'#8b949e', font:{size:9} }, grid:{color:'#21262d'} }
      }
    }
  });
}

function buildGateCostChart() {
  destroyBalChart('gateCost');
  const ctx = document.getElementById('chartGateCost');
  if (!ctx) return;
  const gates   = [{ label:'Recomb. I',cost:100 },{ label:'H Synthesis',cost:50 },{ label:'Automation I',cost:25 },{ label:'Atomic Asm.',cost:500 },{ label:'Isotope Eng.',cost:1500 },{ label:'Heavy Elem.',cost:3000 },{ label:'Radioactive',cost:5000 }];
  const surplus = [250, 165, 127, 500, 1500, 3000, 5000];
  _balCharts.gateCost = new Chart(ctx, {
    type: 'bar',
    data: {
      labels: gates.map(g => g.label),
      datasets: [
        { label:'Gate Cost (e)', data:gates.map(g=>g.cost), backgroundColor:'#f0883e88', borderColor:'#f0883e', borderWidth:1 },
        { label:'Player Surplus', data:surplus, type:'line', borderColor:'#3fb950', backgroundColor:'transparent', pointBackgroundColor:'#3fb950', borderWidth:2, tension:0.3 }
      ]
    },
    options: {
      responsive: true,
      plugins: { legend:{ labels:{ color:'#8b949e',font:{size:9} } } },
      scales: {
        x: { ticks:{color:'#8b949e',font:{size:9}}, grid:{color:'#21262d'} },
        y: { type:'logarithmic', ticks:{color:'#8b949e',font:{size:9},callback: v=>v>=1000?(v/1000)+'k':v}, grid:{color:'#21262d'}, title:{display:true,text:'Entropy (log)',color:'#8b949e',font:{size:9}} }
      }
    }
  });
}

function buildPowerBudgetChart() {
  destroyBalChart('powerBudget');
  const ctx = document.getElementById('chartPowerBudget');
  if (!ctx) return;
  const phases = ['Tutorial\nEnd','Post-\nAtomic Asm.','Post-\nHeavy Elem.','Post-\nIsotopes','Post-\nRadioactive'];
  _balCharts.powerBudget = new Chart(ctx, {
    type: 'bar',
    data: {
      labels: phases,
      datasets: [
        { label:'SFC (10)',         data:[10, 0,   0,    0,    0   ], backgroundColor:'#484f5888', borderWidth:0 },
        { label:'Atom Gen (5)',     data:[5,  5,   5,    5,    5   ], backgroundColor:'#8b949e88', borderWidth:0 },
        { label:'Assembler',        data:[0,  20,  1190, 1190, 1190], backgroundColor:'#d2a8ff88', borderWidth:0 },
        { label:'Manipulator',      data:[0,  0,   0,    8,    24  ], backgroundColor:'#58a6ff88', borderWidth:0 },
        { label:'Containment (30)', data:[0,  0,   0,    0,    30  ], backgroundColor:'#f0883e88', borderWidth:0 },
        { label:'Generator Cap', data:[50,100,400,400,400], type:'line', borderColor:'#3fb950', backgroundColor:'transparent', pointBackgroundColor:'#3fb950', borderWidth:2, borderDash:[4,3] }
      ]
    },
    options: {
      responsive: true,
      plugins: {
        legend: { labels:{ color:'#8b949e',font:{size:9} } },
        tooltip: { callbacks: { footer: items => { const t = items.reduce((s,i)=>i.dataset.label!=='Generator Cap'?s+i.raw:s,0); return t>0?'Total draw: '+t+' eV':''; } } }
      },
      scales: {
        x: { stacked:true, ticks:{color:'#8b949e',font:{size:9}}, grid:{color:'#21262d'} },
        y: { stacked:true, title:{display:true,text:'eV',color:'#8b949e',font:{size:9}}, ticks:{color:'#8b949e',font:{size:9}}, grid:{color:'#21262d'} }
      }
    }
  });
}

function buildEfficiencyChart() {
  destroyBalChart('efficiency');
  const ctx = document.getElementById('chartEfficiency');
  if (!ctx) return;
  const points = BAL_RECIPES.map(r => {
    const item = BAL_ITEMS.find(x => x.id === r.sellId);
    const sell = item ? item.sell : 0;
    return { x: r.time, y: r.time > 0 ? sell / r.time : 0, label: r.label };
  });
  const colors = BAL_RECIPES.map(r =>
    r.building === 'SFC' ? '#484f58' : r.building === 'Assembler' ? '#3fb950cc' : '#58a6ffcc'
  );
  _balCharts.efficiency = new Chart(ctx, {
    type: 'scatter',
    data: { datasets: [{ label:'Recipes', data: points.map(p=>({x:p.x,y:p.y})), backgroundColor:colors, pointRadius:6, pointHoverRadius:8 }] },
    options: {
      responsive: true,
      plugins: { legend:{display:false}, tooltip:{ callbacks:{ label: c => `${points[c.dataIndex].label}: ${points[c.dataIndex].y.toFixed(2)} e/s` } } },
      scales: {
        x: { type:'logarithmic', title:{display:true,text:'Craft time (s, log)',color:'#8b949e',font:{size:9}}, ticks:{color:'#8b949e',font:{size:9}}, grid:{color:'#21262d'} },
        y: { type:'logarithmic', title:{display:true,text:'e/s (log)',color:'#8b949e',font:{size:9}}, ticks:{color:'#8b949e',font:{size:9},callback: v=>v>=100?v:v.toFixed(1)}, grid:{color:'#21262d'} }
      }
    }
  });
}

function buildProgressionChart() {
  destroyBalChart('progression');
  const ctx = document.getElementById('chartProgression');
  if (!ctx) return;
  const milestones = ['Quark','Nucleon','Hydrogen','He-4','Li','Carbon','Oxygen','Silicon','Iron','Uranium'];
  const thisGame   = [1,3,5,20,35,60,80,140,280,1000];
  const cc  = milestones.map((_,i) => Math.round(Math.pow(2.2, i) * 10) / 10);
  const c2s = milestones.map((_,i) => Math.round(Math.pow(3.5, i) * 10) / 10);
  _balCharts.progression = new Chart(ctx, {
    type: 'line',
    data: {
      labels: milestones,
      datasets: [
        { label:'This Game',                   data:thisGame, borderColor:'#d2a8ff', backgroundColor:'#d2a8ff22', fill:false, borderWidth:2.5, pointRadius:4, tension:0.2 },
        { label:'Generic Idle (×2.2 per step)',data:cc,       borderColor:'#f0883e', backgroundColor:'transparent', fill:false, borderWidth:1.5, borderDash:[5,3], pointRadius:2, tension:0.2 },
        { label:'Cell to Singularity (×3.5)',  data:c2s,      borderColor:'#58a6ff', backgroundColor:'transparent', fill:false, borderWidth:1.5, borderDash:[2,2], pointRadius:2, tension:0.2 }
      ]
    },
    options: {
      responsive: true,
      plugins: { legend:{ labels:{ color:'#8b949e',font:{size:10} } } },
      scales: {
        x: { ticks:{color:'#8b949e',font:{size:10}}, grid:{color:'#21262d'} },
        y: { type:'logarithmic', title:{display:true,text:'Value (log scale)',color:'#8b949e',font:{size:10}}, ticks:{color:'#8b949e',font:{size:10},callback:v=>v>=1000?(v/1000).toFixed(0)+'k':v>=1?v:''}, grid:{color:'#21262d'} }
      }
    }
  });
}

// ─── SECTION 8: SIMPLE OVERVIEW TAB ──────────────────────────────────────

function loadSimpleData() {
  try {
    const saved = JSON.parse(localStorage.getItem(SIMPLE_STORE));
    return saved || JSON.parse(JSON.stringify(D.simple_overview || []));
  } catch {
    return JSON.parse(JSON.stringify(D.simple_overview || []));
  }
}

function saveSimpleData(data) {
  localStorage.setItem(SIMPLE_STORE, JSON.stringify(data));
}

function buildSimplePhaseCard(ph, phIdx, cumul) {
  const pt     = phaseTotal(ph);
  const maxDur = Math.max(...ph.steps.map(s => s.dur), 1);

  const card = document.createElement('div');
  card.className = 'simple-phase-card';

  const hdr = document.createElement('div');
  hdr.className = 'simple-phase-hdr';
  hdr.style.cssText = `border-left:4px solid ${ph.phaseColor};background:${ph.phaseColor}18`;
  hdr.innerHTML = `<h2>Phase ${ph.phase}: ${ph.phaseName}</h2><span class="ph-time">${formatDur(pt)}</span><span class="ph-toggle">▾</span>`;

  const body = document.createElement('div');
  body.className = 'simple-phase-body';

  hdr.addEventListener('click', () => {
    body.classList.toggle('collapsed');
    hdr.querySelector('.ph-toggle').textContent = body.classList.contains('collapsed') ? '▸' : '▾';
  });

  ph.steps.forEach(step => {
    const pct   = Math.round(step.dur / maxDur * 100);
    const color = STEP_COLOR[step.type] || '#58a6ff';
    const row   = document.createElement('div');
    row.className = 'simple-step-row';
    row.innerHTML = `
      <span class="simple-icon">${STEP_ICON[step.type] || '▪'}</span>
      <span class="simple-name">${step.name}</span>
      <div class="simple-bar-wrap"><div class="simple-bar" style="width:${pct}%;background:${color}"></div></div>
      <span class="simple-dur">${formatDur(step.dur)}</span>`;
    body.appendChild(row);
  });

  const cumulRow = document.createElement('div');
  cumulRow.className = 'simple-cumul';
  cumulRow.innerHTML = `Cumulative: <span>${formatDur(cumul)}</span>`;
  body.appendChild(cumulRow);

  body.appendChild(buildAddStepBtn(phIdx, body));
  card.appendChild(hdr);
  card.appendChild(body);
  return card;
}

function buildAddStepBtn(phIdx, body) {
  const btn = document.createElement('button');
  btn.className = 'simple-add-btn';
  btn.textContent = '+ Add Step';
  btn.addEventListener('click', () => {
    if (body.querySelector('.simple-inline-form')) return;
    btn.style.display = 'none';
    const form = buildAddStepForm(
      step => { _simpleData[phIdx].steps.push(step); saveSimpleData(_simpleData); renderSimpleOverview(); },
      ()   => { form.remove(); btn.style.display = ''; }
    );
    body.insertBefore(form, btn);
  });
  return btn;
}

function buildAddStepForm(onConfirm, onCancel) {
  const form = document.createElement('div');
  form.className = 'simple-inline-form';
  form.innerHTML = `
    <input class="sif-name" type="text" placeholder="Step name">
    <select>
      <option value="action">👆 action</option>
      <option value="dialogue">💬 dialogue</option>
      <option value="gate">🔬 gate</option>
      <option value="building">🏭 building</option>
      <option value="milestone">🎉 milestone</option>
    </select>
    <input class="sif-num" type="number" placeholder="min" min="0" title="minutes">
    <input class="sif-num" type="number" placeholder="sec" min="0" max="59" title="seconds">
    <button class="sif-confirm">Add</button>
    <button class="sif-cancel">Cancel</button>`;
  form.querySelector('.sif-confirm').addEventListener('click', () => {
    const name = form.querySelector('.sif-name').value.trim();
    const type = form.querySelector('select').value;
    const mins = parseInt(form.querySelector('[title="minutes"]').value)  || 0;
    const secs = parseInt(form.querySelector('[title="seconds"]').value)  || 0;
    const dur  = mins * 60 + secs;
    if (!name || dur <= 0) { alert('Enter a name and duration > 0.'); return; }
    onConfirm({ name, type, dur });
  });
  form.querySelector('.sif-cancel').addEventListener('click', onCancel);
  return form;
}

function buildAddPhaseBtn(timeline) {
  const btn = document.createElement('button');
  btn.className = 'simple-add-phase-btn';
  btn.textContent = '+ Add Phase';
  btn.addEventListener('click', () => {
    if (timeline.querySelector('.simple-phase-form')) return;
    btn.style.display = 'none';
    const form = buildAddPhaseForm(
      name => {
        const last    = _simpleData[_simpleData.length - 1];
        const nextNum = typeof last.phase === 'number' ? last.phase + 1 : _simpleData.length + 1;
        _simpleData.push({ phase: nextNum, phaseName: name, phaseColor: '#4a5568', isTutorial: false, steps: [] });
        saveSimpleData(_simpleData);
        renderSimpleOverview();
      },
      () => { form.remove(); btn.style.display = ''; }
    );
    timeline.insertBefore(form, btn);
  });
  return btn;
}

function buildAddPhaseForm(onConfirm, onCancel) {
  const form = document.createElement('div');
  form.className = 'simple-phase-form';
  form.innerHTML = `
    <input type="text" placeholder="Phase name (e.g. Stellar Fusion)">
    <button class="sif-confirm">Add</button>
    <button class="sif-cancel">Cancel</button>`;
  form.querySelector('.sif-confirm').addEventListener('click', () => {
    const name = form.querySelector('input').value.trim();
    if (!name) return;
    onConfirm(name);
  });
  form.querySelector('.sif-cancel').addEventListener('click', onCancel);
  return form;
}

function updateSimpleSidebar(data) {
  let tutorialSec = 0, fullSec = 0, totalSteps = 0;
  data.forEach(ph => {
    const t = phaseTotal(ph);
    fullSec += t;
    if (ph.isTutorial) tutorialSec += t;
    totalSteps += ph.steps.length;
  });
  const set = (id, v) => { const el = document.getElementById(id); if (el) el.textContent = v; };
  set('s-total-tutorial', formatDur(tutorialSec));
  set('s-total-full',     formatDur(fullSec));
  set('s-phase-count',    data.length);
  set('s-step-count',     totalSteps);
}

function renderSimpleOverview() {
  const timeline = document.getElementById('simple-timeline');
  if (!timeline) return;

  _simpleData = loadSimpleData();
  updateSimpleSidebar(_simpleData);

  timeline.innerHTML = '';
  let cumul = 0;
  _simpleData.forEach((ph, phIdx) => {
    cumul += phaseTotal(ph);
    timeline.appendChild(buildSimplePhaseCard(ph, phIdx, cumul));
  });
  timeline.appendChild(buildAddPhaseBtn(timeline));
}

document.getElementById('s-reset-btn')?.addEventListener('click', () => {
  if (!confirm('Reset to default steps? Custom phases and steps will be lost.')) return;
  localStorage.removeItem(SIMPLE_STORE);
  renderSimpleOverview();
});

// ─── SECTION 8b: PRESTIGE SHOP TAB ──────────────────────────────────────

const PC_DATA = (D.prestige || {});
let   _pcChart = null;

// ── Formula helpers ──

function pcEarned(netWorth, base, scale) {
  if (base <= 0 || netWorth <= 0) return 0;
  return Math.max(0, Math.floor(Math.log10(netWorth / base) * scale));
}

// ── §1 — Formula section ──

function updatePrestigeFormula() {
  const base  = parseFloat(document.getElementById('pc-base')?.value)  || 5000;
  const scale = parseFloat(document.getElementById('pc-scale')?.value) || 50;
  const mult  = parseFloat(document.getElementById('pc-wall-mult')?.value) || 10;
  const wall  = base * mult;

  const wallEl = document.getElementById('pc-wall-display');
  const firstEl = document.getElementById('pc-first-earn');
  if (wallEl)  wallEl.textContent  = wall.toLocaleString() + ' e';
  if (firstEl) firstEl.textContent = pcEarned(wall, base, scale) + ' ✦';

  // Earnings table
  const netWorthPoints = [
    wall,
    wall * 2,
    wall * 10,
    wall * 100,
    wall * 1000,
    wall * 10000,
    wall * 100000,
    wall * 1000000,
  ];

  const tbody = document.getElementById('pc-earnings-body');
  if (tbody) {
    tbody.innerHTML = '';
    let prev = 0;
    netWorthPoints.forEach(nw => {
      const earned = pcEarned(nw, base, scale);
      const delta  = earned - prev;
      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td style="padding:3px 8px;color:#c9d1d9;">${formatNW(nw)}</td>
        <td style="padding:3px 8px;text-align:right;color:#d2a8ff;">${earned} ✦</td>
        <td style="padding:3px 8px;text-align:right;color:#58a6ff;">+${delta}</td>`;
      tbody.appendChild(tr);
      prev = earned;
    });
  }

  // Chart
  updatePrestigeChart(netWorthPoints, base, scale);

  // Keep simulator max in sync
  const simInput = document.getElementById('sim-pc');
  if (simInput) {
    const maxPc = pcEarned(netWorthPoints[netWorthPoints.length - 1], base, scale);
    simInput.max = Math.max(maxPc, 30000);
  }
}

function formatNW(n) {
  if (n >= 1e12) return (n / 1e12).toPrecision(3) + 'T';
  if (n >= 1e9)  return (n / 1e9).toPrecision(3)  + 'B';
  if (n >= 1e6)  return (n / 1e6).toPrecision(3)  + 'M';
  if (n >= 1e3)  return (n / 1e3).toPrecision(3)  + 'K';
  return n.toLocaleString();
}

function updatePrestigeChart(netWorthPoints, base, scale) {
  const canvas = document.getElementById('pc-chart');
  if (!canvas || typeof Chart === 'undefined') return;
  if (_pcChart) { _pcChart.destroy(); _pcChart = null; }

  const labels = netWorthPoints.map(formatNW);
  const values = netWorthPoints.map(nw => pcEarned(nw, base, scale));

  _pcChart = new Chart(canvas, {
    type: 'bar',
    data: {
      labels,
      datasets: [{
        label: 'PC Earned (✦)',
        data: values,
        backgroundColor: 'rgba(210,168,255,0.55)',
        borderColor:     '#d2a8ff',
        borderWidth:     1,
      }],
    },
    options: {
      responsive: true,
      plugins: {
        legend: { display: false },
        tooltip: { callbacks: { label: ctx => `${ctx.raw} ✦` } },
      },
      scales: {
        x: { ticks: { color: '#8b949e', font: { size: 10 } }, grid: { color: '#21262d' } },
        y: { ticks: { color: '#8b949e', font: { size: 10 } }, grid: { color: '#21262d' } },
      },
    },
  });
}

// ── §2 — Upgrades table ──

function renderPrestigeUpgradesTable() {
  const tbody = document.getElementById('prestige-upgrades-body');
  if (!tbody || !PC_DATA.upgrades) return;

  const TIER_COLOR = { 1: '#3fb950', 2: '#58a6ff', 3: '#f0883e' };

  tbody.innerHTML = '';
  PC_DATA.upgrades.forEach((up, i) => {
    const total    = up.costs.reduce((a, b) => a + b, 0);
    const prereqStr = up.prereqs.length === 0 ? '—' :
      up.prereqs.map(p => {
        const pd = PC_DATA.upgrades.find(u => u.id === p.id);
        return `${pd ? pd.name : p.id} L${p.minLevel}`;
      }).join(', ');

    const tierColor = TIER_COLOR[up.tier] || '#8b949e';
    const costStr   = up.costs.join(' · ');
    const effectStr = `+${up.effectPerLevel} ${up.unit} / lv`;

    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td class="step-num">${i + 1}</td>
      <td style="font-weight:600;color:#e6edf3;">${up.name}</td>
      <td style="text-align:center;"><span style="color:${tierColor};font-size:11px;font-weight:700;">T${up.tier}</span></td>
      <td style="font-size:12px;color:#8b949e;">${effectStr}</td>
      <td style="text-align:center;">${up.maxLevel}</td>
      <td style="font-size:11px;color:#d2a8ff;font-family:monospace;">${costStr}</td>
      <td style="text-align:right;color:#d2a8ff;font-weight:600;">${total.toLocaleString()}</td>
      <td style="font-size:11px;color:#8b949e;">${prereqStr}</td>`;
    tbody.appendChild(tr);
  });
}

// ── §4 — Simulator ──

function updatePrestigeSimulator(totalPC) {
  const statusEl  = document.getElementById('sim-upgrade-status');
  const effectEl  = document.getElementById('sim-effect-summary');
  if (!statusEl || !effectEl || !PC_DATA.upgrades) return;

  // Greedily purchase upgrades in order (respecting prereqs) with the given budget
  const levels = {};
  const getBought = id => levels[id] || 0;

  // Iterate until no more purchases are possible with remaining budget
  let remaining = totalPC;
  let changed = true;
  while (changed) {
    changed = false;
    for (const up of PC_DATA.upgrades) {
      const cur = getBought(up.id);
      if (cur >= up.maxLevel) continue;
      // Check prereqs
      const prereqsMet = up.prereqs.every(p => getBought(p.id) >= p.minLevel);
      if (!prereqsMet) continue;
      const cost = up.costs[cur];
      if (remaining >= cost) {
        remaining -= cost;
        levels[up.id] = cur + 1;
        changed = true;
      }
    }
  }

  // Render status
  const TIER_COLOR = { 1: '#3fb950', 2: '#58a6ff', 3: '#f0883e' };
  let statusHtml = '';
  for (const up of PC_DATA.upgrades) {
    const cur = getBought(up.id);
    if (cur === 0) continue;
    const isMax = cur >= up.maxLevel;
    const color = isMax ? '#3fb950' : '#d2a8ff';
    const badge = isMax ? '★ MAX' : `Lv ${cur}/${up.maxLevel}`;
    statusHtml += `<div style="margin-bottom:3px;color:${color};">${up.name} — ${badge}</div>`;
  }
  const lockedCount = PC_DATA.upgrades.filter(up => getBought(up.id) === 0).length;
  if (lockedCount > 0) {
    statusHtml += `<div style="color:#484f58;margin-top:4px;">${lockedCount} upgrade${lockedCount > 1 ? 's' : ''} locked</div>`;
  }
  statusEl.innerHTML = statusHtml || '<span style="color:#484f58;">No upgrades yet</span>';

  // Render effect summary
  const effectMap = {};
  for (const up of PC_DATA.upgrades) {
    const cur = getBought(up.id);
    if (cur === 0) continue;
    const total = up.effectPerLevel * cur;
    effectMap[up.effectType] = (effectMap[up.effectType] || 0) + total;
  }

  const EFFECT_LABEL = {
    StartingEntropyBonus:     (v) => `+${v} e starting entropy`,
    GlobalResearchDiscount:   (v) => `−${v}% all research costs`,
    CraftSpeedMultiplier:     (v) => `+${v}% craft speed`,
    VaultCapacity:            (v) => `+${v} inventory slots`,
    OutputQuantityMultiplier: (v) => `+${v}% output quantity`,
    BuildingCostReduction:    (v) => `−${v}% building costs`,
    DecayCollectionRate:      (v) => `+${v}% decay collection`,
    BuildingStartPrePlaced:   (v) => `+${v} pre-placed Harvester${v > 1 ? 's' : ''}`,
    ResearchSpeed:            (v) => `+${v}% research speed`,
    PrestigeGainMultiplier:   (v) => `+${v}% prestige currency earned`,
  };

  let effectHtml = '<strong style="color:#3fb950;display:block;margin-bottom:6px;">Combined Effects</strong>';
  let hasEffect = false;
  for (const [type, val] of Object.entries(effectMap)) {
    if (!val) continue;
    hasEffect = true;
    const label = EFFECT_LABEL[type] ? EFFECT_LABEL[type](val) : `${type}: +${val}`;
    effectHtml += `<div style="margin-bottom:3px;">✓ ${label}</div>`;
  }
  if (!hasEffect) effectHtml += '<span style="color:#484f58;">No active effects</span>';
  effectEl.innerHTML = effectHtml;
}

function renderPrestigeTab() {
  updatePrestigeFormula();
  renderPrestigeUpgradesTable();
  updatePrestigeSimulator(0);
}

// ── Prestige tab event wiring ──

['pc-base', 'pc-scale', 'pc-wall-mult'].forEach(id => {
  document.getElementById(id)?.addEventListener('input', updatePrestigeFormula);
});

document.getElementById('sim-pc')?.addEventListener('input', e => {
  const v = parseInt(e.target.value) || 0;
  const el = document.getElementById('sim-pc-display');
  if (el) el.textContent = v.toLocaleString() + ' ✦';
  updatePrestigeSimulator(v);
});

// ─── SECTION 9: INIT ─────────────────────────────────────────────────────
// Runs once at page load. DOM is ready because this script is at end of body.

renderOverviewTab();
renderDialogueTab();
renderGapsTab();
renderItemsTable();
renderRecipesTable();
renderBuildingsTable();
renderResearchTable();
refreshBalDerived();

['cfg-ev-mass', 'cfg-ev-neutron', 'cfg-prestige-base', 'cfg-starting-entropy'].forEach(id => {
  document.getElementById(id)?.addEventListener('input', updateConfigFromInputs);
});
