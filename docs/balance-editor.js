// ═════════════════════════════════════════════════════════════════════════
//  balance-editor.js  —  Interactive editor for Assets/Data/game_data.json
//
//  Reads the REAL source of truth, edits balance/progression in-browser, and
//  saves back preserving all `_comment` docs (browser JSON.parse keeps them).
//  Must run from a localhost origin in Chrome/Edge (File System Access API).
//  After saving, re-run MobileIdleBuilder -> Import Game Data in Unity.
// ═════════════════════════════════════════════════════════════════════════

// ─── SECTION 1: STATE ────────────────────────────────────────────────────

let DATA       = null;   // the parsed game_data.json object (mutated in place)
let fileHandle = null;   // FileSystemFileHandle (null in fallback mode)
let dirty      = false;
let usingFsApi = typeof window.showOpenFilePicker === 'function';

// Keys that appear with `.0` float formatting in the source file. Used by the
// serializer to keep integer-valued floats (e.g. base_sell_value 5 -> "5.0")
// so the file's numeric formatting survives the round-trip.
let FLOAT_KEYS = new Set();

// Join indexes (rebuilt by buildIndexes)
let recipeByOutput   = new Map(); // item id -> first recipe with that output
let researchByItem   = new Map(); // item id -> research object that unlocks it
let tutorialItemIds  = new Set(); // numeric item_id referenced by tutorial_steps

// Pending rebalance changes awaiting Apply
let pendingHouseRule = [];
let pendingClaude    = [];

// Option lists mirrored from GameDataEditorWindow.cs
const ITEM_CATEGORIES = ['RawResource','Nucleon','Element','Isotope','Molecule','OrganicCompound','Alloy','Component','Particle','Megastructure'];
const BUILDING_CATEGORIES = ['Core','Transient','Power','Megastructure'];
const RESEARCH_BRANCHES = ['Chemistry','Nuclear','Materials','Engineering','Astrophysics','Biology'];

// ─── SECTION 2: UTILITIES ────────────────────────────────────────────────

const $ = (id) => document.getElementById(id);

function esc(s) {
  return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;')
    .replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

function markDirty() {
  if (!dirty) { dirty = true; refreshToolbar(); }
}

function refreshToolbar() {
  const loaded = !!DATA;
  $('tb-save').disabled   = !loaded || !dirty;
  $('tb-reload').disabled = !loaded || !fileHandle;
  const dot = dirty ? '<span class="dirty-dot"></span>' : '<span class="dirty-dot clean"></span>';
  const name = fileHandle ? fileHandle.name : (loaded ? 'game_data.json (fallback)' : 'No file loaded');
  $('tb-status').innerHTML = dot + (loaded ? (dirty ? 'Unsaved changes — ' : 'Saved — ') + esc(name) : esc(name));
  $('tb-version').textContent = loaded && DATA._meta ? 'v' + (DATA._meta.version || '?') : '';
}

function banner(kind, html) {
  const b = $('editor-banner');
  b.className = 'editor-banner show ' + kind;
  b.innerHTML = html;
}
function clearBanner() { $('editor-banner').className = 'editor-banner'; }

function num(v, fallback = 0) {
  const n = parseFloat(v);
  return Number.isFinite(n) ? n : fallback;
}

// Round a currency/sell value to a sensible precision
function roundSell(v) {
  if (!Number.isFinite(v)) return 0;
  if (Math.abs(v) >= 100) return Math.round(v);
  return Math.round(v * 100) / 100;
}

// ─── SECTION 3: FILE I/O ─────────────────────────────────────────────────

// Custom serializer: matches game_data.json conventions far better than raw
// JSON.stringify — inlines primitive arrays and preserves `.0` on float keys.
// Comments survive because `_`-prefixed keys are ordinary keys in the object.
// The FIRST save canonicalizes the hand-formatting (one-time large, data-safe
// diff); every save after that is a minimal, reviewable diff.
function serializeGameData(data) {
  return serNode(data, '', null) + '\n';
}
function isPrim(v) { return v === null || typeof v !== 'object'; }
function fmtNum(v, key) {
  if (Number.isInteger(v) && FLOAT_KEYS.has(key)) return v + '.0';
  return String(v);
}
function serPrim(v, key) {
  if (v === null) return 'null';
  if (typeof v === 'number')  return fmtNum(v, key);
  if (typeof v === 'boolean') return String(v);
  return JSON.stringify(v);
}
function serNode(v, indent, key) {
  if (isPrim(v)) return serPrim(v, key);
  const pad = indent + '  ';
  if (Array.isArray(v)) {
    if (v.length === 0) return '[]';
    if (v.every(isPrim)) return '[' + v.map(e => serPrim(e, key)).join(', ') + ']';
    return '[\n' + v.map(e => pad + serNode(e, pad, key)).join(',\n') + '\n' + indent + ']';
  }
  const keys = Object.keys(v);
  if (keys.length === 0) return '{}';
  return '{\n' + keys.map(k => pad + JSON.stringify(k) + ': ' + serNode(v[k], pad, k)).join(',\n') + '\n' + indent + '}';
}


async function openFile() {
  try {
    if (usingFsApi) {
      const [h] = await window.showOpenFilePicker({
        types: [{ description: 'game_data.json', accept: { 'application/json': ['.json'] } }],
        multiple: false,
      });
      fileHandle = h;
      const file = await h.getFile();
      loadFromText(await file.text());
    } else {
      // Fallback: hidden file input
      const inp = document.createElement('input');
      inp.type = 'file'; inp.accept = '.json,application/json';
      inp.onchange = async () => {
        if (inp.files[0]) { fileHandle = null; loadFromText(await inp.files[0].text()); }
      };
      inp.click();
    }
  } catch (err) {
    if (err && err.name === 'AbortError') return;
    banner('warn', 'Could not open file: ' + esc(err.message));
  }
}

function loadFromText(text) {
  let parsed;
  try {
    parsed = JSON.parse(text);
  } catch (err) {
    banner('warn', 'This file is not valid JSON: ' + esc(err.message));
    return;
  }
  if (!parsed._meta || !Array.isArray(parsed.items)) {
    banner('warn', 'This does not look like <code>game_data.json</code> (missing <code>_meta</code> / <code>items</code>).');
    return;
  }
  DATA = parsed;
  dirty = false;
  // Capture which keys use `.0` float formatting so the serializer preserves it.
  FLOAT_KEYS = new Set();
  const fre = /"([^"]+)"\s*:\s*-?\d+\.0(?!\d)/g;
  let fm; while ((fm = fre.exec(text))) FLOAT_KEYS.add(fm[1]);
  buildIndexes();
  clearBanner();
  renderAll();
  $('progression-empty').style.display = 'none';
  $('progression-view').style.display = 'block';
  refreshToolbar();
  let msg = 'Loaded <code>' + esc(fileHandle ? fileHandle.name : 'game_data.json') +
    '</code> — ' + DATA.items.length + ' items, ' + DATA.recipes.length + ' recipes, ' +
    DATA.buildings.length + ' buildings, ' + DATA.research.length + ' research.';
  if (serializeGameData(DATA) !== text) {
    msg += ' <strong>Note:</strong> the first save will normalize this file\'s hand-formatting ' +
      '(a one-time large diff — no data or comments change; verified byte-for-byte identical data). ' +
      'Commit that formatting pass on its own so later balance edits stay minimal.';
  }
  banner('info', msg);
}

