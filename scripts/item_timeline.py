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
    ("native_sulfur", "Native Sulfur", "S", "new"), ("red_phosphorus", "Red Phosphorus", "P", "new"),
    ("halite", "Halite (rock salt)", "NaCl", "new"), ("sylvite", "Sylvite (potash)", "KCl", "new"),
    ("limestone", "Limestone", "CaCO3", "new"),
]
SYN, REACT, ORG, BIO = "molecular_synthesizer", "catalytic_reactor", "organic_synthesizer", "organic_synthesizer"
COMP = "compound_synthesizer"     # the mineral acids (nitric, sulfuric, phosphoric)
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
    ("nitric_acid", "Nitric Acid", "HNO3", "c2", [(1, "ammonia"), (2, "oxygen_gas")], 1, [(1, "water")], COMP, "keep"),
    # C3 Organics
    ("ethylene", "Ethylene", "C2H4", "c3", [(2, "methane")], 1, [(2, "hydrogen_gas")], ORG, "new"),
    ("acetylene", "Acetylene", "C2H2", "c3", [(2, "methane")], 1, [(3, "hydrogen_gas")], ORG, "new"),
    ("ethane", "Ethane", "C2H6", "c3", [(1, "ethylene"), (1, "hydrogen_gas")], 1, [], ORG, "keep"),
    ("ethanol", "Ethanol", "C2H6O", "c3", [(1, "ethylene"), (1, "water")], 1, [], ORG, "keep"),
    ("acetic_acid", "Acetic Acid", "C2H4O2", "c3", [(1, "methanol"), (1, "carbon_monoxide")], 1, [], ORG, "new"),
    ("glycerol", "Glycerol", "C3H8O3", "c3", [(3, "carbon_monoxide"), (4, "hydrogen_gas")], 1, [], ORG, "new"),
    # C4 Phosphorus & Sulfur
    ("hydrogen_sulfide", "Hydrogen Sulfide", "H2S", "c4", [(1, "native_sulfur"), (1, "hydrogen_gas")], 1, [], SYN, "new"),
    ("sulfuric_acid", "Sulfuric Acid", "H2SO4", "c4", [(2, "native_sulfur"), (3, "oxygen_gas"), (2, "water")], 2, [], COMP, "keep"),
    ("phosphoric_acid", "Phosphoric Acid", "H3PO4", "c4", [(4, "red_phosphorus"), (5, "oxygen_gas"), (6, "water")], 4, [], COMP, "new"),
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
    # Rows follow PHYS_NODES (the research nodes the game will get), so the timeline and the game agree.
    def add_item(iid, ph_, node, how, status="keep"):
        it = items.get(iid)
        if it:
            add(iid, it["display_name"], "world_physics", ph_, status, z=it.get("atomic_number") or 0,
                how=how, node=node)
    for iid in ("up_quark", "down_quark", "electron", "proton", "neutron", "deuterium", "hydrogen", "helium_4",
                "carbon"):
        add_item(iid, "p_ignition", None, "tutorial")
    order_key = {p["id"]: i for i, p in enumerate(PHASES)}
    for node, name, ph_, iids, *_ in phys_nodes(items):
        for iid in iids:
            how = {"p_descent": "decay chain", "p_metals": "spallation", "p_iron": "both ends meet",
                   "p_deep": "transuranic / superheavy"}.get(ph_, "fusion")
            add_item(iid, ph_, node, "mined source" if iid == "uranium" else how,
                     "moved" if ph_ in ("p_descent", "p_metals", "p_iron") else "keep")
    for ph_, iids in (("p_light1", ("neutrino", "gamma_photon")), ("p_descent", ("alpha_particle", "beta_particle"))):
        for iid in iids:
            add_item(iid, ph_, None, "byproduct particle")
    rows.sort(key=lambda r: order_key[r["phase"]])     # stable: keeps node order inside a phase

    # ── Chemistry: life chemistry ──
    src_phase = {"hydrogen_gas": "c1", "oxygen_gas": "c1", "nitrogen_gas": "c1", "graphite": "c1",
                 "native_sulfur": "c4", "red_phosphorus": "c4", "halite": "c5", "sylvite": "c5", "limestone": "c5"}
    for iid, name, f, st in CHEM_SOURCES:
        add(iid, name, "world_chemistry", src_phase[iid], st, formula=f, how="mined source")
    for iid, name, f, ph_, ins, outq, byp, b, st in CHEM:
        add(iid, name, "world_chemistry", ph_, st, formula=f, how="reaction",
            equation=equation(ins, outq, iid, byp), building=b, inputs=[{"item": x, "q": q} for q, x in ins])
    # Chemistry rows follow CHEM_NODES (the research nodes the game gets), like Physics follows phys_nodes.
    node_ix = {iid: (k, n[0]) for k, n in enumerate(CHEM_NODES) for iid in n[3]}
    chem = [r for r in rows if r["world"] == "world_chemistry"]
    rest = [r for r in rows if r["world"] != "world_chemistry"]
    for r in chem:
        r["node"] = node_ix[r["id"]][1]
    chem.sort(key=lambda r: node_ix[r["id"]][0])
    rows[:] = rest + chem
    for iid, why in RETIRED.items():
        it = items.get(iid)
        if it:
            add(iid, it["display_name"], "world_chemistry", "retired", "retire", how=why)

    # ── Biology, Metallurgy, Engineering: fewer items, each a bigger step ──
    for iid, name, ph_, st, how in BIOLOGY + METALLURGY_ITEMS + ENGINEERING:
        world = {"b": "world_biology", "m": "world_metallurgy", "e": "world_engineering"}[ph_[0]]
        it = items.get(iid)
        add(iid, it["display_name"] if it else name, world, ph_, st, how=how)
    attach_recipes(gd, rows, built)
    return rows


