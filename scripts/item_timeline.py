"""Item Timeline: when a player reaches every item, today and in the proposed restructure (2026-11-22).

    python scripts/item_timeline.py            # write docs/item-timeline.html
    python scripts/item_timeline.py --check    # print the summary and check every proposed equation balances

Two views for the editable page (scripts/item_timeline_template.html):
- As built: the game as it is. Unlock order and hours within a run come from progression_curve.analyse() (5 buildings
  on the best recipe); they are laid onto the prestige runs of docs/balance/prestige-pacing.xlsx (Summary sheet:
  active hours and calendar days per run), so an item's time is cumulative active play including every re-climb.
- Restructure: the proposal. Physics climbs H -> Mn by fusion; Uranium then arrives as a mined source and decay,
  fission and neutron work walk it down to Iron, the capstone reached from both ends. Chemistry is life chemistry
  (H, C, N, O, P, S, plus mined salts); metals are parked in a future Metallurgy world. The page spaces each phase's
  items from its pacing parameters (burst, gap growth, finale); the user drags and edits, then saves to the
  artifact's database (`timelines/working`). A later script applies the saved version to game_data.json.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))
import progression_curve as pc  # noqa: E402

TEMPLATE = ROOT / "scripts" / "item_timeline_template.html"
OUT = ROOT / "docs" / "item-timeline.html"
WORKBOOK = ROOT / "docs" / "balance" / "prestige-pacing.xlsx"

# Runs of the prestige plan (fallback when the workbook is missing): target research, active h, days, days with ads.
FALLBACK_RUNS = [
    ("nucleon_harvesting", 1.66, 0.83, 0.57), ("light_elements", 1.73, 0.86, 0.59), ("fusion_ii", 3.33, 1.33, 0.97),
    ("mid_elements", 3.60, 1.44, 1.05), ("mid_elements", 2.89, 0.86, 0.67), ("fusion_iii", 3.29, 0.97, 0.77),
    ("radioactive_isotopes", 4.92, 1.23, 1.00), ("slow_neutron_capture", 5.15, 1.20, 0.98),
]
CHEMISTRY_OPENS_AFTER_RUN = 5   # the five delivery stages, one per run (world_tasks)


def load_runs():
    try:
        import openpyxl
        ws = openpyxl.load_workbook(WORKBOOK, data_only=True)["Summary"]
        rows, started = [], False
        for row in ws.iter_rows(values_only=True):
            if row and row[0] == "Run":
                started = True
                continue
            if started and row and isinstance(row[0], (int, float)) and isinstance(row[2], (int, float)):
                rows.append((row[1], float(row[2]), float(row[7] or 0), float(row[8] or 0)))
        if rows:
            return rows, "docs/balance/prestige-pacing.xlsx"
    except Exception as e:  # noqa: BLE001
        print(f"(workbook not read: {e}; using the built-in run table)")
    return FALLBACK_RUNS, "built-in run table"


# ── As built ────────────────────────────────────────────────────────────────────────────────────────
def as_built(gd):
    order, free, value, items = pc.analyse(gd)
    recipes = {r["id"]: r for r in gd["recipes"]}
    research = {r["id"]: r for r in gd["research"]}
    buildings = {b["id"]: b for b in gd["buildings"]}
    runs, runs_source = load_runs()
    elapsed = {n["id"]: n["elapsed_h"] for n in order}

    # Cumulative active hours / days: an item first reached in run k at in-run hour e sits at
    # sum(active of runs before k) + e / target_k * active_k. Past the last run, the last run's pace continues.
    targets = [(elapsed.get(t, 0.0), a, d, da) for t, a, d, da in runs]

    def place(e):
        cum_a = cum_d = cum_da = 0.0
        for k, (te, a, d, da) in enumerate(targets):
            if e <= te + 1e-9 and te > 0:
                f = e / te
                return cum_a + f * a, cum_d + f * d, cum_da + f * da, k + 1
            cum_a, cum_d, cum_da = cum_a + a, cum_d + d, cum_da + da
        te, a, d, da = targets[-1]
        extra = max(0.0, e - te) / te if te else 0
        return cum_a + extra * a, cum_d + extra * d, cum_da + extra * da, len(targets) + 1

    out, seen = [], set()

    def world_of(rec):
        b = buildings.get((rec.get("valid_buildings") or [""])[0], {})
        it = items.get(rec["output_item"], {})
        return it.get("world") or b.get("world") or ""

    def add(rec_row, node):
        iid = rec_row["out"]
        if iid in seen or iid not in items:
            return
        seen.add(iid)
        rec = recipes.get(rec_row["id"], {})
        e = node["elapsed_h"] if node else 0.0
        h, d, da, run = place(e)
        it = items[iid]
        out.append({
            "id": iid, "name": it.get("display_name", iid), "world": world_of(rec) or "world_physics",
            "phase": (research.get(node["id"], {}).get("block") if node else "ignition") or "ignition",
            "research": node["name"] if node else "Start", "runH": round(e, 3), "h": round(h, 3),
            "days": round(d, 3), "daysAds": round(da, 3), "run": run,
            "value": value.get(iid, 0), "craft": rec_row["time"],
            "inputs": [{"item": i["item"], "q": i["quantity"]} for i in rec.get("inputs") or []],
            "outQ": rec.get("output_quantity") or 1,
            "building": (rec.get("valid_buildings") or [""])[0],
            "source": "mined" if not rec.get("inputs") else "recipe",
        })

    for r in free:
        add(pc_row(r, value), None)
    for node in order:
        for row in node["recipes"]:
            add(row, node)

    # Where Chemistry starts, and how its first item compares with Physics at that moment.
    lab = next(n for n in order if n["id"] == "chemistry_lab")
    water = next(x for x in out if x["id"] == "water")
    best = recipes.get(lab["best_recipe_before"], {})
    best_item = items.get(best.get("output_item", ""), {})
    chem = {
        "h": water["h"], "days": water["days"], "daysAds": water["daysAds"], "afterRun": CHEMISTRY_OPENS_AFTER_RUN,
        "research": lab["name"], "researchCost": lab["cost"], "payback_min": lab["payback_min"],
        "first": {"name": water["name"], "value": water["value"], "outQ": water["outQ"], "craft": water["craft"],
                  "equation": "2 H2 + O2 -> 2 H2O",
                  "rate": (water["value"] * water["outQ"] - sum(value.get(i["item"], 0) * i["q"] for i in water["inputs"]))
                          / water["craft"]},
        "physics": {"name": best_item.get("display_name", best.get("output_item", "?")),
                    "value": value.get(best.get("output_item", ""), 0),
                    "craft": float(best.get("base_craft_time") or 0), "rate": lab["best_rate_before"]},
    }

    cum = []
    a = d = da = 0.0
    for k, (t, ah, dd, dda) in enumerate(runs):
        a, d, da = a + ah, d + dd, da + dda
        cum.append({"n": k + 1, "target": research.get(t, {}).get("display_name", t), "h": round(a, 3),
                    "days": round(d, 3), "daysAds": round(da, 3)})
    return out, cum, chem, runs_source


def pc_row(r, value):
    t = float(r.get("base_craft_time") or 0) or 1.0
    return {"id": r["id"], "out": r["output_item"], "value": value.get(r["output_item"], 0), "time": t}


# ── Restructure (proposal) ──────────────────────────────────────────────────────────────────────────
ATOMS = re.compile(r"([A-Z][a-z]?)(\d*)")


def atoms(formula):
    c = {}
    for el, n in ATOMS.findall(formula):
        c[el] = c.get(el, 0) + int(n or 1)
    return c


# Chemistry sources (mined) and products: id, name, formula, phase, [(qty, input id)], out qty, [(qty, byproduct)],
# building, status. Every product's equation must balance (checked below).
CHEM_SOURCES = [
    ("hydrogen_gas", "Hydrogen Gas", "H2", "keep"), ("oxygen_gas", "Oxygen Gas", "O2", "keep"),
    ("nitrogen_gas", "Nitrogen Gas", "N2", "keep"), ("graphite", "Graphite", "C", "keep"),
    ("sulfur", "Sulfur", "S", "new"), ("phosphorus", "Red Phosphorus", "P", "new"),
    ("halite", "Halite (rock salt)", "NaCl", "new"), ("sylvite", "Sylvite (potash)", "KCl", "new"),
    ("limestone", "Limestone", "CaCO3", "new"),
]
SYN, REACT, ORG, BIO = "molecular_synthesizer", "catalytic_reactor", "organic_synthesizer", "organic_synthesizer"
CHEM = [
    # C1 First Water: the burst right after the gate.
    ("water", "Water", "H2O", "c1", [(2, "hydrogen_gas"), (1, "oxygen_gas")], 2, [], SYN, "keep"),
    ("carbon_dioxide", "Carbon Dioxide", "CO2", "c1", [(1, "graphite"), (1, "oxygen_gas")], 1, [], SYN, "keep"),
    ("methane", "Methane", "CH4", "c1", [(1, "graphite"), (2, "hydrogen_gas")], 1, [], SYN, "keep"),
    ("ammonia", "Ammonia", "NH3", "c1", [(1, "nitrogen_gas"), (3, "hydrogen_gas")], 2, [], SYN, "keep"),
    # C2 Small Molecules
    ("carbon_monoxide", "Carbon Monoxide", "CO", "c2", [(2, "graphite"), (1, "oxygen_gas")], 2, [], SYN, "new"),
    ("hydrogen_peroxide", "Hydrogen Peroxide", "H2O2", "c2", [(1, "hydrogen_gas"), (1, "oxygen_gas")], 1, [], SYN, "new"),
    ("formaldehyde", "Formaldehyde", "CH2O", "c2", [(1, "carbon_monoxide"), (1, "hydrogen_gas")], 1, [], REACT, "new"),
    ("methanol", "Methanol", "CH4O", "c2", [(1, "carbon_monoxide"), (2, "hydrogen_gas")], 1, [], REACT, "new"),
    ("formic_acid", "Formic Acid", "CH2O2", "c2", [(1, "carbon_monoxide"), (1, "water")], 1, [], REACT, "new"),
    ("hydrogen_cyanide", "Hydrogen Cyanide", "HCN", "c2", [(1, "methane"), (1, "ammonia")], 1, [(3, "hydrogen_gas")], REACT, "new"),
    ("urea", "Urea", "CH4N2O", "c2", [(1, "carbon_dioxide"), (2, "ammonia")], 1, [(1, "water")], REACT, "new"),
    ("nitric_acid", "Nitric Acid", "HNO3", "c2", [(1, "ammonia"), (2, "oxygen_gas")], 1, [(1, "water")], REACT, "keep"),
    # C3 Organics
    ("ethylene", "Ethylene", "C2H4", "c3", [(2, "methane")], 1, [(2, "hydrogen_gas")], ORG, "new"),
    ("acetylene", "Acetylene", "C2H2", "c3", [(2, "methane")], 1, [(3, "hydrogen_gas")], ORG, "new"),
    ("ethane", "Ethane", "C2H6", "c3", [(1, "ethylene"), (1, "hydrogen_gas")], 1, [], ORG, "keep"),
    ("ethanol", "Ethanol", "C2H6O", "c3", [(1, "ethylene"), (1, "water")], 1, [], ORG, "keep"),
    ("acetic_acid", "Acetic Acid", "C2H4O2", "c3", [(1, "methanol"), (1, "carbon_monoxide")], 1, [], ORG, "new"),
    ("glycerol", "Glycerol", "C3H8O3", "c3", [(3, "carbon_monoxide"), (4, "hydrogen_gas")], 1, [], ORG, "new"),
    # C4 Phosphorus & Sulfur
    ("hydrogen_sulfide", "Hydrogen Sulfide", "H2S", "c4", [(1, "sulfur"), (1, "hydrogen_gas")], 1, [], SYN, "new"),
    ("sulfuric_acid", "Sulfuric Acid", "H2SO4", "c4", [(2, "sulfur"), (3, "oxygen_gas"), (2, "water")], 2, [], REACT, "keep"),
    ("phosphoric_acid", "Phosphoric Acid", "H3PO4", "c4", [(4, "phosphorus"), (5, "oxygen_gas"), (6, "water")], 4, [], REACT, "new"),
    ("glycerol_phosphate", "Glycerol Phosphate", "C3H9O6P", "c4", [(1, "glycerol"), (1, "phosphoric_acid")], 1, [(1, "water")], ORG, "new"),
    # C5 Building Blocks of Life
    ("glycine", "Glycine", "C2H5NO2", "c5", [(1, "acetic_acid"), (1, "ammonia")], 1, [(1, "hydrogen_gas")], ORG, "new"),
    ("alanine", "Alanine", "C3H7NO2", "c5", [(3, "formaldehyde"), (1, "ammonia")], 1, [(1, "water")], ORG, "new"),
    ("serine", "Serine", "C3H7NO3", "c5", [(1, "glycine"), (1, "formaldehyde")], 1, [], ORG, "new"),
    ("cysteine", "Cysteine", "C3H7NO2S", "c5", [(1, "serine"), (1, "hydrogen_sulfide")], 1, [(1, "water")], ORG, "new"),
    ("ribose", "Ribose", "C5H10O5", "c5", [(5, "formaldehyde")], 1, [], ORG, "new"),
    ("glucose", "Glucose", "C6H12O6", "c5", [(6, "formaldehyde")], 1, [], ORG, "keep"),
    ("adenine", "Adenine", "C5H5N5", "c5", [(5, "hydrogen_cyanide")], 1, [], ORG, "new"),
    ("guanine", "Guanine", "C5H5N5O", "c5", [(5, "hydrogen_cyanide"), (1, "water")], 1, [(1, "hydrogen_gas")], ORG, "new"),
    ("cytosine", "Cytosine", "C4H5N3O", "c5", [(3, "hydrogen_cyanide"), (1, "formaldehyde")], 1, [], ORG, "new"),
    ("palmitic_acid", "Palmitic Acid (fatty acid)", "C16H32O2", "c5", [(8, "acetic_acid"), (14, "hydrogen_gas")], 1, [(14, "water")], ORG, "new"),
    # C6 Molecules of Life: the hard capstones.
    ("dipeptide", "Dipeptide (Gly-Ala)", "C5H10N2O3", "c6", [(1, "glycine"), (1, "alanine")], 1, [(1, "water")], BIO, "new"),
    ("amp", "Nucleotide (AMP)", "C10H14N5O7P", "c6", [(1, "adenine"), (1, "ribose"), (1, "phosphoric_acid")], 1, [(2, "water")], BIO, "new"),
    ("phospholipid", "Phospholipid", "C35H69O8P", "c6", [(1, "glycerol_phosphate"), (2, "palmitic_acid")], 1, [(2, "water")], BIO, "new"),
    ("atp", "ATP", "C10H16N5O13P3", "c6", [(1, "amp"), (2, "phosphoric_acid")], 1, [(2, "water")], BIO, "new"),
]
FORMULA = {s[0]: s[2] for s in CHEM_SOURCES}
FORMULA.update({c[0]: c[2] for c in CHEM})


def check_equations():
    bad = []
    for iid, name, formula, phase, ins, outq, byp, b, st in CHEM:
        left, right = {}, {}
        for q, x in ins:
            for el, n in atoms(FORMULA[x]).items():
                left[el] = left.get(el, 0) + q * n
        for q, x in [(outq, iid)] + byp:
            for el, n in atoms(FORMULA[x]).items():
                right[el] = right.get(el, 0) + q * n
        if left != right:
            bad.append(f"{iid}: {left} -> {right}")
    return bad


def equation(ins, outq, iid, byp):
    side = lambda xs: " + ".join(f"{q} {FORMULA[x]}" if q > 1 else FORMULA[x] for q, x in xs)
    return f"{side(ins)} -> {side([(outq, iid)] + byp)}"


def proposal(gd, built):
    items = {i["id"]: i for i in gd["items"]}
    built_d = {b["id"]: b["daysAds"] for b in built}
    rows = []

    def add(iid, name, world, phase, status, **kw):
        rows.append({"id": iid, "name": name, "world": world, "phase": phase, "status": status,
                     "builtDays": built_d.get(iid), "pinDay": None, **kw})

    # ── Physics: fusion climbs to Mn (Silicon on day 4 opens Chemistry), Uranium walks down to Iron ──
    particles = {"up_quark": "p_ignition", "down_quark": "p_ignition", "electron": "p_ignition",
                 "proton": "p_ignition", "neutron": "p_ignition", "neutrino": "p_light1",
                 "gamma_photon": "p_light1", "alpha_particle": "p_descent", "beta_particle": "p_descent"}
    for iid, ph in particles.items():
        it = items.get(iid)
        if it:
            add(iid, it["display_name"], "world_physics", ph, "keep", z=0, how="particle")

    def phys_phase(z):
        if z <= 2 or z == 6: return "p_ignition"
        if z <= 8: return "p_light1"
        if z <= 14: return "p_light2"
        if z < 26: return "p_light3"
        if z == 26: return "p_iron"
        if 81 <= z <= 92: return "p_descent"
        if z < 81: return "p_metals"
        return "p_deep"

    how_by_phase = {"p_ignition": "atom generator / first fusion", "p_light1": "fusion", "p_light2": "fusion",
                    "p_light3": "fusion", "p_iron": "both ends: fusion and descent", "p_descent": "decay chain",
                    "p_metals": "fission / neutron capture", "p_deep": "transuranic / superheavy"}
    elements = sorted((i for i in gd["items"] if i.get("category") in ("Element", "Isotope")
                       and (i.get("world") or "world_physics") == "world_physics"),
                      key=lambda i: (i.get("atomic_number") or 0, i["id"]))
    for it in elements:
        z = it.get("atomic_number") or 0
        ph_ = phys_phase(z)
        how = "mined source" if it["id"] == "uranium" else how_by_phase[ph_]
        add(it["id"], it["display_name"], "world_physics", ph_,
            "moved" if ph_ in ("p_descent", "p_metals", "p_iron") else "keep", z=z, how=how)
    order_key = {p["id"]: i for i, p in enumerate(PHASES)}
    desc = ("p_descent", "p_metals")
    rows.sort(key=lambda r: (order_key[r["phase"]], 0 if r["id"] == "uranium" else 1,
                             -r["z"] if r["phase"] in desc else r["z"]))

    # ── Chemistry: life chemistry ──
    src_phase = {"hydrogen_gas": "c1", "oxygen_gas": "c1", "nitrogen_gas": "c2", "graphite": "c2",
                 "sulfur": "c4", "phosphorus": "c4", "halite": "c5", "sylvite": "c5", "limestone": "c5"}
    for iid, name, f, st in CHEM_SOURCES:
        add(iid, name, "world_chemistry", src_phase[iid], st, formula=f, how="mined source")
    for iid, name, f, ph_, ins, outq, byp, b, st in CHEM:
        add(iid, name, "world_chemistry", ph_, st, formula=f, how="reaction",
            equation=equation(ins, outq, iid, byp), building=b, inputs=[{"item": x, "q": q} for q, x in ins])
    for iid, why in RETIRED.items():
        it = items.get(iid)
        if it:
            add(iid, it["display_name"], "world_chemistry", "retired", "retire", how=why)

    # ── Biology, Metallurgy, Engineering: fewer items, each a bigger step ──
    for iid, name, ph_, st, how in BIOLOGY + METALLURGY_ITEMS + ENGINEERING:
        world = {"b": "world_biology", "m": "world_metallurgy", "e": "world_engineering"}[ph_[0]]
        it = items.get(iid)
        add(iid, it["display_name"] if it else name, world, ph_, st, how=how)
    return rows


# Biology (fed by the life chemicals), Metallurgy (ores -> metals -> alloys) and Engineering (few, slow parts
# ending in megastructure material). (id, name, phase, status, how)
BIOLOGY = [
    ("protein", "Protein", "b1", "keep", "dipeptides -> protein"),
    ("carbohydrate", "Carbohydrate", "b1", "keep", "glucose chains"),
    ("lipid_membrane", "Lipid Membrane", "b1", "keep", "phospholipid bilayer"),
    ("nucleic_acid", "Nucleic Acid", "b1", "keep", "AMP / nucleotides -> strand"),
    ("ribosome", "Ribosome", "b2", "keep", "protein + nucleic acid"),
    ("cell_membrane", "Cell Membrane", "b2", "keep", "membrane + protein"),
    ("mitochondria", "Mitochondria", "b2", "keep", "protein + membrane + ATP"),
    ("prokaryotic_cell", "Prokaryotic Cell", "b2", "keep", "first cell"),
    ("eukaryotic_cell", "Eukaryotic Cell", "b3", "keep", "cell + mitochondria"),
    ("tissue", "Tissue", "b3", "keep", "cells"),
    ("organ", "Organ", "b3", "keep", "tissues"),
    ("organism", "Organism", "b4", "keep", "organs"),
    ("population", "Population", "b4", "keep", "organisms"),
    ("ecosystem", "Ecosystem", "b4", "keep", "Biology capstone"),
]
METALLURGY_ITEMS = [
    ("hematite", "Hematite (iron ore)", "m1", "new", "mined ore, Fe2O3"),
    ("bauxite", "Bauxite (aluminium ore)", "m1", "new", "mined ore"),
    ("chalcopyrite", "Chalcopyrite (copper ore)", "m1", "new", "mined ore, CuFeS2"),
    ("rutile", "Rutile (titanium ore)", "m1", "new", "mined ore, TiO2"),
    ("silica", "Silica", "m1", "moved", "quartz sand"),
    ("iron_oxide", "Iron Oxide", "m1", "moved", "roasted ore"),
    ("steel", "Steel", "m2", "moved", "iron + carbon"),
    ("aluminium_ingot", "Aluminium Ingot", "m2", "new", "electrolysis of bauxite"),
    ("copper_wire", "Copper Wire", "m2", "new", "smelted chalcopyrite"),
    ("titanium_alloy", "Titanium Alloy", "m2", "moved", "Kroll process"),
    ("carbon_fiber", "Carbon Fiber", "m2", "moved", "graphite fibre"),
    ("semiconductor_wafer", "Semiconductor Wafer", "m3", "moved", "silica + hydrogen gas"),
    ("aerogel", "Aerogel", "m3", "moved", "silica gel, dried"),
    ("superconductor", "Superconductor", "m3", "moved", "alloy + cooling"),
    ("metamaterial", "Metamaterial", "m3", "moved", "Metallurgy capstone"),
]
ENGINEERING = [
    ("circuit_board", "Circuit Board", "e1", "new", "wafer + copper wire"),
    ("quantum_processor", "Quantum Processor", "e1", "moved", "circuit + superconductor"),
    ("plasma_containment_ring", "Plasma Containment Ring", "e1", "moved", "superconducting magnet"),
    ("orbital_frame", "Orbital Frame", "e2", "moved", "titanium + carbon fiber"),
    ("antimatter_cell", "Antimatter Cell", "e2", "moved", "containment + deep physics"),
    ("graviton_lens", "Graviton Lens", "e2", "moved", "metamaterial + processor"),
    ("dyson_node", "Dyson Node", "e3", "moved", "every world's capstone"),
    ("megastructure_material", "Megastructure Material", "e3", "new",
     "earned by Ascending once every item is unlocked; builds the megastructure"),
]
RETIRED = {"octane": "fuel, not life chemistry", "polymer_precursor": "replaced by real monomers (ethylene, acetylene)",
           "amino_acid": "split into glycine, alanine, serine, cysteine", "fatty_acid": "now palmitic acid",
           "nucleotide": "now AMP (and ATP)", "catalyst": "metal catalysts belong to Metallurgy",
           "chlorine_gas": "industrial; drop", "sodium_hydroxide": "industrial; drop",
           "uranium_hexafluoride": "nuclear fuel processing; drop", "table_salt": "now mined halite"}


# Phases, in calendar DAYS for an engaged daily player (the page converts to active hours with its
# "active hours per day" setting). Physics is spread so Silicon lands at the end of day 4, when Chemistry
# opens. Later worlds have fewer items and longer gaps. burstGapH is in active-play hours.
def _ph(id_, world, name, start, end, burst=1, burst_gap_h=1.0, growth=1.1, finale=1.6):
    return {"id": id_, "world": world, "name": name, "start": start, "end": end, "burst": burst,
            "burstGapH": burst_gap_h, "growth": growth, "finale": finale}


PHASES = [
    _ph("p_ignition", "world_physics", "Ignition (tutorial)", 0.0, 0.45, 6, 0.08, 1.1, 1.3),
    _ph("p_light1", "world_physics", "First Fusion: Li to O", 0.45, 1.5, 2, 0.5, 1.15, 1.6),
    _ph("p_light2", "world_physics", "Stellar Forge: F to Si", 1.5, 4.0, 1, 1.0, 1.15, 1.8),
    _ph("p_light3", "world_physics", "Deep Burning: P to Mn", 4.0, 7.5, 1, 1.0, 1.12, 1.6),
    _ph("p_descent", "world_physics", "Actinide Descent: U to Pb", 7.5, 12.0, 2, 1.0, 1.08, 1.5),
    _ph("p_metals", "world_physics", "Metal Descent: Pb to Co", 12.0, 22.0, 2, 1.0, 1.03, 1.8),
    _ph("p_iron", "world_physics", "Iron: both ends meet", 22.0, 23.0, 0, 0, 1.0, 1.0),
    _ph("p_deep", "world_physics", "Deep Physics: transuranics (feeds Engineering)", 40.0, 75.0, 1, 4.0, 1.06, 1.8),
    _ph("c1", "world_chemistry", "C1 First Water", 4.0, 4.6, 3, 0.15, 1.15, 1.3),
    _ph("c2", "world_chemistry", "C2 Small Molecules", 4.6, 8.0, 2, 1.0, 1.15, 1.6),
    _ph("c3", "world_chemistry", "C3 Organics", 8.0, 12.0, 1, 1.5, 1.15, 1.6),
    _ph("c4", "world_chemistry", "C4 Phosphorus & Sulfur", 12.0, 17.0, 1, 2.0, 1.15, 1.7),
    _ph("c5", "world_chemistry", "C5 Building Blocks of Life", 17.0, 24.0, 1, 2.0, 1.12, 1.8),
    _ph("c6", "world_chemistry", "C6 Molecules of Life", 24.0, 30.0, 0, 0, 1.3, 2.0),
    _ph("retired", "world_chemistry", "Retired from Chemistry", None, None),
    _ph("b1", "world_biology", "B1 Biomolecules", 30.0, 35.0, 1, 3.0, 1.15, 1.5),
    _ph("b2", "world_biology", "B2 Cells", 35.0, 42.0, 1, 4.0, 1.15, 1.7),
    _ph("b3", "world_biology", "B3 Multicellular", 42.0, 50.0, 0, 0, 1.2, 1.8),
    _ph("b4", "world_biology", "B4 Ecosystems", 50.0, 58.0, 0, 0, 1.25, 2.0),
    _ph("m1", "world_metallurgy", "M1 Ores", 58.0, 62.0, 2, 2.0, 1.1, 1.4),
    _ph("m2", "world_metallurgy", "M2 Smelting & Alloys", 62.0, 70.0, 1, 4.0, 1.15, 1.7),
    _ph("m3", "world_metallurgy", "M3 Advanced Materials", 70.0, 78.0, 0, 0, 1.25, 2.0),
    _ph("e1", "world_engineering", "E1 Components", 78.0, 83.0, 0, 0, 1.2, 1.6),
    _ph("e2", "world_engineering", "E2 Systems", 83.0, 88.0, 0, 0, 1.25, 1.8),
    _ph("e3", "world_engineering", "E3 Dyson Node and first Ascension", 88.0, 91.0, 0, 0, 1.0, 1.5),
]
WORLDS = [
    {"id": "world_physics", "name": "Physics"}, {"id": "world_chemistry", "name": "Chemistry"},
    {"id": "world_biology", "name": "Biology"}, {"id": "world_metallurgy", "name": "Metallurgy"},
    {"id": "world_engineering", "name": "Engineering"},
]

# Idle-Planet-Miner walls: the next research costs far more than one run earns, so the player prestiges at
# the same spot several times, buying the named upgrades, until a run breaks through.
WALLS = [
    {"before": "oxygen", "repeats": 2, "breaks": "Assembly Line 2, Memory Resonance 1",
     "note": "first wall: teaches that prestiging is progress"},
    {"before": "sodium", "repeats": 3, "breaks": "Quantum Yield 2, Entropy Headstart 3",
     "note": "delivery stages 3-4 land around here"},
    {"before": "silicon", "repeats": 3, "breaks": "Assembly Line 4, Memory Resonance 3",
     "note": "Silicon is delivery stage 5: Chemistry opens"},
    {"before": "titanium", "repeats": 3, "breaks": "Research Overdrive 3, Entropy Echo 1", "note": ""},
    {"before": "lead", "repeats": 4, "breaks": "Fission Yield 2", "note": "the descent's first big wall"},
    {"before": "copper", "repeats": 5, "breaks": "Neutron Economy 3, Quantum Yield 5", "note": ""},
    {"before": "iron", "repeats": 4, "breaks": "Fission Yield 5, Neutron Economy 5", "note": "Physics capstone"},
    {"before": "hydrogen_cyanide", "repeats": 3, "breaks": "Spring Pressure 2",
     "note": "Chemistry walls run alongside Physics"},
    {"before": "phosphoric_acid", "repeats": 4, "breaks": "Catalysis 3", "note": ""},
    {"before": "palmitic_acid", "repeats": 5, "breaks": "Catalysis 5, Spring Pressure 4", "note": ""},
    {"before": "atp", "repeats": 4, "breaks": "Catalysis 7", "note": "Chemistry capstone"},
    {"before": "prokaryotic_cell", "repeats": 5, "breaks": "Growth Media 3", "note": ""},
    {"before": "ecosystem", "repeats": 6, "breaks": "Growth Media 6, Entropy Echo 4", "note": "Biology capstone"},
    {"before": "titanium_alloy", "repeats": 5, "breaks": "Furnace Heat 3", "note": ""},
    {"before": "metamaterial", "repeats": 6, "breaks": "Furnace Heat 6, Precision 1", "note": ""},
    {"before": "graviton_lens", "repeats": 6, "breaks": "Precision 4", "note": ""},
    {"before": "dyson_node", "repeats": 8, "breaks": "every track near max",
     "note": "the last wall before the first Ascension"},
]

# Prestige shop plan: today's upgrades plus one track per world, each priced so its walls fall at the planned
# repeats. Costs are prestige currency per level.
UPGRADES = [
    {"id": "assembly_line", "name": "Assembly Line", "effect": "+10% craft speed / level", "levels": 5,
     "costs": "15, 30, 60, 120, 240", "status": "keep", "world": "All", "walls": "Oxygen, Silicon"},
    {"id": "memory_resonance", "name": "Memory Resonance", "effect": "-5% research cost / level", "levels": 5,
     "costs": "10, 20, 40, 80, 160", "status": "keep", "world": "All", "walls": "Oxygen, Silicon"},
    {"id": "quantum_yield", "name": "Quantum Yield", "effect": "+10% output / level", "levels": 10,
     "costs": "40, 80, 160, 320, 640, 1.3k, 2.6k, 5k, 10k, 20k", "status": "extend (5 -> 10)",
     "world": "All", "walls": "Sodium, Copper"},
    {"id": "entropy_headstart", "name": "Entropy Headstart", "effect": "start with more entropy (x4 per level)",
     "levels": 10, "costs": "5 ... 2.5k", "status": "extend (5 -> 10)", "world": "All", "walls": "Sodium"},
    {"id": "research_overdrive", "name": "Research Overdrive", "effect": "-5% research timer / level",
     "levels": 10, "costs": "30 ... 2.8k", "status": "keep", "world": "All", "walls": "Titanium"},
    {"id": "entropy_echo", "name": "Entropy Echo", "effect": "+5% prestige currency / level", "levels": 8,
     "costs": "200, 600, 1.8k, 5.4k, 16k, 49k, 146k, 437k", "status": "extend (4 -> 8)", "world": "All",
     "walls": "Titanium, Ecosystem"},
    {"id": "fission_yield", "name": "Fission Yield", "effect": "+15% decay and fission output / level",
     "levels": 6, "costs": "300 ... 10k", "status": "new", "world": "Physics", "walls": "Lead, Iron"},
    {"id": "neutron_economy", "name": "Neutron Economy", "effect": "-10% neutrons per capture / level",
     "levels": 5, "costs": "800 ... 13k", "status": "new", "world": "Physics", "walls": "Copper, Iron"},
    {"id": "spring_pressure", "name": "Spring Pressure", "effect": "+20% Spring flow / level", "levels": 6,
     "costs": "250 ... 8k", "status": "new", "world": "Chemistry", "walls": "HCN, Palmitic Acid"},
    {"id": "catalysis", "name": "Catalysis", "effect": "+15% Chemistry craft speed / level", "levels": 8,
     "costs": "600 ... 40k", "status": "new", "world": "Chemistry", "walls": "Phosphoric Acid, Palmitic Acid, ATP"},
    {"id": "growth_media", "name": "Growth Media", "effect": "+15% Biology output / level", "levels": 8,
     "costs": "5k ... 300k", "status": "new", "world": "Biology", "walls": "Prokaryotic Cell, Ecosystem"},
    {"id": "furnace_heat", "name": "Furnace Heat", "effect": "+15% Metallurgy speed / level", "levels": 8,
     "costs": "40k ... 2.5M", "status": "new", "world": "Metallurgy", "walls": "Titanium Alloy, Metamaterial"},
    {"id": "precision", "name": "Precision", "effect": "+10% Engineering output / level", "levels": 8,
     "costs": "250k ... 16M", "status": "new", "world": "Engineering", "walls": "Metamaterial, Graviton Lens"},
]

# Ascension: once every item is unlocked, Ascend (worlds lock again; items, research and prestige currency
# reset; shop upgrades stay) for megastructure material. Each megastructure stage costs more material and
# adds a permanent production bonus, so each loop is faster. The page projects this over years.
ASCENSION = {"firstLoopDays": 91, "loopShrink": 0.82, "floorDays": 21, "materialFirst": 10,
             "materialGrowth": 1.18, "stageCostFirst": 10, "stageCostGrowth": 1.55, "stageBonusPct": 10,
             "years": 4}


def main():
    gd = pc.load()
    bad = check_equations()
    if bad:
        sys.exit("unbalanced equations:\n  " + "\n  ".join(bad))
    built, runs, chem, runs_source = as_built(gd)
    prop = proposal(gd, built)
    data = {
        "generated": __import__("datetime").date.today().isoformat(),
        "runsSource": runs_source, "runs": runs, "chemStart": chem, "worlds": WORLDS, "phases": PHASES,
        "walls": WALLS, "upgrades": UPGRADES, "ascension": ASCENSION, "activeHoursPerDay": 2.0,
        "asBuilt": built, "proposal": prop,
    }
    if "--check" in sys.argv:
        print(f"as built: {len(built)} items; proposal: {len(prop)} rows; runs: {len(runs)} ({runs_source})")
        print(f"Chemistry opens at {chem['h']:.1f} active h / {chem['days']:.1f} days, after run {chem['afterRun']}")
        print(f"  first item {chem['first']['name']}: {chem['first']['equation']}, {chem['first']['craft']:.0f}s,"
              f" {chem['first']['rate']:.1f} e/s per building")
        print(f"  best Physics then: {chem['physics']['name']} at {chem['physics']['rate']:.1f} e/s per building")
        print(f"all {len(CHEM)} Chemistry equations balance")
        return
    html = TEMPLATE.read_text(encoding="utf-8").replace("/*DATA*/null", json.dumps(data, ensure_ascii=False))
    OUT.write_text(html, encoding="utf-8", newline="\n")
    print(OUT)


if __name__ == "__main__":
    main()