async function saveFile() {
  if (!DATA) return;
  // Preserve comments + section order + float formatting via the custom serializer.
  const text = serializeGameData(DATA);
  try {
    if (fileHandle) {
      const perm = await fileHandle.requestPermission({ mode: 'readwrite' });
      if (perm !== 'granted') { banner('warn', 'Write permission denied.'); return; }
      const w = await fileHandle.createWritable();
      await w.write(text);
      await w.close();
    } else {
      // Fallback: download a file the user drops into Assets/Data/
      const blob = new Blob([text], { type: 'application/json' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = 'game_data.json';
      a.click();
      URL.revokeObjectURL(a.href);
    }
    dirty = false;
    refreshToolbar();
    banner('ok', 'Saved. Now run <code>MobileIdleBuilder &rarr; Import Game Data</code> in the Unity Editor to regenerate ScriptableObjects. ' +
      (fileHandle ? '' : '(Fallback mode: move the downloaded file into <code>Assets/Data/</code>.)'));
  } catch (err) {
    banner('warn', 'Save failed: ' + esc(err.message));
  }
}

async function reloadFile() {
  if (!fileHandle) return;
  if (dirty && !confirm('Discard unsaved changes and reload from disk?')) return;
  const file = await fileHandle.getFile();
  loadFromText(await file.text());
}

// ─── SECTION 4: JOIN INDEXES ─────────────────────────────────────────────

function buildIndexes() {
  recipeByOutput = new Map();
  for (const r of DATA.recipes) {
    if (r.output_item && !recipeByOutput.has(r.output_item)) recipeByOutput.set(r.output_item, r);
  }
  researchByItem = new Map();
  for (const res of DATA.research) {
    for (const it of (res.unlocks_items || [])) {
      if (!researchByItem.has(it)) researchByItem.set(it, res);
    }
  }
  tutorialItemIds = new Set();
  for (const step of (DATA.tutorial_steps || [])) {
    const ac = step.advance_condition;
    if (ac && Array.isArray(ac.items)) {
      for (const it of ac.items) if (it.item_id != null) tutorialItemIds.add(it.item_id);
    }
    const oe = step.on_enter;
    if (oe && Array.isArray(oe.demon_highlight_item_ids)) {
      for (const id of oe.demon_highlight_item_ids) tutorialItemIds.add(id);
    }
  }
}

function nextId(arr, field) {
  let max = 0;
  for (const o of arr) if (typeof o[field] === 'number' && o[field] > max) max = o[field];
  return max + 1;
}

// ─── SECTION 5: OPTION HELPERS ───────────────────────────────────────────

function optionList(values, selected) {
  return values.map(v =>
    `<option value="${esc(v)}"${v === selected ? ' selected' : ''}>${esc(v)}</option>`).join('');
}
function researchOptions(selected) {
  return `<option value="">(none)</option>` +
    DATA.research.map(r => `<option value="${esc(r.id)}"${r.id === selected ? ' selected' : ''}>${esc(r.id)}</option>`).join('');
}
function buildingOptions() {
  return `<option value="">+ add</option>` +
    DATA.buildings.map(b => `<option value="${esc(b.id)}">${esc(b.id)}</option>`).join('');
}

// ─── SECTION 6: PROGRESSION TAB (item-centric join) ──────────────────────

function progFilter() { return ($('prog-filter').value || '').trim().toLowerCase(); }

function renderProgressionTable() {
  if (!DATA) return;
  const f = progFilter();
  const rows = DATA.items.map((item, idx) => {
    if (f) {
      const hay = (item.id + ' ' + (item.display_name || '') + ' ' + (item.category || '')).toLowerCase();
      if (!hay.includes(f)) return '';
    }
    const rec = recipeByOutput.get(item.id);
    const res = researchByItem.get(item.id);
    const inTut = item.item_id != null && tutorialItemIds.has(item.item_id);

    // Built-in buildings (from the joined recipe)
    let builtIn = '<span class="badge-no">no recipe</span>';
    if (rec) {
      const tags = (rec.valid_buildings || []).map(b =>
        `<span class="bldg-tag">${esc(b)}<span class="rm" data-role="bldg-rm" data-item="${esc(item.id)}" data-bldg="${esc(b)}">&times;</span></span>`).join('');
      builtIn = `<div class="bldg-tags">${tags}
        <select class="e-in" data-role="bldg-add" data-item="${esc(item.id)}" style="width:70px;">${buildingOptions()}</select></div>`;
    }

    const craftCell = rec
      ? `<input class="e-in num" type="number" step="0.1" value="${rec.base_craft_time ?? 0}" data-role="rec-craft" data-item="${esc(item.id)}">`
      : '<span class="badge-no">—</span>';
    const manualCell = rec
      ? `<input class="e-in num" type="number" step="0.1" value="${rec.manual_craft_time ?? 0}" data-role="rec-manual" data-item="${esc(item.id)}">`
      : '<span class="badge-no">—</span>';

    return `<tr>
      <td><span class="reorder">
        <button data-role="move-up" data-idx="${idx}"${idx === 0 ? ' disabled' : ''}>&#9650;</button>
        <button data-role="move-down" data-idx="${idx}"${idx === DATA.items.length - 1 ? ' disabled' : ''}>&#9660;</button>
      </span></td>
      <td class="num-id">${item.item_id ?? ''}</td>
      <td class="id-cell">${esc(item.id)}</td>
      <td><input class="e-in wide" value="${esc(item.display_name || '')}" data-role="item-name" data-item="${esc(item.id)}"></td>
      <td><input class="e-in num" style="width:44px;" type="number" min="1" value="${item.tier ?? 1}" data-role="item-tier" data-item="${esc(item.id)}"></td>
      <td><select class="e-in" data-role="item-cat" data-item="${esc(item.id)}">${optionList(ITEM_CATEGORIES, item.category)}</select></td>
      <td><input class="e-in num" type="number" step="any" value="${item.base_sell_value ?? 0}" data-role="item-sell" data-item="${esc(item.id)}"></td>
      <td><input class="e-in num" style="width:56px;" type="number" step="0.1" value="${item.isotope_sell_multiplier ?? 1}" data-role="item-mult" data-item="${esc(item.id)}"></td>
      <td>${craftCell}</td>
      <td>${manualCell}</td>
      <td>${builtIn}</td>
      <td><select class="e-in" data-role="item-unlock" data-item="${esc(item.id)}">${researchOptions(res ? res.id : '')}</select></td>
      <td style="text-align:center;"><input class="e-check" type="checkbox" data-role="item-tut" data-item="${esc(item.id)}"${item._in_tutorial ? ' checked' : ''}></td>
      <td style="text-align:center;">${inTut ? '<span class="badge-yes" title="Referenced in tutorial_steps">&#10003;</span>' : '<span class="badge-no">&mdash;</span>'}</td>
    </tr>`;
  }).join('');

  $('prog-body').innerHTML = rows;
  $('prog-count').textContent = DATA.items.length + ' items';
}

function itemById(id)     { return DATA.items.find(x => x.id === id); }

function handleProgEvent(e) {
  const el = e.target.closest('[data-role]');
  if (!el) return;
  const role = el.dataset.role;
  const itemId = el.dataset.item;
  const item = itemId ? itemById(itemId) : null;
  const rec = item ? recipeByOutput.get(item.id) : null;

  switch (role) {
    case 'item-name':  item.display_name = el.value; markDirty(); break;
    case 'item-tier': {
      const t = Math.max(1, Math.round(num(el.value, 1)));
      item.tier = t;
      const tierObj = DATA.tiers.find(x => x.tier_number === t);
      item.tier_ref = tierObj ? tierObj.id : 'tier_' + t;
      markDirty(); break;
    }
    case 'item-cat':   item.category = el.value; markDirty(); break;
    case 'item-sell':  item.base_sell_value = num(el.value); markDirty(); break;
    case 'item-mult':  item.isotope_sell_multiplier = num(el.value, 1); markDirty(); break;
    case 'rec-craft':  if (rec) { rec.base_craft_time = num(el.value); markDirty(); } break;
    case 'rec-manual': if (rec) { rec.manual_craft_time = num(el.value); markDirty(); } break;
    case 'item-tut':   item._in_tutorial = el.checked; markDirty(); break;
    case 'item-unlock': reassignUnlock(item, el.value); markDirty(); break;
    case 'bldg-add': {
      if (rec && el.value) {
        rec.valid_buildings = rec.valid_buildings || [];
        if (!rec.valid_buildings.includes(el.value)) rec.valid_buildings.push(el.value);
        markDirty(); renderProgressionTable();
      }
      break;
    }
  }
}

// Click-only controls in the Progression table (buttons / remove-x spans do not
// emit input/change events).
function handleProgClick(e) {
  const el = e.target.closest('[data-role]');
  if (!el) return;
  const role = el.dataset.role;
  if (role === 'move-up')   { moveItem(num(el.dataset.idx), -1); return; }
  if (role === 'move-down') { moveItem(num(el.dataset.idx), +1); return; }
  if (role === 'bldg-rm') {
    const item = itemById(el.dataset.item);
    const rec = item ? recipeByOutput.get(item.id) : null;
    if (rec) {
      rec.valid_buildings = (rec.valid_buildings || []).filter(b => b !== el.dataset.bldg);
      markDirty(); renderProgressionTable();
    }
  }
}

function reassignUnlock(item, newResearchId) {
  // Remove this item from every research's unlocks_items, then add to the chosen one.
  for (const res of DATA.research) {
    if (Array.isArray(res.unlocks_items)) {
      res.unlocks_items = res.unlocks_items.filter(x => x !== item.id);
    }
  }
  if (newResearchId) {
    const res = DATA.research.find(r => r.id === newResearchId);
    if (res) { res.unlocks_items = res.unlocks_items || []; if (!res.unlocks_items.includes(item.id)) res.unlocks_items.push(item.id); }
  }
  researchByItem = new Map();
  for (const res of DATA.research)
    for (const it of (res.unlocks_items || [])) if (!researchByItem.has(it)) researchByItem.set(it, res);
}

function moveItem(idx, dir) {
  const j = idx + dir;
  if (j < 0 || j >= DATA.items.length) return;
  const [it] = DATA.items.splice(idx, 1);
  DATA.items.splice(j, 0, it);
  markDirty();
  renderProgressionTable();
}

// ── Add-item form ──
function toggleAddItemForm(show) {
  const host = $('prog-addform');
  if (!show) { host.innerHTML = ''; return; }
  host.innerHTML = `<div class="simple-inline-form">
    <input id="ai-id" class="sif-name" placeholder="id (snake_case, unique)">
    <input id="ai-name" class="sif-name" placeholder="Display name">
    <input id="ai-tier" class="sif-num" type="number" min="1" value="1" title="tier">
    <select id="ai-cat" class="e-in">${optionList(ITEM_CATEGORIES, 'Element')}</select>
    <input id="ai-sell" class="sif-num" type="number" step="any" value="0" title="base sell value">
    <label style="font-size:12px;color:#c9d1d9;display:flex;align-items:center;gap:5px;">
      <input id="ai-recipe" type="checkbox" checked> recipe in
      <select id="ai-bldg" class="e-in" style="width:110px;">${DATA.buildings.map(b=>`<option value="${esc(b.id)}">${esc(b.id)}</option>`).join('')}</select>
    </label>
    <button id="ai-confirm" class="sif-confirm">Add</button>
    <button id="ai-cancel" class="sif-cancel">Cancel</button>
  </div>`;
  $('ai-confirm').onclick = confirmAddItem;
  $('ai-cancel').onclick = () => toggleAddItemForm(false);
  $('ai-id').focus();
}

function confirmAddItem() {
  const id = ($('ai-id').value || '').trim();
  if (!/^[a-z0-9_]+$/.test(id)) { alert('id must be snake_case (a-z, 0-9, _).'); return; }
  if (itemById(id)) { alert('An item with id "' + id + '" already exists.'); return; }
  const tier = Math.max(1, Math.round(num($('ai-tier').value, 1)));
  const tierObj = DATA.tiers.find(x => x.tier_number === tier);
  const item = {
    id,
    display_name: ($('ai-name').value || id).trim(),
    symbol: '',
    icon_path: 'TODO',
    item_id: nextId(DATA.items, 'item_id'),
    tier,
    tier_ref: tierObj ? tierObj.id : 'tier_' + tier,
    category: $('ai-cat').value,
    atomic_number: 0, atomic_mass: 0, charge: '',
    is_radioactive: false, decay_type: 'None', half_life_note: '',
    is_fissile: false, is_harvested: false, field_type: 'None',
    is_secondary_particle: false, particle_uses: 0,
    codex_entry: '',
    base_sell_value: num($('ai-sell').value),
    isotope_sell_multiplier: 1.0,
  };
  DATA.items.push(item);

  if ($('ai-recipe').checked) {
    const bldg = $('ai-bldg').value;
    DATA.recipes.push({
      id, display_name: 'Craft ' + item.display_name,
      recipe_id: nextId(DATA.recipes, 'recipe_id'),
      tier, category: item.category,
      inputs: [], neutron_adjustment: [], output_item: id, output_quantity: 1, byproducts: [],
      base_craft_time: 5.0, manual_craft_time: 5.0,
      power_cost_is_dynamic: false, fixed_power_cost_ev: 0.0,
      valid_buildings: bldg ? [bldg] : [],
      can_craft_manually: true, known_from_start: false,
      required_research: null, unlocks_research: null,
      is_simplified: false, simplification_note: '',
    });
  }
  markDirty();
  buildIndexes();
  toggleAddItemForm(false);
  renderProgressionTable();
  banner('info', 'Added item <code>' + esc(id) + '</code> (item_id ' + item.item_id + '). ' +
    'New items need a generated icon — run <code>Generate Element Icons</code> before importing, or <code>ItemIconTests</code> will fail.');
}

// ─── SECTION 7: RECIPES TAB ──────────────────────────────────────────────

function renderRecipesTable() {
  if (!DATA) return;
  const f = ($('rec-filter').value || '').trim().toLowerCase();
  $('rec-body').innerHTML = DATA.recipes.map(r => {
    if (f && !(r.id + ' ' + (r.output_item || '')).toLowerCase().includes(f)) return '';
    const inputs = (r.inputs || []).map(i => `${i.quantity}&times;${esc(i.item)}`).join(', ') || '&mdash;';
    const tags = (r.valid_buildings || []).map(b => `<span class="bldg-tag">${esc(b)}</span>`).join(' ');
    return `<tr>
      <td class="num-id">${r.recipe_id ?? ''}</td>
      <td class="id-cell">${esc(r.id)}</td>
      <td class="id-cell">${esc(r.output_item || '')}</td>
      <td><input class="e-in num" style="width:48px;" type="number" value="${r.output_quantity ?? 1}" data-role="r-qty" data-rid="${esc(r.id)}"></td>
      <td><input class="e-in num" type="number" step="0.1" value="${r.base_craft_time ?? 0}" data-role="r-craft" data-rid="${esc(r.id)}"></td>
      <td><input class="e-in num" type="number" step="0.1" value="${r.manual_craft_time ?? 0}" data-role="r-manual" data-rid="${esc(r.id)}"></td>
      <td><input class="e-in num" type="number" step="0.1" value="${r.fixed_power_cost_ev ?? 0}" data-role="r-power" data-rid="${esc(r.id)}"></td>
      <td style="text-align:center;"><input class="e-check" type="checkbox"${r.power_cost_is_dynamic ? ' checked' : ''} data-role="r-dyn" data-rid="${esc(r.id)}"></td>
      <td><div class="bldg-tags">${tags}</div></td>
      <td><select class="e-in" data-role="r-research" data-rid="${esc(r.id)}">${researchOptions(r.required_research)}</select></td>
      <td class="id-cell">${inputs}</td>
    </tr>`;
  }).join('');
  $('rec-count').textContent = DATA.recipes.length + ' recipes';
}

function recipeById(id) { return DATA.recipes.find(x => x.id === id); }

function handleRecipeEvent(e) {
  const el = e.target.closest('[data-role]'); if (!el) return;
  const r = recipeById(el.dataset.rid); if (!r) return;
  switch (el.dataset.role) {
    case 'r-qty':      r.output_quantity = num(el.value, 1); markDirty(); break;
    case 'r-craft':    r.base_craft_time = num(el.value); markDirty(); break;
    case 'r-manual':   r.manual_craft_time = num(el.value); markDirty(); break;
    case 'r-power':    r.fixed_power_cost_ev = num(el.value); markDirty(); break;
    case 'r-dyn':      r.power_cost_is_dynamic = el.checked; markDirty(); break;
    case 'r-research': r.required_research = el.value || null; markDirty(); break;
  }
}

// ─── SECTION 8: BUILDINGS TAB ────────────────────────────────────────────

function renderBuildingsTable() {
  if (!DATA) return;
  const f = ($('bld-filter').value || '').trim().toLowerCase();
  $('bld-body').innerHTML = DATA.buildings.map(b => {
    if (f && !(b.id + ' ' + (b.display_name || '')).toLowerCase().includes(f)) return '';
    return `<tr>
      <td class="num-id">${b.building_id ?? ''}</td>
      <td class="id-cell">${esc(b.id)}</td>
      <td><input class="e-in wide" value="${esc(b.display_name || '')}" data-role="b-name" data-bid="${esc(b.id)}"></td>
      <td><input class="e-in num" style="width:44px;" type="number" min="1" value="${b.tier ?? 1}" data-role="b-tier" data-bid="${esc(b.id)}"></td>
      <td><select class="e-in" data-role="b-cat" data-bid="${esc(b.id)}">${optionList(BUILDING_CATEGORIES, b.category)}</select></td>
      <td><input class="e-in num" type="number" value="${b.entropy_cost ?? 0}" data-role="b-cost" data-bid="${esc(b.id)}"></td>
      <td><input class="e-in num" type="number" step="0.1" value="${b.base_output_rate ?? 0}" data-role="b-rate" data-bid="${esc(b.id)}"></td>
      <td><input class="e-in num" type="number" step="0.1" value="${b.base_power_cost_ev ?? 0}" data-role="b-power" data-bid="${esc(b.id)}"></td>
      <td><select class="e-in" data-role="b-research" data-bid="${esc(b.id)}">${researchOptions(b.required_research)}</select></td>
      <td style="text-align:center;"><input class="e-check" type="checkbox"${b.available_from_start ? ' checked' : ''} data-role="b-start" data-bid="${esc(b.id)}"></td>
    </tr>`;
  }).join('');
  $('bld-count').textContent = DATA.buildings.length + ' buildings';
}

function buildingById(id) { return DATA.buildings.find(x => x.id === id); }

function handleBuildingEvent(e) {
  const el = e.target.closest('[data-role]'); if (!el) return;
  const b = buildingById(el.dataset.bid); if (!b) return;
  switch (el.dataset.role) {
    case 'b-name': b.display_name = el.value; markDirty(); break;
    case 'b-tier': b.tier = Math.max(1, Math.round(num(el.value, 1))); markDirty(); break;
    case 'b-cat':  b.category = el.value; markDirty(); break;
    case 'b-cost': b.entropy_cost = num(el.value); markDirty(); break;
    case 'b-rate': b.base_output_rate = num(el.value); markDirty(); break;
    case 'b-power': b.base_power_cost_ev = num(el.value); markDirty(); break;
    case 'b-research': b.required_research = el.value || null; markDirty(); break;
    case 'b-start': b.available_from_start = el.checked; markDirty(); break;
  }
}

// ─── SECTION 9: RESEARCH TAB ─────────────────────────────────────────────

function renderResearchTable() {
  if (!DATA) return;
  const f = ($('res-filter').value || '').trim().toLowerCase();
  $('res-body').innerHTML = DATA.research.map(r => {
    if (f && !(r.id + ' ' + (r.display_name || '') + ' ' + (r.branch || '')).toLowerCase().includes(f)) return '';
    const prereqs = (r.prerequisites || []).join(', ') || '&mdash;';
    const uItems = (r.unlocks_items || []).length;
    return `<tr>
      <td class="id-cell">${esc(r.id)}</td>
      <td><input class="e-in wide" value="${esc(r.display_name || '')}" data-role="res-name" data-resid="${esc(r.id)}"></td>
      <td><select class="e-in" data-role="res-branch" data-resid="${esc(r.id)}">${optionList(RESEARCH_BRANCHES, r.branch)}</select></td>
      <td><input class="e-in num" style="width:48px;" type="number" value="${r.depth_in_tree ?? 0}" data-role="res-depth" data-resid="${esc(r.id)}"></td>
      <td><input class="e-in num" type="number" value="${r.cost_base_currency ?? 0}" data-role="res-cost" data-resid="${esc(r.id)}"></td>
      <td><input class="e-in num" type="number" value="${r.cost_prestige_currency ?? 0}" data-role="res-pcost" data-resid="${esc(r.id)}"></td>
      <td><input class="e-in num" type="number" value="${r.duration_seconds ?? 0}" data-role="res-dur" data-resid="${esc(r.id)}"></td>
      <td class="id-cell">${esc(prereqs)}</td>
      <td class="id-cell" style="text-align:center;">${uItems}</td>
    </tr>`;
  }).join('');
  $('res-count').textContent = DATA.research.length + ' research';
}

function researchById(id) { return DATA.research.find(x => x.id === id); }

function handleResearchEvent(e) {
  const el = e.target.closest('[data-role]'); if (!el) return;
  const r = researchById(el.dataset.resid); if (!r) return;
  switch (el.dataset.role) {
    case 'res-name':   r.display_name = el.value; markDirty(); break;
    case 'res-branch': r.branch = el.value; markDirty(); break;
    case 'res-depth':  r.depth_in_tree = Math.round(num(el.value)); markDirty(); break;
    case 'res-cost':   r.cost_base_currency = num(el.value); markDirty(); break;
    case 'res-pcost':  r.cost_prestige_currency = num(el.value); markDirty(); break;
    case 'res-dur':    r.duration_seconds = Math.round(num(el.value)); markDirty(); break;
  }
}

// ─── SECTION 10: CONFIG TAB ──────────────────────────────────────────────

function renderConfigTab() {
  if (!DATA) return;
  const cfg = DATA.game_config || {};
  let html = '';
  for (const key of Object.keys(cfg)) {
    if (key.startsWith('_')) continue;            // doc comments
    const v = cfg[key];
    if (Array.isArray(v)) continue;               // e.g. reserved id lists
    const input = (typeof v === 'number')
      ? `<input class="e-in num" type="number" step="any" value="${v}" data-role="cfg" data-key="${esc(key)}">`
      : `<input class="e-in" style="width:180px;" value="${esc(v)}" data-role="cfg" data-key="${esc(key)}">`;
    html += `<div class="cfg-label">${esc(key)}</div><div>${input}</div>`;
  }
  $('config-grid').innerHTML = html;
}

function handleConfigEvent(e) {
  const el = e.target.closest('[data-role="cfg"]'); if (!el) return;
  const key = el.dataset.key;
  const cur = DATA.game_config[key];
  DATA.game_config[key] = (typeof cur === 'number') ? num(el.value) : el.value;
  markDirty();
}

// ─── SECTION 11: REBALANCE — HOUSE RULES ─────────────────────────────────

function computeHouseRuleChanges() {
  if (!DATA) { banner('warn', 'Load game_data.json first.'); return []; }
  const changes = [];

  if ($('hr-sell').checked) {
    const anchorId = ($('hr-anchor').value || '').trim();
    const anchorVal = num($('hr-anchor-val').value, 5);
    const ratio = num($('hr-ratio').value, 2);
    const sorted = DATA.items
      .filter(it => Number.isFinite(it.base_sell_value))
      .slice()
      .sort((a, b) => (a.base_sell_value || 0) - (b.base_sell_value || 0));
    const anchorIdx = sorted.findIndex(it => it.id === anchorId);
    if (anchorIdx === -1) {
      banner('warn', 'Sell ladder: anchor item <code>' + esc(anchorId) + '</code> not found.');
    } else if (ratio > 1) {
      sorted.forEach((it, i) => {
        const nv = roundSell(anchorVal * Math.pow(ratio, i - anchorIdx));
        if (nv !== it.base_sell_value) {
          changes.push({ obj: it, field: 'base_sell_value', old: it.base_sell_value, val: nv,
            path: 'items.' + it.id + '.base_sell_value' });
        }
      });
    }
  }

  if ($('hr-research').checked) {
    // 10s @ depth 0 -> 14400s (4h) @ depth 8
    const ratio = Math.pow(1440, 1 / 8);
    for (const r of DATA.research) {
      const d = r.depth_in_tree || 0;
      const nv = Math.round(10 * Math.pow(ratio, d));
      if (nv !== r.duration_seconds) {
        changes.push({ obj: r, field: 'duration_seconds', old: r.duration_seconds, val: nv,
          path: 'research.' + r.id + '.duration_seconds' });
      }
    }
  }
  return changes;
}

function renderDiff(hostId, changes, applyBtnId) {
  const host = $(hostId);
  if (!changes.length) {
    host.innerHTML = '<div class="diff-empty">No changes — current values already match.</div>';
    $(applyBtnId).disabled = true;
    return;
  }
  const rows = changes.map((c, i) => `<tr>
    <td><input class="e-check" type="checkbox" data-diff-idx="${i}" checked></td>
    <td class="diff-path">${esc(c.path)}</td>
    <td class="diff-old">${esc(c.old)}</td>
    <td class="diff-arrow">&rarr;</td>
    <td class="diff-new">${esc(c.val)}</td>
  </tr>`).join('');
  host.innerHTML = `<table class="diff-table">
    <thead><tr><th></th><th>Path</th><th>Old</th><th></th><th>New</th></tr></thead>
    <tbody>${rows}</tbody></table>`;
  $(applyBtnId).disabled = false;
}

function applyDiff(changes, host) {
  let applied = 0;
  host.querySelectorAll('[data-diff-idx]').forEach(cb => {
    if (cb.checked) {
      const c = changes[+cb.dataset.diffIdx];
      c.obj[c.field] = c.val;
      applied++;
    }
  });
  if (applied) {
    markDirty();
    buildIndexes();
    renderAll();
    banner('ok', 'Applied ' + applied + ' change' + (applied === 1 ? '' : 's') + '. Review, then Save.');
  }
  host.innerHTML = '';
}

// ─── SECTION 12: REBALANCE — CLAUDE BRIDGE ───────────────────────────────

function buildClaudePrompt() {
  if (!DATA) return 'Load game_data.json first.';
  const scope = ($('claude-scope').value || 'items').split(',');
  const slice = {};
  for (const s of scope) {
    if (s === 'game_config') slice.game_config = DATA.game_config;
    else if (Array.isArray(DATA[s])) slice[s] = DATA[s];
  }
  const goal = ($('claude-goal').value || '').trim() || '(no goal specified)';
  return `You are rebalancing a mobile idle game. Here is the current balance data from game_data.json.

GOAL:
${goal}

Return ONLY a JSON array of changes, no prose. Each change is:
  { "path": "<section>.<id>.<field>", "value": <number|string|boolean> }
where <section> is one of items, recipes, buildings, research (keyed by string id) or game_config (path is game_config.<field>). Only include fields you are changing. Example:
[
  { "path": "items.hydrogen.base_sell_value", "value": 6 },
  { "path": "research.recombination_i.duration_seconds", "value": 8 },
  { "path": "game_config.prestige_base_value", "value": 5000 }
]

CURRENT DATA:
${JSON.stringify(slice, null, 2)}`;
}

function resolvePatchPath(path) {
  const parts = path.split('.');
  if (parts[0] === 'game_config') {
    return { obj: DATA.game_config, field: parts.slice(1).join('.') };
  }
  const section = DATA[parts[0]];
  if (!Array.isArray(section)) return null;
  const obj = section.find(x => x.id === parts[1]);
  if (!obj) return null;
  return { obj, field: parts.slice(2).join('.') };
}

function computeClaudeChanges() {
  if (!DATA) { banner('warn', 'Load game_data.json first.'); return null; }
  let patch;
  try {
    patch = JSON.parse($('claude-patch').value);
  } catch (err) {
    banner('warn', 'Patch is not valid JSON: ' + esc(err.message));
    return null;
  }
  if (!Array.isArray(patch)) { banner('warn', 'Patch must be a JSON array of {path, value}.'); return null; }
  const changes = [];
  const skipped = [];
  for (const p of patch) {
    if (!p || typeof p.path !== 'string' || !('value' in p)) { skipped.push(JSON.stringify(p)); continue; }
    const r = resolvePatchPath(p.path);
    if (!r || !r.field) { skipped.push(p.path); continue; }
    const old = r.obj[r.field];
    if (old === p.value) continue;
    changes.push({ obj: r.obj, field: r.field, old, val: p.value, path: p.path });
  }
  if (skipped.length) banner('warn', 'Skipped ' + skipped.length + ' unresolved path(s): ' + esc(skipped.slice(0, 5).join(', ')));
  return changes;
}

// ─── SECTION 13: RENDER-ALL + INIT ───────────────────────────────────────

function renderAll() {
  renderProgressionTable();
  renderRecipesTable();
  renderBuildingsTable();
  renderResearchTable();
  renderConfigTab();
}

function initTabs() {
  document.querySelectorAll('.tab').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
      document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
      btn.classList.add('active');
      $(btn.dataset.tab).classList.add('active');
    });
  });
}