# Recipes for the later worlds in the proposal: (inputs [(q, item)], craft seconds, output quantity).
# Worlds stay self-contained except Engineering, which imports Metallurgy, Biology and Chemistry products.
LATER_RECIPES = {
    "protein": ([(3, "dipeptide")], 60), "carbohydrate": ([(3, "glucose")], 60),
    "lipid_membrane": ([(3, "phospholipid")], 60), "nucleic_acid": ([(3, "amp"), (1, "atp")], 75),
    "ribosome": ([(2, "protein"), (1, "nucleic_acid")], 90), "cell_membrane": ([(2, "lipid_membrane"), (1, "protein")], 90),
    "mitochondria": ([(2, "protein"), (1, "lipid_membrane"), (2, "atp")], 90),
    "prokaryotic_cell": ([(1, "ribosome"), (1, "mitochondria"), (1, "cell_membrane")], 120),
    "eukaryotic_cell": ([(1, "prokaryotic_cell"), (1, "mitochondria")], 150), "tissue": ([(2, "eukaryotic_cell")], 420),
    "organ": ([(2, "tissue")], 600), "organism": ([(2, "organ")], 900), "population": ([(2, "organism")], 1200),
    "ecosystem": ([(3, "population")], 1800),
    "hematite": ([], 60), "bauxite": ([], 75), "chalcopyrite": ([], 90), "rutile": ([], 120), "silica": ([], 45),
    "coal": ([], 60), "iron_oxide": ([(2, "hematite")], 90), "steel": ([(3, "iron_oxide"), (1, "coal")], 300),
    "aluminium_ingot": ([(2, "bauxite")], 240), "copper_wire": ([(2, "chalcopyrite")], 240),
    "titanium_alloy": ([(2, "rutile"), (1, "aluminium_ingot")], 480), "carbon_fiber": ([(4, "coal")], 400),
    "semiconductor_wafer": ([(4, "silica")], 600), "aerogel": ([(3, "silica")], 700),
    "superconductor": ([(2, "copper_wire"), (1, "titanium_alloy")], 900),
    "metamaterial": ([(2, "superconductor"), (2, "semiconductor_wafer")], 1200),
    "circuit_board": ([(1, "semiconductor_wafer"), (2, "copper_wire")], 1800),
    "quantum_processor": ([(2, "circuit_board"), (1, "superconductor")], 2400),
    "plasma_containment_ring": ([(2, "superconductor"), (2, "titanium_alloy")], 2400),
    "orbital_frame": ([(3, "titanium_alloy"), (2, "carbon_fiber"), (2, "steel")], 3600),
    "antimatter_cell": ([(1, "plasma_containment_ring"), (2, "aerogel")], 3600),
    "graviton_lens": ([(2, "metamaterial"), (1, "quantum_processor")], 5400),
    "dyson_node": ([(1, "graviton_lens"), (1, "orbital_frame"), (1, "antimatter_cell"), (1, "ecosystem")], 7200),
}
# The Chemistry research nodes, in order: (id, display name, phase, items, buildings it unlocks, fields it sells).
# chemistry_lab, molecular_synthesis and nitrogen_fixation keep their ids (the Chemistry tutorial uses the first).
CHEM_NODES = [
    ("chemistry_lab", "Chemistry Lab", "c1", ["hydrogen_gas", "oxygen_gas", "water"],
     ["element_harvester", "molecular_synthesizer"], ["hydrogen_gas_field", "oxygen_gas_field"]),
    ("molecular_synthesis", "Carbon Chemistry", "c1", ["graphite", "carbon_dioxide", "methane"], [], ["graphite_field"]),
    ("nitrogen_fixation", "Nitrogen Fixation", "c1", ["nitrogen_gas", "ammonia"], [], ["nitrogen_gas_field"]),
    ("syngas", "Syngas", "c2", ["carbon_monoxide", "hydrogen_peroxide"], [], []),
    ("carbonyl_chemistry", "Carbonyl Chemistry", "c2", ["formaldehyde", "methanol", "formic_acid"],
     ["catalytic_reactor"], []),
    ("prebiotic_nitrogen", "Prebiotic Nitrogen", "c2", ["hydrogen_cyanide", "urea", "nitric_acid"],
     ["compound_synthesizer"], []),
    ("hydrocarbons", "Hydrocarbons", "c3", ["ethylene", "acetylene", "ethane"], ["organic_synthesizer"], []),
    ("alcohols_and_acids", "Alcohols and Acids", "c3", ["ethanol", "acetic_acid", "glycerol"], [], []),
    ("sulfur_chemistry", "Sulfur Chemistry", "c4", ["native_sulfur", "hydrogen_sulfide", "sulfuric_acid"], [],
     ["sulfur_vent_field"]),
    ("phosphorus_chemistry", "Phosphorus Chemistry", "c4", ["red_phosphorus", "phosphoric_acid", "glycerol_phosphate"],
     [], ["phosphate_rock_field"]),
    ("mineral_salts", "Mineral Salts", "c5", ["halite", "sylvite", "limestone"], [],
     ["halite_bed_field", "sylvite_bed_field", "limestone_bed_field"]),
    ("amino_acids", "Amino Acids", "c5", ["glycine", "alanine", "serine", "cysteine"], [], []),
    ("sugars", "Sugars", "c5", ["ribose", "glucose"], [], []),
    ("nucleobases", "Nucleobases", "c5", ["adenine", "guanine", "cytosine"], [], []),
    ("fatty_acids", "Fatty Acids", "c5", ["palmitic_acid"], [], []),
    ("peptides", "Peptide Bonds", "c6", ["dipeptide"], [], []),
    ("nucleotides", "Nucleotides", "c6", ["amp"], [], []),
    ("membranes", "Membranes", "c6", ["phospholipid"], [], []),
    ("energy_currency", "Energy Currency", "c6", ["atp"], [], []),
]

