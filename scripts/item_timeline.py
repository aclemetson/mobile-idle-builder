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

# Chemistry items today that leave Chemistry: metals and industry go to Metallurgy; retired ones go away.
METALLURGY = ["silica", "iron_oxide", "steel", "carbon_fiber", "titanium_alloy", "semiconductor_wafer", "aerogel",
              "superconductor", "metamaterial", "catalyst", "chlorine_gas", "sodium_hydroxide", "uranium_hexafluoride",
              "table_salt"]
RETIRED = {"octane": "fuel, not life chemistry", "polymer_precursor": "replaced by real monomers (ethylene, acetylene)",
           "amino_acid": "split into glycine, alanine, serine, cysteine", "fatty_acid": "now palmitic acid",
           "nucleotide": "now AMP (and ATP)"}


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
    built_h = {b["id"]: b["h"] for b in built}
    rows = []

    def add(iid, name, world, phase, status, **kw):
        rows.append({"id": iid, "name": name, "world": world, "phase": phase, "status": status,
                     "builtH": built_h.get(iid), "pinH": None, **kw})

    # Physics
    particles = {"up_quark": "ignition", "down_quark": "ignition", "electron": "ignition", "proton": "ignition",
                 "neutron": "ignition", "neutrino": "light", "gamma_photon": "light",
                 "alpha_particle": "descent", "beta_particle": "descent"}
    for iid, ph in particles.items():
        it = items.get(iid)
        if it:
            add(iid, it["display_name"], "world_physics", ph, "keep", z=0, how="particle")
    elements = sorted((i for i in gd["items"] if i.get("category") in ("Element", "Isotope")
                       and (i.get("world") or "world_physics") == "world_physics"),
                      key=lambda i: (i.get("atomic_number") or 0, i["id"]))
    for it in elements:
        z = it.get("atomic_number") or 0
        iid = it["id"]
        if z <= 2:
            add(iid, it["display_name"], "world_physics", "ignition", "keep", z=z, how="atom generator")
        elif z == 6:
            add(iid, it["display_name"], "world_physics", "ignition", "keep", z=z, how="fusion (tutorial end)")
        elif z < 26:
            add(iid, it["display_name"], "world_physics", "light", "keep", z=z, how="fusion")
        elif z == 26:
            add(iid, it["display_name"], "world_physics", "iron", "moved", z=z, how="both ends: fusion and descent")
        elif z <= 92:
            how = "mined source" if iid == "uranium" else ("decay chain" if z >= 81 else "fission / neutron capture")
            add(iid, it["display_name"], "world_physics", "descent" if z >= 81 else "metals_descent", "moved", z=z, how=how)
        else:
            add(iid, it["display_name"], "world_physics", "act4", "keep", z=z, how="transuranic / superheavy")
    for it in gd["items"]:
        if it.get("category") == "Component":
            add(it["id"], it["display_name"], "world_physics", "act4", "keep", z=0, how="component")
    # Descent runs from Uranium (92) down toward Iron: order by Z descending inside those phases.
    order_key = {"ignition": 0, "light": 1, "descent": 2, "metals_descent": 3, "iron": 4, "act4": 5}
    phys = sorted([r for r in rows], key=lambda r: (order_key[r["phase"]],
                  -r["z"] if r["phase"] in ("descent", "metals_descent") else r["z"]))
    # Uranium first in its phase (the source), then its isotope, then the chain.
    phys.sort(key=lambda r: (order_key[r["phase"]], 0 if r["id"] == "uranium" else 1,
                             -r["z"] if r["phase"] in ("descent", "metals_descent") else r["z"]))
    rows = phys

    # Chemistry
    for iid, name, f, st in CHEM_SOURCES:
        add(iid, name, "world_chemistry", "c1" if iid in ("hydrogen_gas", "oxygen_gas") else
            ("c4" if iid in ("sulfur", "phosphorus") else ("c2" if iid in ("nitrogen_gas", "graphite") else "c5")),
            st, formula=f, how="mined source")
    for iid, name, f, ph, ins, outq, byp, b, st in CHEM:
        add(iid, name, "world_chemistry", ph, st, formula=f, how="reaction", equation=equation(ins, outq, iid, byp),
            building=b, inputs=[{"item": x, "q": q} for q, x in ins])
    # Metallurgy (parked) and retired
    for iid in METALLURGY:
        it = items.get(iid)
        if it:
            add(iid, it["display_name"], "world_metallurgy", "parked", "parked", how="moves to Metallurgy")
    for iid, why in RETIRED.items():
        it = items.get(iid)
        if it:
            add(iid, it["display_name"], "world_chemistry", "retired", "retire", how=why)
    # Biology (unchanged)
    for it in gd["items"]:
        if (it.get("world") or "") == "world_biology":
            add(it["id"], it["display_name"], "world_biology", "bio", "keep", how="biology (unchanged)")
    return rows