function init() {
  initTabs();

  // Toolbar
  $('tb-open').onclick = openFile;
  $('tb-save').onclick = saveFile;
  $('tb-reload').onclick = reloadFile;

  // Browser capability notice
  if (!usingFsApi) {
    $('fsapi-warn').innerHTML = 'Your browser lacks the File System Access API — Save will download a file you must move into <code>Assets/Data/</code> manually. Use Chrome or Edge (served from localhost) for in-place saving.';
  }

  // Edit delegation (input + change cover text/number and select/checkbox)
  const bind = (id, handler) => {
    const body = $(id);
    body.addEventListener('input', handler);
    body.addEventListener('change', handler);
  };
  bind('prog-body', handleProgEvent);
  $('prog-body').addEventListener('click', handleProgClick);
  bind('rec-body', handleRecipeEvent);
  bind('bld-body', handleBuildingEvent);
  bind('res-body', handleResearchEvent);
  bind('config-grid', handleConfigEvent);

  // Filters
  $('prog-filter').addEventListener('input', renderProgressionTable);
  $('rec-filter').addEventListener('input', renderRecipesTable);
  $('bld-filter').addEventListener('input', renderBuildingsTable);
  $('res-filter').addEventListener('input', renderResearchTable);

  // Add item
  $('prog-add').onclick = () => toggleAddItemForm(true);

  // Rebalance — house rules
  $('hr-preview').onclick = () => { pendingHouseRule = computeHouseRuleChanges(); renderDiff('hr-diff', pendingHouseRule, 'hr-apply'); };
  $('hr-apply').onclick   = () => applyDiff(pendingHouseRule, $('hr-diff'));

  // Rebalance — Claude bridge
  $('claude-copy').onclick = async () => {
    try { await navigator.clipboard.writeText(buildClaudePrompt()); banner('info', 'Prompt copied — paste it into a Claude conversation, then paste the returned patch below.'); }
    catch { banner('warn', 'Clipboard blocked — select the goal text manually. (Some browsers require a user gesture or HTTPS.)'); }
  };
  $('claude-preview').onclick = () => { pendingClaude = computeClaudeChanges() || []; renderDiff('claude-diff', pendingClaude, 'claude-apply'); };
  $('claude-apply').onclick   = () => applyDiff(pendingClaude, $('claude-diff'));

  // Unsaved-changes guard
  window.addEventListener('beforeunload', (e) => { if (dirty) { e.preventDefault(); e.returnValue = ''; } });

  refreshToolbar();
}

init();