# Craft seconds for one spallation step (heavier element + neutron -> the next lighter element).
SPALLATION_CRAFT = 30.0
DROP_RESEARCH_RECIPES = set()

# The Physics research nodes after the tutorial, in order: (id, display name, phase, items it unlocks,
# buildings it unlocks, fields it lets you buy). Existing ids are kept where the content matches, so saves,
# tests and the Chemistry prerequisites keep working. Metal Descent is generated: 5 elements per node, from
# Mercury down to Cobalt.
PHYS_NODES_FIXED = [
    ("light_elements", "Light Elements", "p_light1", ["lithium", "beryllium", "boron"], [], []),
    ("cno_cycle", "CNO Cycle", "p_light1", ["nitrogen", "oxygen"], [], []),
    ("fusion_ii", "Stellar Forge", "p_light2", ["fluorine", "neon", "sodium"], [], []),
    ("mid_elements", "Neon Burning", "p_light2", ["magnesium", "aluminum", "silicon"], [], []),
    ("fusion_iii", "Oxygen Burning", "p_light3", ["phosphorus", "sulfur", "chlorine", "argon"], [], []),
    ("explosive_burning", "Explosive Burning", "p_light3", ["potassium", "calcium", "scandium", "titanium"], [], []),
    ("silicon_burning", "Silicon Burning", "p_light3", ["vanadium", "chromium", "manganese"], [], []),
    ("heavy_elements", "Uranium Vein", "p_descent", ["uranium", "thorium", "radium"],
     ["radioactive_containment"], ["uranium_field"]),
    ("radioactive_isotopes", "Radioactive Decay", "p_descent", ["radon", "polonium", "lead"], [], []),
    ("isotope_chemistry", "Isotope Chemistry", "p_descent",
     ["uranium_235", "thorium_233", "protactinium", "radium_227", "actinium"], ["isotopic_manipulator"], []),
    ("decay_branches", "Decay Branches", "p_descent",
     ["radon_223", "francium", "radon_211", "astatine", "lead_209", "bismuth", "lead_201", "thallium"], [], []),
]
PHYS_NODES_TAIL = [
    ("iron_convergence", "Iron Convergence", "p_iron", ["iron"], [], []),
    ("fission_i", "Breeder Reactor", "p_deep", ["neptunium", "plutonium"], ["fission_reactor"], []),
    ("transuranic_elements", "Transuranic Elements", "p_deep",
     ["americium", "curium", "berkelium", "californium", "einsteinium", "fermium"], [], []),
    ("superheavy_synthesis", "Superheavy Synthesis", "p_deep",
     ["mendelevium", "nobelium", "lawrencium", "rutherfordium", "dubnium", "seaborgium", "bohrium", "hassium",
      "meitnerium", "darmstadtium"], [], []),
    ("island_of_stability", "Island of Stability", "p_deep",
     ["roentgenium", "copernicium", "nihonium", "flerovium", "moscovium", "livermorium", "tennessine",
      "oganesson"], [], []),
]
SPALLATION_PER_NODE = 5