# Phases of the proposal: world, name, window in cumulative active hours, pacing. The page re-spaces items
# inside each window from these numbers; the user edits them there.
PHASES = [
    {"id": "ignition", "world": "world_physics", "name": "Ignition (tutorial)", "start": 0.0, "end": 1.0, "burst": 5,
     "burstGapMin": 3, "growth": 1.12, "finale": 1.5},
    {"id": "light", "world": "world_physics", "name": "Light Elements: fusion to Mn", "start": 1.0, "end": 9.0, "burst": 3,
     "burstGapMin": 8, "growth": 1.08, "finale": 1.8},
    {"id": "descent", "world": "world_physics", "name": "Actinide Descent: U to Pb", "start": 9.0, "end": 14.0, "burst": 3,
     "burstGapMin": 6, "growth": 1.08, "finale": 1.6},
    {"id": "metals_descent", "world": "world_physics", "name": "Metal Descent: Pb to Co", "start": 14.0, "end": 24.0,
     "burst": 3, "burstGapMin": 8, "growth": 1.03, "finale": 1.8},
    {"id": "iron", "world": "world_physics", "name": "Iron: both ends meet", "start": 24.0, "end": 26.0, "burst": 0,
     "burstGapMin": 0, "growth": 1.0, "finale": 1.0},
    {"id": "act4", "world": "world_physics", "name": "Act IV: transuranics to megastructure (after Chemistry)",
     "start": 50.0, "end": 140.0, "burst": 2, "burstGapMin": 30, "growth": 1.03, "finale": 1.8},
    {"id": "c1", "world": "world_chemistry", "name": "C1 First Water", "start": 13.2, "end": 14.0, "burst": 4,
     "burstGapMin": 4, "growth": 1.1, "finale": 1.2},
    {"id": "c2", "world": "world_chemistry", "name": "C2 Small Molecules", "start": 14.0, "end": 19.0, "burst": 3,
     "burstGapMin": 10, "growth": 1.12, "finale": 1.6},
    {"id": "c3", "world": "world_chemistry", "name": "C3 Organics", "start": 19.0, "end": 25.0, "burst": 2,
     "burstGapMin": 15, "growth": 1.12, "finale": 1.6},
    {"id": "c4", "world": "world_chemistry", "name": "C4 Phosphorus & Sulfur", "start": 25.0, "end": 31.0, "burst": 2,
     "burstGapMin": 20, "growth": 1.12, "finale": 1.7},
    {"id": "c5", "world": "world_chemistry", "name": "C5 Building Blocks of Life", "start": 31.0, "end": 42.0, "burst": 2,
     "burstGapMin": 20, "growth": 1.1, "finale": 1.8},
    {"id": "c6", "world": "world_chemistry", "name": "C6 Molecules of Life", "start": 42.0, "end": 50.0, "burst": 0,
     "burstGapMin": 0, "growth": 1.25, "finale": 2.0},
    {"id": "bio", "world": "world_biology", "name": "Biology (unchanged)", "start": 50.0, "end": 90.0, "burst": 2,
     "burstGapMin": 30, "growth": 1.08, "finale": 1.6},
    {"id": "parked", "world": "world_metallurgy", "name": "Metallurgy (future world, not built)", "start": None,
     "end": None},
    {"id": "retired", "world": "world_chemistry", "name": "Retired from Chemistry", "start": None, "end": None},
]
WORLDS = [
    {"id": "world_physics", "name": "Physics"}, {"id": "world_chemistry", "name": "Chemistry"},
    {"id": "world_metallurgy", "name": "Metallurgy (parked)"}, {"id": "world_biology", "name": "Biology"},
]


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