def phys_nodes(items):
    """PHYS_NODES_FIXED + the generated Metal Descent nodes (Hg -> Co) + PHYS_NODES_TAIL."""
    by_z = {i.get("atomic_number"): i for i in items.values() if i.get("category") == "Element"}
    metals = [by_z[z]["id"] for z in range(80, 26, -1) if z in by_z]
    nodes = list(PHYS_NODES_FIXED)
    for k in range(0, len(metals), SPALLATION_PER_NODE):
        chunk = metals[k:k + SPALLATION_PER_NODE]
        first, last = items[chunk[0]]["display_name"], items[chunk[-1]]["display_name"]
        nodes.append((f"spallation_{k // SPALLATION_PER_NODE + 1}", f"Spallation: {first} to {last}", "p_metals",
                      chunk, ["neutron_capture"] if k == 0 else [], []))
    return nodes + PHYS_NODES_TAIL
# Default craft seconds for a new Chemistry reaction, by phase (kept items keep today's craft time).
CHEM_CRAFT = {"c1": 15, "c2": 30, "c3": 45, "c4": 60, "c5": 90, "c6": 150}


def attach_recipes(gd, rows, built):
    """Give every row inputs, craft seconds, output quantity and today's value: the later worlds from
    LATER_RECIPES, Chemistry from its reaction (today's craft time when the item already exists), Physics from
    its first recipe in game_data.json. Sources with no inputs are mines."""
    value = {b["id"]: b["value"] for b in built}
    items_by_id = {i["id"]: i for i in gd["items"]}
    phys_buildings = {bd["id"] for bd in gd["buildings"] if bd.get("world") == "world_physics"}
    phys_first = {}
    for r in gd["recipes"]:
        if set(r.get("valid_buildings") or []) & phys_buildings and r.get("required_research") not in DROP_RESEARCH_RECIPES:
            if not r["id"].startswith(("s_capture_", "r_capture_", "fuse_iron", "fission_")):
                phys_first.setdefault(r["output_item"], r)
    z_of = {i["id"]: i.get("atomic_number") or 0 for i in gd["items"]}
    elem_by_z = {i.get("atomic_number"): i["id"] for i in gd["items"] if i.get("category") == "Element"}
    for row in rows:
        if row["world"] != "world_physics" or row["phase"] == "p_ignition":
            continue
        iid = row["id"]
        if iid == "uranium":
            row["inputs"], row["craft"], row["outQ"] = [], 60.0, 1
        elif row["phase"] == "p_metals":
            heavier = elem_by_z[z_of[iid] + 1]
            row["inputs"] = [{"item": heavier, "q": 1}, {"item": "neutron", "q": 1}]
            row["craft"], row["outQ"] = SPALLATION_CRAFT, 1
            row["how"] = "spallation of " + heavier
        elif iid == "iron":
            row["inputs"] = [{"item": "manganese", "q": 1}, {"item": "cobalt", "q": 1}]
            row["craft"], row["outQ"] = 60.0, 1
        elif iid in phys_first:
            r = phys_first[iid]
            row["inputs"] = [{"item": i["item"], "q": i["quantity"]} for i in r["inputs"]]
            row["craft"], row["outQ"] = float(r.get("base_craft_time") or 1), r.get("output_quantity") or 1
            row["recipe"] = r["id"]
        else:
            row["inputs"], row["craft"], row["outQ"] = [], None, 1
    descent_done = {r["id"] for r in rows if r["world"] == "world_physics" and r["phase"] != "p_ignition"}
    first = {}
    for r in gd["recipes"]:
        first.setdefault(r["output_item"], r)
    for row in rows:
        iid = row["id"]
        row["builtValue"] = value.get(iid)
        if row.get("how") == "byproduct particle":
            row["builtValue"] = float(items_by_id[iid].get("base_sell_value") or 0)
        if iid in descent_done:
            continue
        if iid in LATER_RECIPES:
            ins, t = LATER_RECIPES[iid]
            row["inputs"], row["craft"], row["outQ"] = [{"item": x, "q": q} for q, x in ins], t, 1
        elif row["world"] == "world_chemistry" and row.get("how") == "reaction":
            r = first.get(iid)
            row["craft"] = float(r["base_craft_time"]) if r and row["status"] == "keep" else CHEM_CRAFT[row["phase"]]
            row["outQ"] = next(c[5] for c in CHEM if c[0] == iid)
        elif row.get("how") == "mined source":
            r = first.get(iid)
            row["inputs"], row["craft"], row["outQ"] = [], float(r["base_craft_time"]) if r else 20.0, 1
        else:
            r = first.get(iid)
            if r:
                row["inputs"] = [{"item": i["item"], "q": i["quantity"]} for i in r["inputs"]]
                row["craft"], row["outQ"] = float(r.get("base_craft_time") or 1), r.get("output_quantity") or 1
            elif iid != "megastructure_material":
                row["inputs"], row["craft"], row["outQ"] = [], None, 1


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
    ("coal", "Coal", "m1", "new", "mined, carbon for steel and fibre"),
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
    # Prestige currency is log10(net worth): a run earns ~90 at the first wall, ~250 at Iron, ~500 at the Dyson
    # Node. Each new level costs about half a prestige at the wall it is for, rising x1.4, so every repeat at a
    # wall buys about one level. "effect" names the per-world multiplier the game applies (2026-11-23).
    {"id": "assembly_line", "name": "Assembly Line", "effect": "+10% craft speed / level", "levels": 5,
     "costs": "15, 30, 60, 120, 240", "status": "keep", "world": "All", "walls": "Oxygen, Silicon"},
    {"id": "memory_resonance", "name": "Memory Resonance", "effect": "-5% research cost / level", "levels": 5,
     "costs": "10, 20, 40, 80, 160", "status": "keep", "world": "All", "walls": "Oxygen, Silicon"},
    {"id": "quantum_yield", "name": "Quantum Yield", "effect": "+10% output / level", "levels": 10,
     "costs": "40, 80, 160, 320, 640, 960, 1500, 2200, 3300, 4900", "status": "extend (5 -> 10)",
     "world": "All", "walls": "Sodium, Copper"},
    {"id": "entropy_headstart", "name": "Entropy Headstart", "effect": "+250 starting entropy / level",
     "levels": 5, "costs": "5, 10, 20, 40, 80", "status": "keep", "world": "All", "walls": "Sodium"},
    {"id": "research_overdrive", "name": "Research Overdrive", "effect": "-5% research timer / level",
     "levels": 10, "costs": "30 ... 2800", "status": "keep", "world": "All", "walls": "Titanium"},
    {"id": "entropy_echo", "name": "Entropy Echo", "effect": "+5% prestige currency / level", "levels": 4,
     "costs": "200, 600, 1800, 5400", "status": "keep", "world": "All", "walls": "Titanium, Ecosystem"},
    {"id": "fission_yield", "name": "Fission Yield", "effect": "+15% Physics output / level",
     "levels": 6, "costs": "95, 140, 190, 270, 370, 520", "status": "new", "world": "Physics", "walls": "Lead, Iron"},
    {"id": "neutron_economy", "name": "Neutron Economy", "effect": "+10% Physics craft speed / level",
     "levels": 5, "costs": "130, 180, 250, 340, 480", "status": "new", "world": "Physics", "walls": "Copper, Iron"},
    {"id": "spring_pressure", "name": "Spring Pressure", "effect": "+20% Spring flow / level", "levels": 6,
     "costs": "89, 130, 180, 250, 340, 480", "status": "new", "world": "Chemistry", "walls": "HCN, Palmitic Acid"},
    {"id": "catalysis", "name": "Catalysis", "effect": "+15% Chemistry craft speed / level", "levels": 8,
     "costs": "120, 170, 230, 320, 450, 630, 870, 1300", "status": "new", "world": "Chemistry",
     "walls": "Phosphoric Acid, Palmitic Acid, ATP"},
    {"id": "growth_media", "name": "Growth Media", "effect": "+15% Biology output / level", "levels": 8,
     "costs": "180, 250, 350, 480, 680, 940, 1400, 1900", "status": "new", "world": "Biology",
     "walls": "Prokaryotic Cell, Ecosystem"},
    {"id": "furnace_heat", "name": "Furnace Heat", "effect": "+15% Metallurgy craft speed / level", "levels": 8,
     "costs": "220, 310, 440, 610, 850, 1200, 1700, 2400", "status": "new", "world": "Metallurgy",
     "walls": "Titanium Alloy, Metamaterial"},
    {"id": "precision", "name": "Precision", "effect": "+10% Engineering output / level", "levels": 8,
     "costs": "240, 330, 460, 650, 900, 1300, 1800, 2500", "status": "new", "world": "Engineering",
     "walls": "Metamaterial, Graviton Lens"},
]

# Pricing model the page applies live (edit it there):
#   wage(H) = wage0 x 2^((H - anchorH) / doubleH) entropy/s per building, H = active hours at unlock
#   value   = (inputs + wage x craft) / output quantity  (the game's derived-economy rule, with wage on time)
#   research cost = buildings x wage x upgrade multiplier x active seconds since the world's previous unlock,
#   where the upgrade multiplier grows (1 + wallGain) per wall already cleared. Tutorial items keep their values.
PRICES = {"wage0": 20, "anchorH": 1.0, "doubleH": 10, "buildings": 5, "wallGain": 0.15, "minPaybackMin": 2}


# Ascension: once every item is unlocked, Ascend (worlds lock again; items, research and prestige currency
# reset; shop upgrades stay) for megastructure material. Each megastructure stage costs more material and
# adds a permanent production bonus, so each loop is faster. The page projects this over years.
ASCENSION = {"firstLoopDays": 91, "loopShrink": 0.82, "floorDays": 21, "materialFirst": 10,
             "materialGrowth": 1.18, "stageCostFirst": 10, "stageCostGrowth": 1.55, "stageBonusPct": 10,
             "years": 4}


ROOT_DIR = Path(__file__).resolve().parent.parent


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
        "prices": PRICES,
        "asBuilt": built, "proposal": prop,
    }
    if "--export" in sys.argv:
        out = ROOT_DIR / "docs" / "balance" / "item-timeline-plan.json"
        keep = {k: data[k] for k in ("generated", "phases", "proposal", "walls", "upgrades", "ascension",
                                     "activeHoursPerDay", "prices", "worlds")}
        out.write_text(json.dumps(keep, indent=1, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
        print(out)
        return
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
