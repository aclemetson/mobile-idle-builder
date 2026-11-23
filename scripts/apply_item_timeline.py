"""Apply the Item Timeline plan to Assets/Data/game_data.json, one phase at a time (2026-11-23).

    python scripts/apply_item_timeline.py --phase 1            # preview: print what would change
    python scripts/apply_item_timeline.py --phase 1 --apply    # write it

The plan is docs/balance/item-timeline-plan.json (python scripts/item_timeline.py --export), or the version the
user saved on the Item Timeline page (read it with ArtifactData into that file first). Values and research costs
come from scripts/economy_plan.py, the same model the page shows. Everything this writes is marked
"planned": true so scripts/rebalance_curve.py leaves it alone.

Phase 1 (Physics): fusion climbs to Manganese; Uranium is mined and walked down by decay and spallation to
Iron, which joins Manganese and Cobalt; transuranics move to Deep Physics. See docs/agents/elements-and-isotopes.md.
"""
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import economy_plan as ep                      # noqa: E402
import item_timeline as tl                     # noqa: E402
from gamedata_edit import GameData             # noqa: E402

# Research removed in phase 1 and where anything that pointed at it now points.
REMOVED_RESEARCH = {
    "transition_metals": "iron_convergence", "precious_metals": "spallation_1",
    "exotic_metals": "spallation_1", "lanthanides_and_heavy_metals": "spallation_3",
    "heavy_transition_metals": "spallation_5", "rapid_neutron_capture": "spallation_4",
    "slow_neutron_capture": "spallation_7", "fissile_extraction": "heavy_elements",
}
REMOVED_RECIPE_PREFIXES = ("s_capture_", "r_capture_")
REMOVED_RECIPES = {"fuse_iron", "uranium", "neptunium", "plutonium", "collect_plutonium"}   # uranium is mined; Np/Pu are bred
# U-235 / Pu-239 splitting would skip the descent (Ba + Kr are priced by it, x66 their fuel): the Fission Reactor
# becomes a breeder instead, the way real reactors make the first transuranics.
REMOVED_RECIPE_PREFIXES = REMOVED_RECIPE_PREFIXES + ("fission_u235", "fission_pu239")
BREEDER = [
    {"id": "breed_neptunium", "display_name": "Neptunium", "inputs": [("uranium", 1)],
     "output": "neptunium", "byproducts": [("beta_particle", 1)], "time": 240.0,
     "note": "U-238 captures one of the reactor's own neutrons and beta-decays to Np-239."},
    {"id": "breed_plutonium", "display_name": "Plutonium", "inputs": [("neptunium", 1)],
     "output": "plutonium", "byproducts": [("beta_particle", 1)], "time": 240.0,
     "note": "Np-239 beta-decays to Pu-239: how reactors breed plutonium."},
]
BLOCKS_REMOVED = {"actinide_frontier"}

# Deep Physics (transuranics) keeps the old curve's prices in phase 1: the plan prices it at days 40-75, which
# only adds up once Biology, Metallurgy and Engineering are on the plan too (phase 4).
LATER_PHASES = {"p_deep"}
PHASE_BLOCK = {"p_light1": "first_fusion", "p_light2": "stellar_forge", "p_light3": "deep_burning",
               "p_descent": "actinide_descent", "p_metals": "metal_descent", "p_iron": "iron_age",
               "p_deep": "deep_physics"}
PHASE_DURATION = {"p_light1": 120, "p_light2": 300, "p_light3": 600, "p_descent": 900, "p_metals": 1200,
                  "p_iron": 1800, "p_deep": 3600}
PHASE_BRANCH = {"p_light1": "Astrophysics", "p_light2": "Astrophysics", "p_light3": "Astrophysics",
                "p_descent": "Nuclear", "p_metals": "Nuclear", "p_iron": "Astrophysics", "p_deep": "Nuclear"}
NODE_TEXT = {
    "light_elements": "Fuse helium and deuterium into Lithium, then capture protons up to Boron.",
    "cno_cycle": "Capture a proton on Carbon for Nitrogen; fuse Carbon with helium for Oxygen.",
    "fusion_ii": "Fluorine, Neon and Sodium: the forge heats up.",
    "mid_elements": "Neon burning climbs to Magnesium, Aluminum and Silicon, the last delivery before Chemistry.",
    "fusion_iii": "Oxygen burning: Phosphorus, Sulfur, Chlorine and Argon.",
    "explosive_burning": "Potassium, Calcium, Scandium and Titanium.",
    "silicon_burning": "Vanadium, Chromium and Manganese: the end of the fusion climb, one step short of Iron.",
    "heavy_elements": "Open the Uranium Vein. Uranium decays to Thorium and Radium in the Radioactive Containment.",
    "radioactive_isotopes": "Radon, Polonium and Lead: the bottom of the main decay chain.",
    "isotope_chemistry": "The Isotopic Manipulator: Uranium-235 and the branch isotopes for Protactinium and Actinium.",
    "decay_branches": "Francium, Astatine, Bismuth and Thallium from the side branches of the chain.",
    "iron_convergence": "Manganese from fusion and Cobalt from the descent meet: Iron, the most tightly bound nucleus.",
    "fission_i": "The Fission Reactor breeds Uranium into Neptunium and Plutonium, the first transuranics.",
    "transuranic_elements": "Americium to Fermium, assembled nucleon by nucleon.",
    "superheavy_synthesis": "Mendelevium to Darmstadtium.",
    "island_of_stability": "Roentgenium to Oganesson, the end of the table.",
}
BLOCK_TEXT = {
    "first_fusion": ("First Fusion", "Lithium to Oxygen after the tutorial."),
    "stellar_forge": ("Stellar Forge", "Fluorine to Silicon: the end of Physics before Chemistry opens."),
    "deep_burning": ("Deep Burning", "Phosphorus to Manganese, the end of the fusion climb."),
    "actinide_descent": ("Actinide Descent", "Mine Uranium and walk it down the decay chain to Lead and Thallium."),
    "metal_descent": ("Metal Descent", "Spallation, one element at a time, from Mercury down to Cobalt."),
    "iron_age": ("Iron", "Both ends meet: Iron from Manganese and Cobalt."),
    "deep_physics": ("Deep Physics", "Fission, the transuranics and the superheavy elements."),
}
# Block order (game phases): by when the plan opens them; Chemistry and Biology blocks keep their place between.
# chemistry_world sits after iron_age until phase 2 replaces its content: its old nodes (materials_science) need Iron.
BLOCK_ORDER = ["ignition", "foundations", "first_fusion", "stellar_forge", "chemistry_entry", "deep_burning",
               "actinide_descent", "metal_descent", "iron_age", "chemistry_world", "biology_world", "deep_physics",
               "synthetic_frontier"]
BLOCK_PRESET_FROM = {"first_fusion": "foundations", "actinide_descent": "actinide_frontier",
                     "metal_descent": "actinide_frontier", "iron_age": "actinide_frontier",
                     "deep_physics": "biology_world"}


def plan_rows():
    plan = ep.build(ep.load())
    return plan, [r for r in plan["proposal"] if r["world"] == "world_physics"]


def phase1(gd_json, apply):
    plan, rows = plan_rows()
    by_id = {r["id"]: r for r in rows}
    items = {i["id"]: i for i in gd_json["items"]}
    nodes = tl.phys_nodes(items)
    ahpd = plan["activeHoursPerDay"]
    phases = {p["id"]: p for p in plan["phases"]}
    changes = []
    gd = GameData() if apply else None

    def note(msg):
        changes.append(msg)

    recipes = {r["id"]: r for r in gd_json["recipes"]}
    next_recipe_id = max(r["recipe_id"] for r in gd_json["recipes"]) + 1

    # ── Recipes ────────────────────────────────────────────────────────────────────────────────
    node_of = {iid: n[0] for n in nodes for iid in n[3]}
    removed = sorted(rid for rid in recipes if rid.startswith(REMOVED_RECIPE_PREFIXES) or rid in REMOVED_RECIPES)
    note(f"remove {len(removed)} recipes: {', '.join(removed[:6])} ...")
    node_recipes = {n[0]: [] for n in nodes}
    new_recipes = []
    for r in rows:
        node = node_of.get(r["id"])
        if not node:
            continue
        if r["phase"] == "p_metals":
            rid = "spall_" + r["id"]
            heavier = r["inputs"][0]["item"]
            new_recipes.append({
                "id": rid, "display_name": items[r["id"]]["display_name"], "recipe_id": next_recipe_id, "tier": 3,
                "category": "Element",
                "inputs": [{"item": heavier, "quantity": 1}, {"item": "neutron", "quantity": 1}],
                "neutron_adjustment": [], "output_item": r["id"], "output_quantity": 1,
                "byproducts": [{"item": "proton", "quantity": 1}],
                "base_craft_time": float(r["craft"]), "manual_craft_time": float(r["craft"]),
                "power_cost_is_dynamic": False, "fixed_power_cost_ev": 150.0, "valid_buildings": ["neutron_capture"],
                "can_craft_manually": False, "known_from_start": False, "required_research": node,
                "unlocks_research": None, "is_simplified": True,
                "simplification_note": f"Spallation: a neutron knocks a proton out of {heavier}, one element down.",
                "planned": True})
            next_recipe_id += 1
            node_recipes[node].append(rid)
        elif r["id"] == "iron":
            new_recipes.append({
                "id": "iron_convergence", "display_name": "Iron", "recipe_id": next_recipe_id, "tier": 3,
                "category": "Fusion",
                "inputs": [{"item": "manganese", "quantity": 1}, {"item": "cobalt", "quantity": 1}],
                "neutron_adjustment": [], "output_item": "iron", "output_quantity": 1,
                "byproducts": [{"item": "gamma_photon", "quantity": 1}],
                "base_craft_time": float(r["craft"]), "manual_craft_time": float(r["craft"]),
                "power_cost_is_dynamic": False, "fixed_power_cost_ev": 200.0, "valid_buildings": ["fusion_reactor"],
                "can_craft_manually": False, "known_from_start": False, "required_research": node,
                "unlocks_research": None, "is_simplified": True,
                "simplification_note": "The two ends meet: Manganese from fusion and Cobalt from the descent.",
                "planned": True})
            next_recipe_id += 1
            node_recipes[node].append("iron_convergence")
    for b in BREEDER:
        new_recipes.append({
            "id": b["id"], "display_name": b["display_name"], "recipe_id": next_recipe_id, "tier": 3,
            "category": "Fission", "inputs": [{"item": i, "quantity": q} for i, q in b["inputs"]],
            "neutron_adjustment": [], "output_item": b["output"], "output_quantity": 1,
            "byproducts": [{"item": i, "quantity": q} for i, q in b["byproducts"]],
            "base_craft_time": b["time"], "manual_craft_time": b["time"], "power_cost_is_dynamic": False,
            "fixed_power_cost_ev": 300.0, "valid_buildings": ["fission_reactor"], "can_craft_manually": False,
            "known_from_start": False, "required_research": "fission_i", "unlocks_research": None,
            "is_simplified": True, "simplification_note": b["note"]})
        next_recipe_id += 1
        node_recipes["fission_i"].append(b["id"])
    note(f"add {len(new_recipes)} recipes (spallation Hg..Co, iron_convergence, breeder)")
    # existing recipes re-gated to the node that unlocks their output
    regate = {}
    for rid, rec in recipes.items():
        if rid in removed:
            continue
        out = rec["output_item"]
        gate = rec.get("required_research")
        node = node_of.get(out)
        if rid == "collect_uranium":
            node = "heavy_elements"
        elif rid == "collect_plutonium" or rid.startswith("fission_"):
            node = "fission_i"
        elif rid.startswith(("decay_", "irradiate_", "strip_")) or rid == "uranium_235":
            node = node_of.get(out)
        elif node is None or gate in (None, "") or not (set(rec.get("valid_buildings") or []) & PHYS_BUILDINGS(gd_json)):
            continue
        if node and node != gate:
            regate[rid] = node
        if node:
            node_recipes.setdefault(node, [])
            if rid not in node_recipes[node]:
                node_recipes[node].append(rid)
    note(f"re-gate {len(regate)} recipes: " + ", ".join(f"{k}->{v}" for k, v in list(regate.items())[:8]) + " ...")

    # ── Research ──────────────────────────────────────────────────────────────────────────────
    research = {r["id"]: r for r in gd_json["research"]}
    new_research, prev, depth = [], "fusion_i", research["fusion_i"].get("depth_in_tree", 4)
    costs = {}
    for node, name, ph, iids, buildings, fields in nodes:
        cost = ep.nice(sum(by_id[i]["research"] or 0 for i in iids if i in by_id))
        costs[node] = cost
        depth += 1
        base = dict(research.get(node) or {})
        entry = {
            "id": node, "display_name": name, "description": NODE_TEXT.get(node) or f"{name}.",
            "branch": PHASE_BRANCH[ph], "prerequisites": [prev], "depth_in_tree": depth,
            "cost_base_currency": int(cost), "cost_prestige_currency": 0, "duration_seconds": PHASE_DURATION[ph],
            "unlocks_items": [i for i in iids if i in items], "unlocks_recipes": node_recipes.get(node, []),
            "unlocks_buildings": buildings, "unlocks_grid_expansion": False, "unlocks_research": [],
            "resets_on_prestige": True,
            "codex_entry": base.get("codex_entry") or NODE_TEXT.get(node) or name,
            "icon_path": base.get("icon_path") or "TODO", "block": PHASE_BLOCK[ph], "world": "world_physics",
            "planned": ph not in LATER_PHASES,
        }
        if ph in LATER_PHASES:       # priced by rebalance_curve.py until its phase (4) moves the other worlds too
            entry["cost_base_currency"] = int(base.get("cost_base_currency") or cost)
            del entry["planned"]
        for k in ("_comment", "tree_x", "tree_y", "effects", "field_cooldown_mult"):
            if k in base:
                entry[k] = base[k]
        if ph not in ("p_light1", "p_light2"):
            entry["required_world"] = "world_chemistry"     # the Chemistry gate: Physics past Silicon needs it
        new_research.append(entry)
        prev = node
    # Research never costs less than its prerequisite (ProgressionBalanceTests): the plan's costs dip right after a
    # wall and wherever a node holds fewer items, so pool adjacent violators along the chain. Each pooled run keeps
    # its total, so the timeline's hours hold; a wall becomes a stretch of dear research instead of one spike,
    # which is how a wall feels in Idle Planet Miner anyway.
    planned = [e for e in new_research if e.get("planned")]
    pooled = pav([e["cost_base_currency"] for e in planned])
    for e, c in zip(planned, pooled):
        e["cost_base_currency"] = int(ep.nice(c))
        costs[e["id"]] = e["cost_base_currency"]
    note("research chain: " + " > ".join(f"{n['id']}({n['cost_base_currency']:,})" for n in new_research))
    for rid in REMOVED_RESEARCH:
        note(f"remove research {rid} (refs -> {REMOVED_RESEARCH[rid]})")

    if not apply:
        print("\n".join(changes))
        return
    # ── Write ────────────────────────────────────────────────────────────────────────────────
    for rid in removed:
        gd.delete("recipes", rid)
    last_recipe = max((r for r in gd_json["recipes"] if r["id"] not in removed), key=lambda r: r["recipe_id"])["id"]
    for rec in new_recipes:
        gd.insert_after("recipes", last_recipe, rec)
        last_recipe = rec["id"]
    later_nodes = {n[0] for n in nodes if n[2] in LATER_PHASES}
    for rid, node in regate.items():
        if node in later_nodes:
            gd.update("recipes", rid, required_research=node)
        else:
            gd.update("recipes", rid, required_research=node, planned=True)
    for rid in ("collect_uranium",):
        gd.update("recipes", rid, base_craft_time=float(by_id["uranium"]["craft"]),
                  manual_craft_time=float(by_id["uranium"]["craft"]))
    # research: replace / insert in chain order after fusion_i, drop removed
    for rid in REMOVED_RESEARCH:
        if gd.has("research", rid):
            gd.delete("research", rid)
    after = "fusion_i"
    for entry in new_research:
        if gd.has("research", entry["id"]):
            gd.delete("research", entry["id"])
        gd.insert_after("research", after, entry)
        after = entry["id"]
    gd.save()
    finish_phase1(plan, nodes, costs)


def pav(values):
    """Pool adjacent violators: the closest non-decreasing sequence, each pooled run keeping its sum."""
    stack = []
    for v in values:
        stack.append([float(v), 1])
        while len(stack) > 1 and stack[-2][0] / stack[-2][1] > stack[-1][0] / stack[-1][1]:
            total, n = stack.pop()
            stack[-1][0] += total
            stack[-1][1] += n
    out = []
    for total, n in stack:
        out += [total / n] * n
    return out


def PHYS_BUILDINGS(gd_json):
    return {b["id"] for b in gd_json["buildings"] if b.get("world") == "world_physics"}


def finish_phase1(plan, nodes, costs):
    """Second pass on the written file: prerequisites, children lists, buildings, fields, sites, blocks, values."""
    gd = GameData()
    data = json.loads(gd.text)
    research = {r["id"]: r for r in data["research"]}
    node_ids = [n[0] for n in nodes]
    # prerequisites that pointed at removed research
    for r in data["research"]:
        pre = r.get("prerequisites") or []
        new = []
        for p in pre:
            p = REMOVED_RESEARCH.get(p, p)
            if p not in new and p != r["id"]:
                new.append(p)
        if new != pre:
            gd.update("research", r["id"], prerequisites=new)
            r["prerequisites"] = new
    # children lists rebuilt from prerequisites everywhere
    children = {r["id"]: [] for r in data["research"]}
    for r in data["research"]:
        for p in r.get("prerequisites") or []:
            if p in children:
                children[p].append(r["id"])
    for r in data["research"]:
        if (r.get("unlocks_research") or []) != children[r["id"]]:
            gd.update("research", r["id"], unlocks_research=children[r["id"]])
    # buildings
    gd.update("buildings", "neutron_capture", display_name="Spallation Chamber",
              description="Fires neutrons at a heavy element until a proton breaks off, leaving the element one "
                          "below it. Walks the metals down from Mercury to Cobalt.",
              required_research="spallation_1",
              supported_recipes=[r["id"] for r in data["recipes"] if r["id"].startswith("spall_")],
              recipe_mode_labels=None, recipe_mode_prefixes=None, input_slot_labels=["Element", "Neutrons"],
              codex_entry="Spallation: a fast particle chips nucleons off a heavy nucleus. Accelerators use it "
                          "to make neutron beams; here it runs the table downhill, one element at a time.")
    gd.update("buildings", "fission_reactor", supported_recipes=[b["id"] for b in BREEDER],
              description="A breeder: Uranium soaks up the reactor's own neutrons and decays into Neptunium, "
                          "then Plutonium.", input_slot_labels=["Fuel"])
    for bid, node in (("radioactive_containment", "heavy_elements"), ("isotopic_manipulator", "isotope_chemistry"),
                      ("fission_reactor", "fission_i")):
        gd.update("buildings", bid, required_research=node)
    for b in data["buildings"]:
        sup = b.get("supported_recipes")
        if not sup:
            continue
        keep = [x for x in sup if x in {r["id"] for r in data["recipes"]}]
        if b["id"] == "fusion_reactor" and "iron_convergence" not in keep:
            keep.append("iron_convergence")
        if keep != sup and b["id"] not in ("neutron_capture", "fission_reactor"):
            gd.update("buildings", b["id"], supported_recipes=keep)
    # fields: Uranium and Plutonium are bought when their research opens them
    u_cost = int(ep.nice(costs["heavy_elements"] * 0.05))
    gd.update("fields", "uranium_field", purchase_research="heavy_elements", purchase_cost_base=u_cost,
              purchase_cost_multiplier=1.5, purchase_group_size=1)
    # Plutonium is bred in the Fission Reactor, not mined: its vein stays listed at density 0 and is never sold.
    site = next(s for s in data["sites"] if s["id"] == "site_actinide_vein")
    fo = [dict(f, density_multiplier=0.0) if (f.get("field_id") or f.get("field")) in ("uranium_field", "plutonium_field")
          else f for f in site["field_overrides"]]
    gd.update("sites", "site_actinide_vein", field_overrides=fo)
    # item values
    plan_by = {r["id"]: r for r in plan["proposal"] if r["world"] == "world_physics"}
    for it in data["items"]:
        r = plan_by.get(it["id"])
        if r and r.get("value") is not None and r["phase"] not in ("p_ignition", *LATER_PHASES)                 and r.get("how") != "byproduct particle":
            gd.update("items", it["id"], base_sell_value=float(r["value"]), planned=True)
    # research blocks
    old = {b["id"]: b for b in data["research_blocks"]}
    ph_by_block = {v: k for k, v in PHASE_BLOCK.items()}
    blocks = []
    for n, bid in enumerate(BLOCK_ORDER, start=1):
        if bid in ph_by_block:
            ph = next(p for p in plan["phases"] if p["id"] == ph_by_block[bid])
            src = old.get(bid) or old.get(BLOCK_PRESET_FROM.get(bid, ""), {})
            name, desc = BLOCK_TEXT[bid]
            later = ph["id"] in LATER_PHASES
            b = {"id": bid, "name": name, "order": n,
                 "target_hours": float(src.get("target_hours") or 30) if later
                 else round((ph["end"] - ph["start"]) * plan["activeHoursPerDay"], 2),
                 "anchor": False, "description": desc}
            if not later:
                b["planned"] = True
            for k in ("start_prestige_count", "start_prestige_currency_earned", "start_permanent_upgrades",
                      "start_entropy", "start_sites"):
                b[k] = src.get(k, [] if k in ("start_permanent_upgrades", "start_sites") else 0)
        else:
            b = dict(old[bid], order=n)
        blocks.append(b)
    # Dev-console block presets (start_*) must grow block by block (BlockStartPresetsTests): a block the new order
    # puts later than a richer one takes that one's preset.
    preset_keys = ("start_prestige_count", "start_prestige_currency_earned", "start_permanent_upgrades", "start_entropy")
    for prev, b in zip(blocks, blocks[1:]):
        if (b.get("start_prestige_currency_earned") or 0) < (prev.get("start_prestige_currency_earned") or 0):
            for k in preset_keys:
                b[k] = prev.get(k)
        if (b.get("start_entropy") or 0) < (prev.get("start_entropy") or 0):
            b["start_entropy"] = prev.get("start_entropy")
    _replace_blocks(gd, data["research_blocks"], blocks)
    gd.save()


def _replace_blocks(gd, old_blocks, blocks):
    """Keep the first block's slot, drop the rest, then write the new list in order after it."""
    first = old_blocks[0]["id"]
    for b in old_blocks[1:]:
        gd.delete("research_blocks", b["id"])
    gd.put("research_blocks", dict(blocks[0], id=first) if blocks[0]["id"] == first else blocks[0])
    prev = blocks[0]["id"]
    for b in blocks[1:]:
        gd.insert_after("research_blocks", prev, b)
        prev = b["id"]


# ── Phase 2: life Chemistry ─────────────────────────────────────────────────────────────────────────────
CHEM_PHASE_BLOCK = {"c1": "chemistry_entry", "c2": "chem_small_molecules", "c3": "chem_organics",
                    "c4": "chem_sulfur_phosphorus", "c5": "chem_building_blocks", "c6": "chem_molecules_of_life"}
CHEM_BLOCK_TEXT = {
    "chemistry_entry": ("First Chemistry", "The gate is open: hydrogen, oxygen, carbon and nitrogen into water, CO2, methane and ammonia."),
    "chem_small_molecules": ("Small Molecules", "Syngas, formaldehyde, methanol and the prebiotic nitrogen of HCN and urea."),
    "chem_organics": ("Organics", "Ethylene, acetylene and the alcohols and acids built on them."),
    "chem_sulfur_phosphorus": ("Sulfur & Phosphorus", "The last two elements of life: sulfur vents and phosphate rock, and their acids."),
    "chem_building_blocks": ("Building Blocks of Life", "Amino acids, sugars, nucleobases, a fatty acid and the salts cells need."),
    "chem_molecules_of_life": ("Molecules of Life", "Peptides, nucleotides, membranes and ATP: everything Biology starts from."),
}
CHEM_DURATION = {"c1": 300, "c2": 600, "c3": 900, "c4": 1200, "c5": 1500, "c6": 1800}
# Retired now: nothing in Biology or the metal chain uses them. amino_acid, fatty_acid, nucleotide (Biology's
# inputs) and uranium_hexafluoride (the Superconductor) retire with phase 3.
RETIRE_P2_ITEMS = ["octane", "polymer_precursor", "catalyst", "chlorine_gas", "sodium_hydroxide", "table_salt"]
RETIRE_P2_RECIPES = RETIRE_P2_ITEMS + ["methane_combustion", "collect_sodium", "collect_chlorine", "collect_sulfur"]
REMOVED_RESEARCH_P2 = {"organic_chemistry": "hydrocarbons"}
NEW_FIELDS = {   # id: (display, drop, colour, codex)
    "sulfur_vent_field": ("Sulfur Vent", "native_sulfur", "#E3C832", "Yellow crusts of native sulfur around a fumarole."),
    "phosphate_rock_field": ("Phosphate Rock", "red_phosphorus", "#B5543C", "Phosphorus locked in rock, reduced to red phosphorus as it is extracted."),
    "halite_bed_field": ("Halite Bed", "halite", "#E8E1D9", "Rock salt left by an ancient sea: sodium and chlorine for every cell."),
    "sylvite_bed_field": ("Sylvite Bed", "sylvite", "#D98C7A", "Potash: potassium chloride, the salt that keeps nerves firing."),
    "limestone_bed_field": ("Limestone Bed", "limestone", "#C8C2A8", "Calcium carbonate from old shells: calcium for bones and signals."),
}
CODEX = {
    "native_sulfur": "Sulfur as it occurs near volcanic vents. Life needs it for two amino acids.",
    "red_phosphorus": "Phosphorus reduced from phosphate rock: the backbone of DNA and the energy of ATP.",
    "halite": "Rock salt, NaCl. Cells run on the difference between sodium outside and potassium inside.",
    "sylvite": "Potassium chloride from evaporated seas.",
    "limestone": "Calcium carbonate. Shells, bones and the calcium signals in every cell.",
    "carbon_monoxide": "CO: half-burnt carbon. With hydrogen it is syngas, the start of most organic chemistry.",
    "hydrogen_peroxide": "H2O2: an oxidiser cells make and destroy every second.",
    "formaldehyde": "CH2O: the simplest aldehyde. Five of them can join into a sugar.",
    "methanol": "CH3OH: wood alcohol, made from syngas.",
    "formic_acid": "HCOOH: the acid in ant stings, made from carbon monoxide and water.",
    "hydrogen_cyanide": "HCN: deadly to us, yet five of them make adenine, a letter of the genetic code.",
    "urea": "CO(NH2)2: the first organic molecule made from inorganic ones (Wohler, 1828).",
    "ethylene": "C2H4: the simplest alkene, a plant hormone and the start of plastics.",
    "acetylene": "C2H2: a triple-bonded fuel that burns hot enough to weld.",
    "acetic_acid": "CH3COOH: vinegar, and the two-carbon unit cells build fats from.",
    "glycerol": "C3H8O3: the three-carbon spine of every fat.",
    "hydrogen_sulfide": "H2S: rotten-egg gas from the vents, the sulfur source for cysteine.",
    "phosphoric_acid": "H3PO4: phosphate, the link in DNA's backbone and ATP's tail.",
    "glycerol_phosphate": "Glycerol with a phosphate: the head of a membrane lipid.",
    "glycine": "The simplest amino acid.",
    "alanine": "A small amino acid, built from formaldehyde and ammonia.",
    "serine": "An amino acid with an alcohol group.",
    "cysteine": "The amino acid with sulfur: its bridges hold proteins in shape.",
    "ribose": "The five-carbon sugar in RNA and ATP.",
    "adenine": "A nucleobase made from five HCN: the A in DNA and the A in ATP.",
    "guanine": "A nucleobase: the G in the genetic code.",
    "cytosine": "A nucleobase: the C in the genetic code.",
    "palmitic_acid": "A sixteen-carbon fatty acid, built two carbons at a time from acetic acid.",
    "dipeptide": "Two amino acids joined by a peptide bond: the start of every protein.",
    "amp": "Adenosine monophosphate: adenine, ribose and one phosphate, a nucleotide.",
    "phospholipid": "A phosphate head and two fatty tails: it folds itself into membranes.",
    "atp": "Adenosine triphosphate: the energy currency of every living cell.",
}
SUB = str.maketrans("0123456789", "₀₁₂₃₄₅₆₇₈₉")


def phase2(gd_json, apply):
    plan = ep.build(ep.load())
    rows = {r["id"]: r for r in plan["proposal"] if r["world"] == "world_chemistry" and r["phase"] != "retired"}
    items = {i["id"]: i for i in gd_json["items"]}
    recipes = {r["id"]: r for r in gd_json["recipes"]}
    research = {r["id"]: r for r in gd_json["research"]}
    chem = {c[0]: c for c in tl.CHEM}
    src = {c[0]: c for c in tl.CHEM_SOURCES}
    next_item = max(i["item_id"] for i in gd_json["items"]) + 1
    next_recipe = max(r["recipe_id"] for r in gd_json["recipes"]) + 1
    node_of = {iid: n[0] for n in tl.CHEM_NODES for iid in n[3]}
    out = []

    # items
    new_items, item_updates, symbols = [], {}, {}
    for iid, r in rows.items():
        if iid in items:
            item_updates[iid] = float(r["value"])
            if iid == "glucose":      # its symbol was "Glc"; the balance check reads formulas from symbols
                symbols[iid] = chem[iid][2].translate(SUB)
            continue
        name, formula = (src[iid][1], src[iid][2]) if iid in src else (chem[iid][1], chem[iid][2])
        raw = iid in src
        new_items.append({
            "id": iid, "display_name": name, "symbol": formula.translate(SUB), "icon_path": "TODO",
            "item_id": next_item, "tier": 3, "tier_ref": "tier_3", "category": "RawResource" if raw else "Molecule",
            "atomic_number": 0, "atomic_mass": 0, "charge": "0", "is_radioactive": False, "decay_type": "None",
            "half_life_note": "", "is_fissile": False, "is_harvested": raw, "field_type": "Element" if raw else "None",
            "is_secondary_particle": False, "particle_uses": 0, "codex_entry": CODEX.get(iid, name + "."),
            "base_sell_value": float(r["value"]), "isotope_sell_multiplier": 1.0, "world": "world_chemistry",
            "planned": True})
        next_item += 1
    retired = {i: (items[i]["item_id"], int(items[i]["base_sell_value"])) for i in RETIRE_P2_ITEMS if i in items}
    out.append(f"items: {len(new_items)} new, {len(item_updates)} repriced, retire {sorted(retired)}")

    # recipes
    new_recipes, recipe_updates = [], {}
    for iid, r in rows.items():
        node = node_of[iid]
        if iid in src:                                   # a mine: collect_<id> in the Extractor
            rid, body = "collect_" + iid, {
                "display_name": rows[iid]["name"], "tier": 3, "category": "Element", "inputs": [],
                "neutron_adjustment": [], "output_item": iid, "output_quantity": 1, "byproducts": [],
                "base_craft_time": float(r["craft"]), "manual_craft_time": float(r["craft"]),
                "power_cost_is_dynamic": False, "fixed_power_cost_ev": 0.0, "valid_buildings": ["element_harvester"],
                "can_craft_manually": True, "known_from_start": False, "required_research": node,
                "unlocks_research": None, "is_simplified": False, "simplification_note": "", "planned": True}
        else:
            c = chem[iid]
            rid, body = iid, {
                "display_name": c[1], "tier": 3, "category": "Molecule",
                "inputs": [{"item": x, "quantity": q} for q, x in c[4]], "neutron_adjustment": [],
                "output_item": iid, "output_quantity": c[5],
                "byproducts": [{"item": x, "quantity": q} for q, x in c[6]],
                "base_craft_time": float(r["craft"]), "manual_craft_time": 0.0, "power_cost_is_dynamic": False,
                "fixed_power_cost_ev": 200.0, "valid_buildings": [c[7]], "can_craft_manually": False,
                "known_from_start": False, "required_research": node, "unlocks_research": None,
                "is_simplified": False, "simplification_note": tl.equation(c[4], c[5], iid, c[6]), "planned": True}
        if rid in recipes:
            recipe_updates[rid] = body
        else:
            new_recipes.append(dict({"id": rid, "recipe_id": next_recipe}, **body))
            next_recipe += 1
    out.append(f"recipes: {len(new_recipes)} new, {len(recipe_updates)} rewritten, remove {RETIRE_P2_RECIPES}")

    # research: CHEM_NODES chain, costs summed and pooled like Physics
    entries, prev = [], None
    for node, name, ph, iids, buildings, fields in tl.CHEM_NODES:
        base = dict(research.get(node) or {})
        rids = [("collect_" + i) if i in src else i for i in iids]
        e = {"id": node, "display_name": name,
             "description": base.get("description") if node == "chemistry_lab" else
             f"{name}: " + ", ".join(rows[i]["name"] for i in iids) + ".",
             "branch": "Chemistry" if ph != "c6" else "Biology", "prerequisites": [prev] if prev else [],
             "depth_in_tree": 3 + len(entries),
             "cost_base_currency": int(sum(rows[i]["research"] or 0 for i in iids)), "cost_prestige_currency": 0,
             "duration_seconds": CHEM_DURATION[ph], "unlocks_items": iids, "unlocks_recipes": rids,
             "unlocks_buildings": base.get("unlocks_buildings") if node == "chemistry_lab" else buildings,
             "unlocks_grid_expansion": False, "unlocks_research": [], "resets_on_prestige": True,
             "codex_entry": base.get("codex_entry") or CODEX.get(iids[-1], name),
             "icon_path": base.get("icon_path") or "TODO", "block": CHEM_PHASE_BLOCK[ph],
             "required_world": "world_chemistry", "world": "world_chemistry", "planned": True}
        if "_comment" in base:
            e["_comment"] = base["_comment"]
        entries.append(e)
        prev = node
    for e, c in zip(entries, pav([e["cost_base_currency"] for e in entries])):
        e["cost_base_currency"] = int(ep.nice(c))
    out.append("research: " + " > ".join(f"{e['id']}({e['cost_base_currency']:,})" for e in entries))
    if not apply:
        print("\n".join(out))
        print("C# RetiredItems lines:")
        for iid, (num, price) in retired.items():
            print(f"            {{ {num}, {price} }},  // {iid}")
        return

    gd = GameData()
    for rid in RETIRE_P2_RECIPES:
        if gd.has("recipes", rid):
            gd.delete("recipes", rid)
    for iid in RETIRE_P2_ITEMS:
        if gd.has("items", iid):
            gd.delete("items", iid)
    last = gd_json["items"][-1]["id"]
    for it in new_items:
        gd.insert_after("items", last, it)
        last = it["id"]
    for iid, v in item_updates.items():
        gd.update("items", iid, base_sell_value=v, planned=True, **({"symbol": symbols[iid]} if iid in symbols else {}))
    for rid, body in recipe_updates.items():
        gd.update("recipes", rid, **body)
    last = max((r for r in gd_json["recipes"] if r["id"] not in RETIRE_P2_RECIPES), key=lambda r: r["recipe_id"])["id"]
    for rec in new_recipes:
        gd.insert_after("recipes", last, rec)
        last = rec["id"]
    # Biology's inputs keep working until phase 3 rewires them
    gd.update("recipes", "fatty_acid", inputs=[{"item": "palmitic_acid", "quantity": 1}])
    nuc = gd.get("recipes", "nucleotide")
    gd.update("recipes", "nucleotide", inputs=[dict(i, item="red_phosphorus") if i["item"] == "phosphorus" else i
                                               for i in nuc["inputs"]])
    for rid in REMOVED_RESEARCH_P2:
        if gd.has("research", rid):
            gd.delete("research", rid)
    for e in entries:
        if gd.has("research", e["id"]):
            gd.put("research", e)
        else:
            gd.insert_after("research", "chemistry_lab" if e is entries[0] else entries[entries.index(e) - 1]["id"], e)
    gd.save()
    finish_phase2(plan, entries)


def finish_phase2(plan, entries):
    gd = GameData()
    data = json.loads(gd.text)
    recipes = data["recipes"]
    # prerequisites that pointed at removed research, then children lists everywhere
    for r in data["research"]:
        pre = [REMOVED_RESEARCH_P2.get(p, p) for p in r.get("prerequisites") or []]
        if r["id"] == "biochem_precursors":
            pre = ["energy_currency"]         # Biology's precursors come after ATP
        if pre != (r.get("prerequisites") or []):
            gd.update("research", r["id"], prerequisites=pre)
            r["prerequisites"] = pre
    children = {r["id"]: [] for r in data["research"]}
    for r in data["research"]:
        for p in r.get("prerequisites") or []:
            if p in children:
                children[p].append(r["id"])
    for r in data["research"]:
        if (r.get("unlocks_research") or []) != children[r["id"]]:
            gd.update("research", r["id"], unlocks_research=children[r["id"]])
    # what reaction_engineering still unlocks (the metal chain until phase 3)
    left = [r["id"] for r in recipes if r.get("required_research") == "reaction_engineering"]
    gd.update("research", "reaction_engineering", unlocks_recipes=left, unlocks_buildings=["metal_harvester"],
              unlocks_items=sorted({r["output_item"] for r in recipes if r["id"] in left}))
    # buildings: gates and recipe lists for the Chemistry buildings
    for bid, node in (("catalytic_reactor", "carbonyl_chemistry"), ("compound_synthesizer", "prebiotic_nitrogen"),
                      ("organic_synthesizer", "hydrocarbons")):
        gd.update("buildings", bid, required_research=node)
    for bid in ("molecular_synthesizer", "catalytic_reactor", "compound_synthesizer", "organic_synthesizer",
                "element_harvester"):
        gd.update("buildings", bid, supported_recipes=[r["id"] for r in recipes if bid in (r.get("valid_buildings") or [])])
    # fields
    costs = {e["id"]: e["cost_base_currency"] for e in entries}
    node_of_field = {f: n[0] for n in tl.CHEM_NODES for f in n[5]}
    for fid, (name, drop, colour, codex) in NEW_FIELDS.items():
        f = {"id": fid, "display_name": name, "field_type": "Element", "world": "world_chemistry",
             "drops": [{"item": drop, "weight": 1.0}], "tap_cooldown_seconds": 2.0, "field_color": colour,
             "field_icon_path": "TODO", "purchase_research": node_of_field[fid],
             "purchase_cost_base": int(ep.nice(costs[node_of_field[fid]] * 0.05)), "purchase_cost_multiplier": 3.0,
             "purchase_group_size": 1, "codex_entry": codex + " Bought from the Build menu.", "planned": True}
        if gd.has("fields", fid):
            gd.put("fields", f)
        else:
            gd.insert_after("fields", "nitrogen_gas_field", f)
    gd.update("fields", "element_field_mineral", drops=[{"item": "silicon", "weight": 1.0}],
              codex_entry="A bed of silica sand: silicon for the materials chain.")
    site = next(s for s in data["sites"] if s["id"] == "site_chem_lab")
    have = {f["field"] for f in site["field_overrides"]}
    fo = site["field_overrides"] + [{"field": f, "density_multiplier": 0.0} for f in NEW_FIELDS if f not in have]
    gd.update("sites", "site_chem_lab", field_overrides=fo)
    # research blocks: the six Chemistry phases slot in by the day they open
    old = {b["id"]: b for b in data["research_blocks"]}
    order = ["ignition", "foundations", "first_fusion", "stellar_forge", "chemistry_entry", "deep_burning",
             "chem_small_molecules", "actinide_descent", "chem_organics", "chem_sulfur_phosphorus", "metal_descent",
             "chem_building_blocks", "iron_age", "chem_molecules_of_life", "chemistry_world", "biology_world",
             "deep_physics", "synthetic_frontier"]
    phase_of = {v: k for k, v in CHEM_PHASE_BLOCK.items()}
    blocks = []
    for n, bid in enumerate(order, start=1):
        if bid in phase_of:
            ph = next(p for p in plan["phases"] if p["id"] == phase_of[bid])
            name, desc = CHEM_BLOCK_TEXT[bid]
            src = old.get(bid) or blocks[-1]
            b = {"id": bid, "name": name, "order": n,
                 "target_hours": round((ph["end"] - ph["start"]) * plan["activeHoursPerDay"], 2),
                 "anchor": False, "planned": True, "description": desc}
            for k in ("start_prestige_count", "start_prestige_currency_earned", "start_permanent_upgrades",
                      "start_entropy", "start_sites"):
                b[k] = src.get(k, [] if k in ("start_permanent_upgrades", "start_sites") else 0)
        else:
            b = dict(old[bid], order=n)
        blocks.append(b)
    preset_keys = ("start_prestige_count", "start_prestige_currency_earned", "start_permanent_upgrades", "start_entropy")
    for prev, b in zip(blocks, blocks[1:]):
        if (b.get("start_prestige_currency_earned") or 0) < (prev.get("start_prestige_currency_earned") or 0):
            for k in preset_keys:
                b[k] = prev.get(k)
        if (b.get("start_entropy") or 0) < (prev.get("start_entropy") or 0):
            b["start_entropy"] = prev.get("start_entropy")
    _replace_blocks(gd, data["research_blocks"], blocks)
    gd.save()


# ── Phase 3: Biology on the life chemicals, and the Metallurgy world ─────────────────────────────────────
RETIRE_P3_ITEMS = ["amino_acid", "fatty_acid", "nucleotide", "uranium_hexafluoride"]
REMOVED_RESEARCH_P3 = {"biochem_precursors": "energy_currency", "reaction_engineering": "ore_prospecting",
                       "advanced_molecules": "roasting", "materials_science": "steelmaking",
                       "advanced_materials": "metamaterials"}
RETIRED_FIELDS_P3 = ["element_field_metal", "element_field_mineral"]
SHARED_LIFE = ["dipeptide", "glucose", "phospholipid", "amp", "atp"]       # made in Chemistry, used in Biology
BIO_FIELDS = {  # field: (display, drop, field id kept?)
    "sugar_field": ("Sugar Pool", "glucose"), "amino_acid_field": ("Peptide Pool", "dipeptide"),
    "lipid_field": ("Lipid Film", "phospholipid"), "nucleotide_field": ("Nucleotide Pool", "amp"),
    "atp_field": ("ATP Spring", "atp"),
}
BIO_PHASE_BLOCK = {"b1": "bio_biomolecules", "b2": "bio_cells", "b3": "bio_multicellular", "b4": "bio_ecosystems"}
METAL_PHASE_BLOCK = {"m1": "metallurgy_ores", "m2": "metallurgy_alloys", "m3": "metallurgy_advanced"}
P3_BLOCK_TEXT = {
    "bio_biomolecules": ("Biomolecules", "Proteins, carbohydrates, membranes and nucleic acids from the molecules of life."),
    "bio_cells": ("Cells", "Ribosomes, mitochondria and membranes, then the first cell."),
    "bio_multicellular": ("Multicellular Life", "Cells into tissues, tissues into organs."),
    "bio_ecosystems": ("Ecosystems", "Organisms, populations and a whole ecosystem: the Biology capstone."),
    "metallurgy_ores": ("Ores", "Mine iron, aluminium, copper and titanium ores, coal and quartz; roast the first oxide."),
    "metallurgy_alloys": ("Smelting & Alloys", "Steel, aluminium, copper wire, titanium alloy and carbon fibre."),
    "metallurgy_advanced": ("Advanced Materials", "Wafers, aerogel, superconductors and metamaterial: the Metallurgy capstone."),
}
ORE_FIELDS = {  # id: (display, drop, colour, codex)
    "hematite_field": ("Hematite Seam", "hematite", "#8E3B2E", "Red iron ore, Fe2O3, the ore most of the world's iron comes from."),
    "coal_field": ("Coal Seam", "coal", "#2E2E33", "Carbon laid down by ancient forests: the fuel and the carbon in steel."),
    "quartz_sand_field": ("Quartz Sand", "silica", "#E6DCC3", "Silica sand, SiO2: glass, wafers and aerogel start here."),
    "bauxite_field": ("Bauxite Bed", "bauxite", "#C27B4A", "Aluminium ore, smelted by electrolysis."),
    "chalcopyrite_field": ("Chalcopyrite Vein", "chalcopyrite", "#B8963A", "Copper iron sulfide: brassy gold, and the copper in every wire."),
    "rutile_field": ("Rutile Sands", "rutile", "#5A3E36", "Titanium dioxide crystals, reduced to titanium by the Kroll process."),
}
METAL_ITEMS = {  # new items: (display, symbol, category, raw)
    "hematite": ("Hematite", "Fe₂O₃", "RawResource", True), "bauxite": ("Bauxite", "Al(OH)₃", "RawResource", True),
    "chalcopyrite": ("Chalcopyrite", "CuFeS₂", "RawResource", True), "rutile": ("Rutile", "TiO₂", "RawResource", True),
    "coal": ("Coal", "C", "RawResource", True), "aluminium_ingot": ("Aluminium Ingot", "Al", "Alloy", False),
    "copper_wire": ("Copper Wire", "Cu", "Alloy", False),
}
METAL_CODEX = {
    "hematite": "Iron ore. Roasted, then reduced with carbon into steel.",
    "bauxite": "The ore of aluminium, a metal too reactive to find free.",
    "chalcopyrite": "The most common copper ore.",
    "rutile": "Titanium dioxide: strong, light titanium is locked inside.",
    "coal": "Nearly pure carbon. Steel needs it; so does carbon fibre.",
    "aluminium_ingot": "Aluminium won by electrolysis: light, and it never rusts through.",
    "copper_wire": "Drawn copper, the conductor of every circuit.",
}
METAL_TASKS = {
    "world": "world_metallurgy", "deliver_in": "world_biology", "display_name": "Path to Metallurgy",
    "stages": [
        {"id": "metal_task_1", "display_name": "Building Blocks", "description": "Life is built from proteins, sugars and membranes. Send them to Maxwell's Demon.",
         "items": [{"item": "protein", "quantity": 200}, {"item": "carbohydrate", "quantity": 200}, {"item": "lipid_membrane", "quantity": 150}], "dialogue": ""},
        {"id": "metal_task_2", "display_name": "First Cells", "description": "A cell copies itself. Send the first ones.",
         "items": [{"item": "prokaryotic_cell", "quantity": 40}], "dialogue": ""},
        {"id": "metal_task_3", "display_name": "Bodies", "description": "Tissues and organs: life gets large.",
         "items": [{"item": "tissue", "quantity": 20}, {"item": "organ", "quantity": 10}], "dialogue": ""},
        {"id": "metal_task_4", "display_name": "A Living World", "description": "One ecosystem, whole. Life is done; now build with metal.",
         "items": [{"item": "ecosystem", "quantity": 1}], "dialogue": ""},
    ]}
MET_DIALOGUES = [
    {"id": "intro_world_metallurgy", "lines": [
        {"speaker_name": "The Architect", "text": "Life is complete, and it needs tools. Deep in the crust the elements you forged lie as ores: iron in hematite, copper in chalcopyrite, aluminium in bauxite, titanium in rutile.", "portrait_path": "TODO", "pause_game": True, "highlight_target": "", "action": ""},
        {"speaker_name": "The Architect", "text": "Metallurgy is open. Travel there from the Worlds panel. Smelt ores into steel and alloys, and the alloys into the materials a megastructure needs.", "portrait_path": "TODO", "pause_game": False, "highlight_target": "", "action": ""}]},
    {"id": "met_tut_welcome", "lines": [
        {"speaker_name": "The Architect", "text": "Welcome to the foundry. Here ores are mined, not made, and Maxwell's Demon is back to turn what you smelt into entropy.", "portrait_path": "TODO", "pause_game": True, "highlight_target": "", "action": ""},
        {"speaker_name": "The Architect", "text": "I have set aside enough entropy to open your first seam. Study Ore Prospecting first.", "portrait_path": "TODO", "pause_game": False, "highlight_target": "", "action": ""}]},
    {"id": "met_tut_ore", "lines": [
        {"speaker_name": "The Architect", "text": "Hematite, red iron ore. Place a seam anywhere on the map.", "portrait_path": "TODO", "pause_game": False, "highlight_target": "", "action": ""}]},
    {"id": "met_tut_harvester", "lines": [
        {"speaker_name": "The Architect", "text": "A Metal Harvester on the seam digs the ore out. Point its output at the Demon.", "portrait_path": "TODO", "pause_game": False, "highlight_target": "", "action": ""}]},
    {"id": "met_tut_next", "lines": [
        {"speaker_name": "The Architect", "text": "Ore reaches the Demon. Next, Roasting opens the Materials Forge: two hematite make iron oxide, and iron oxide with coal makes steel.", "portrait_path": "TODO", "pause_game": False, "highlight_target": "", "action": ""}]},
]


def phase3(gd_json, apply):
    plan = ep.build(ep.load())
    rows = {r["id"]: r for r in plan["proposal"] if r["world"] in ("world_biology", "world_metallurgy") and r.get("d") is not None}
    prices = {r["id"]: r for r in plan["proposal"] if r.get("value") is not None}
    items = {i["id"]: i for i in gd_json["items"]}
    recipes = {r["id"]: r for r in gd_json["recipes"]}
    research = {r["id"]: r for r in gd_json["research"]}
    next_item = max(i["item_id"] for i in gd_json["items"]) + 1
    next_recipe = max(r["recipe_id"] for r in gd_json["recipes"]) + 1
    nodes = tl.BIO_NODES + tl.METAL_NODES
    node_of = {iid: n[0] for n in nodes for iid in n[3]}
    phase_of_node = {n[0]: n[2] for n in nodes}
    out = []
    bio_wage = ep.wage_at(plan["prices"], 30 * plan["activeHoursPerDay"])

    # items
    new_items, updates = [], {}
    for iid, r in rows.items():
        world = r["world"]
        if iid in items:
            updates[iid] = {"base_sell_value": float(r["value"]), "planned": True, "world": world}
            if world == "world_metallurgy" and iid == "silica":
                updates[iid].update({"is_harvested": True, "field_type": "Ore"})
            continue
        name, symbol, cat, raw = METAL_ITEMS[iid]
        new_items.append({
            "id": iid, "display_name": name, "symbol": symbol, "icon_path": "TODO", "item_id": next_item, "tier": 4,
            "tier_ref": "tier_4", "category": cat, "atomic_number": 0, "atomic_mass": 0, "charge": "0",
            "is_radioactive": False, "decay_type": "None", "half_life_note": "", "is_fissile": False,
            "is_harvested": raw, "field_type": "Ore" if raw else "None", "is_secondary_particle": False,
            "particle_uses": 0, "codex_entry": METAL_CODEX[iid], "base_sell_value": float(r["value"]),
            "isotope_sell_multiplier": 1.0, "world": world, "planned": True})
        next_item += 1
    retired = {i: (items[i]["item_id"], int(items[i]["base_sell_value"])) for i in RETIRE_P3_ITEMS if i in items}
    out.append(f"items: {len(new_items)} new, {len(updates)} repriced/moved, retire {sorted(retired)}")

    # recipes: Biology crafts, Metallurgy crafts and mines, Biology field collects
    new_recipes, recipe_updates = [], {}
    forge = "materials_forge"
    bio_building = {r["output_item"]: (r.get("valid_buildings") or [None])[0] for r in gd_json["recipes"]}
    for iid, r in rows.items():
        node = node_of[iid]
        ins = [{"item": i["item"], "quantity": i["q"]} for i in r.get("inputs") or []]
        if not ins:       # a Metallurgy mine
            rid = "collect_" + iid
            body = {"display_name": METAL_ITEMS[iid][0] if iid in METAL_ITEMS else items[iid]["display_name"],
                    "tier": 4, "category": "Element", "inputs": [], "neutron_adjustment": [],
                    "output_item": iid, "output_quantity": 1, "byproducts": [], "base_craft_time": float(r["craft"]),
                    "manual_craft_time": float(r["craft"]), "power_cost_is_dynamic": False, "fixed_power_cost_ev": 0.0,
                    "valid_buildings": ["metal_harvester"], "can_craft_manually": True, "known_from_start": False,
                    "required_research": node, "unlocks_research": None, "is_simplified": False,
                    "simplification_note": "", "planned": True}
        else:
            rid = iid
            building = bio_building.get(iid) if r["world"] == "world_biology" else forge
            body = {"display_name": r["name"], "tier": 4, "category": "Alloy" if r["world"] == "world_metallurgy" else "Molecule",
                    "inputs": ins, "neutron_adjustment": [], "output_item": iid, "output_quantity": int(r.get("outQ") or 1),
                    "byproducts": [], "base_craft_time": float(r["craft"]), "manual_craft_time": 0.0,
                    "power_cost_is_dynamic": False, "fixed_power_cost_ev": 0.0, "valid_buildings": [building],
                    "can_craft_manually": False, "known_from_start": False, "required_research": node,
                    "unlocks_research": None, "is_simplified": True, "simplification_note": r.get("how") or "",
                    "planned": True}
        if rid in recipes:
            recipe_updates[rid] = body
        else:
            new_recipes.append(dict({"id": rid, "recipe_id": next_recipe}, **body))
            next_recipe += 1
    for fid, (name, drop) in BIO_FIELDS.items():
        rid = "collect_" + drop
        t = float(max(10, round(prices[drop]["value"] / bio_wage)))
        body = {"display_name": items.get(drop, {}).get("display_name", drop), "tier": 4, "category": "Element",
                "inputs": [], "neutron_adjustment": [], "output_item": drop, "output_quantity": 1, "byproducts": [],
                "base_craft_time": t, "manual_craft_time": t, "power_cost_is_dynamic": False, "fixed_power_cost_ev": 0.0,
                "valid_buildings": ["organic_harvester"], "can_craft_manually": True, "known_from_start": False,
                "required_research": "biology_lab", "unlocks_research": None, "is_simplified": False,
                "simplification_note": "", "planned": True}
        if rid in recipes:
            recipe_updates[rid] = body
        else:
            new_recipes.append(dict({"id": rid, "recipe_id": next_recipe}, **body))
            next_recipe += 1
    # silica was made from Silicon in Chemistry; it is mined as quartz sand now (collect_silica)
    removed_recipes = sorted({*RETIRE_P3_ITEMS, "collect_amino_acid", "collect_fatty_acid", "collect_nucleotide",
                              "collect_iron", "collect_copper", "collect_aluminum", "collect_nickel", "collect_silicon",
                              "silica"} & set(recipes))
    out.append(f"recipes: {len(new_recipes)} new, {len(recipe_updates)} rewritten, remove {removed_recipes}")

    # research: two chains, costs summed and pooled
    entries = []
    for chain in (tl.BIO_NODES, tl.METAL_NODES):
        prev = "energy_currency" if chain is tl.BIO_NODES else None
        part = []
        for node, name, ph, iids, buildings, fields in chain:
            base = dict(research.get(node) or {})
            bio = ph.startswith("b")
            e = {"id": node, "display_name": name,
                 "description": base.get("description") or f"{name}: " + ", ".join(rows[i]["name"] for i in iids) + ".",
                 "branch": "Biology" if bio else "Materials", "prerequisites": [prev] if prev else [],
                 "depth_in_tree": base.get("depth_in_tree", 3 + len(part)),
                 "cost_base_currency": int(sum(rows[i]["research"] or 0 for i in iids)), "cost_prestige_currency": 0,
                 "duration_seconds": 1800 if bio else 2400, "unlocks_items": iids,
                 "unlocks_recipes": [("collect_" + i) if not rows[i].get("inputs") else i for i in iids],
                 "unlocks_buildings": buildings, "unlocks_grid_expansion": False, "unlocks_research": [],
                 "resets_on_prestige": True, "codex_entry": base.get("codex_entry") or name,
                 "icon_path": base.get("icon_path") or "TODO",
                 "block": (BIO_PHASE_BLOCK if bio else METAL_PHASE_BLOCK)[ph],
                 "world": "world_biology" if bio else "world_metallurgy", "planned": True}
            if not bio:
                e["required_world"] = "world_metallurgy"
            elif base.get("required_world"):
                e["required_world"] = base["required_world"]
            if node == "biology_lab":
                e["unlocks_recipes"] = e["unlocks_recipes"] + ["collect_" + d for _, d in BIO_FIELDS.values()]
            if "_comment" in base:
                e["_comment"] = base["_comment"]
            part.append(e)
            prev = node
        for e, c in zip(part, pav([e["cost_base_currency"] for e in part])):
            e["cost_base_currency"] = int(ep.nice(c))
        entries += part
    out.append("research: " + " > ".join(f"{e['id']}({e['cost_base_currency']:,})" for e in entries))
    if not apply:
        print("\n".join(out))
        print("C# RetiredItems lines:")
        for iid, (num, price) in retired.items():
            print(f"            {{ {num}, {price} }},  // {iid}")
        return

    gd = GameData()
    for rid in removed_recipes:
        gd.delete("recipes", rid)
    for iid in RETIRE_P3_ITEMS:
        if gd.has("items", iid):
            gd.delete("items", iid)
    last = json.loads(gd.text)["items"][-1]["id"]
    for it in new_items:
        gd.insert_after("items", last, it)
        last = it["id"]
    for iid, ch in updates.items():
        gd.update("items", iid, **ch)
    for iid in SHARED_LIFE:
        it = gd.get("items", iid)
        it.pop("world", None)
        gd.put("items", it)
    for rid, body in recipe_updates.items():
        gd.update("recipes", rid, **body)
    last = max(json.loads(gd.text)["recipes"], key=lambda r: r["recipe_id"])["id"]
    for rec in new_recipes:
        gd.insert_after("recipes", last, rec)
        last = rec["id"]
    for rid in REMOVED_RESEARCH_P3:
        if gd.has("research", rid):
            gd.delete("research", rid)
    for e in entries:
        if gd.has("research", e["id"]):
            gd.put("research", e)
        else:
            gd.insert_after("research", "ecosystems" if e["id"] == "ore_prospecting" else entries[entries.index(e) - 1]["id"], e)
    gd.save()
    finish_phase3(plan, entries)


def finish_phase3(plan, entries):
    gd = GameData()
    data = json.loads(gd.text)
    recipes = data["recipes"]
    rec_ids = {r["id"] for r in recipes}
    for r in data["research"]:
        pre = [REMOVED_RESEARCH_P3.get(p, p) for p in r.get("prerequisites") or []]
        pre = list(dict.fromkeys(pre))
        if pre != (r.get("prerequisites") or []):
            gd.update("research", r["id"], prerequisites=pre)
            r["prerequisites"] = pre
    children = {r["id"]: [] for r in data["research"]}
    for r in data["research"]:
        for p in r.get("prerequisites") or []:
            if p in children:
                children[p].append(r["id"])
    for r in data["research"]:
        if (r.get("unlocks_research") or []) != children[r["id"]]:
            gd.update("research", r["id"], unlocks_research=children[r["id"]])
    # buildings: Metallurgy's two move worlds; Biology and Metallurgy run without power (generators are Physics-only)
    gd.update("buildings", "metal_harvester", world="world_metallurgy", compatible_fields=["Ore"],
              required_research="ore_prospecting", requires_power=False,
              supported_recipes=[r["id"] for r in recipes if "metal_harvester" in (r.get("valid_buildings") or [])])
    gd.update("buildings", "materials_forge", world="world_metallurgy", required_research="roasting", requires_power=False,
              supported_recipes=[r["id"] for r in recipes if "materials_forge" in (r.get("valid_buildings") or [])])
    for bid in ("organic_harvester", "biosynthesizer", "cell_assembler", "tissue_culture", "bioreactor"):
        gd.update("buildings", bid, requires_power=False,
                  supported_recipes=[r["id"] for r in recipes if bid in (r.get("valid_buildings") or [])])
    for bid in ("element_harvester", "molecular_synthesizer", "organic_synthesizer", "catalytic_reactor",
                "compound_synthesizer"):
        gd.update("buildings", bid, supported_recipes=[r["id"] for r in recipes if bid in (r.get("valid_buildings") or [])])
    # fields: Chemistry's metal and mineral beds go; Biology's pools drop the life chemicals; Metallurgy's ores
    for fid in RETIRED_FIELDS_P3:
        if gd.has("fields", fid):
            gd.delete("fields", fid)
    for fid, (name, drop) in BIO_FIELDS.items():
        if gd.has("fields", fid):
            gd.update("fields", fid, display_name=name, drops=[{"item": drop, "weight": 1.0}])
        else:
            base = gd.get("fields", "nucleotide_field")
            gd.insert_after("fields", "nucleotide_field", dict(base, id=fid, display_name=name,
                            drops=[{"item": drop, "weight": 1.0}], field_color="#F2C14E",
                            codex_entry="ATP welling up from a vent of living chemistry: energy, ready to spend."))
    costs = {e["id"]: e["cost_base_currency"] for e in entries}
    node_of_field = {f: n[0] for n in tl.METAL_NODES for f in n[5]}
    last = "atp_field"
    for fid, (name, drop, colour, codex) in ORE_FIELDS.items():
        node = node_of_field[fid]
        base_cost = 1000 if node == "ore_prospecting" else int(ep.nice(costs[node] * 0.02))
        f = {"id": fid, "display_name": name, "field_type": "Ore", "world": "world_metallurgy",
             "drops": [{"item": drop, "weight": 1.0}], "tap_cooldown_seconds": 3.0, "field_color": colour,
             "field_icon_path": "TODO", "purchase_research": node, "purchase_cost_base": base_cost,
             "purchase_cost_multiplier": 2.0, "purchase_group_size": 1, "codex_entry": codex, "planned": True}
        gd.insert_after("fields", last, f)
        last = fid
    chem = next(s for s in data["sites"] if s["id"] == "site_chem_lab")
    gd.update("sites", "site_chem_lab", field_overrides=[f for f in chem["field_overrides"] if f["field"] not in RETIRED_FIELDS_P3])
    bio = next(s for s in data["sites"] if s["id"] == "site_bio_lab")
    if not any(f["field"] == "atp_field" for f in bio["field_overrides"]):
        gd.update("sites", "site_bio_lab", field_overrides=bio["field_overrides"] + [{"field": "atp_field", "density_multiplier": 2.0}])
    gd.insert_after("sites", "site_bio_lab", {
        "id": "site_foundry", "display_name": "Foundry", "unlock_cost": 0,
        "_note_metal": "First Metallurgy-world site (Item Timeline phase 3, 2026-11-23). Ore fields are listed at density 0 and bought after their research; the Demon is back.",
        "field_overrides": [{"field": "quark_field", "density_multiplier": 0.0}, {"field": "electron_field", "density_multiplier": 0.0}]
                           + [{"field": f, "density_multiplier": 0.0} for f in ORE_FIELDS]})
    gd.insert_after("worlds", "world_biology", {
        "id": "world_metallurgy", "display_name": "Metallurgy", "unlock_cost": 0,
        "_note_gate": "Item Timeline phase 3 (2026-11-23): delivery stages made in Biology (more_world_tasks) unlock it.",
        "prereq_unlock_ids": [], "unlock_by_tasks": True, "site_ids": ["site_foundry"],
        "theme": {"background_color": "#170f0a", "tile_color": "#33231a", "field_tint": "#ffd2a8"}})
    # Metallurgy's tutorial and dialogues
    grant = int(ep.nice(costs["ore_prospecting"] + 1000 + 2 * float(gd.get("buildings", "metal_harvester").get("entropy_cost") or 0) + 50000))
    gd.insert_after("world_tutorials", "tutorial_chemistry", {
        "id": "tutorial_metallurgy", "world": "world_metallurgy", "completion_message": "Metallurgy tutorial complete. The foundry is yours.",
        "steps": [
            {"id": "met_welcome", "hint": "", "advance_condition": {"type": "UiEvent", "ui_event_id": "dialogue_complete"}, "on_enter": {"dialogue_id": "met_tut_welcome"}},
            {"id": "met_grant", "hint": "", "advance_condition": {"type": "Auto"}, "on_enter": {}, "reward_entropy": grant},
            {"id": "met_research", "hint": "Open Research and study Ore Prospecting.", "advance_condition": {"type": "ResearchUnlocked", "research_id": "ore_prospecting"}, "on_enter": {"pulse_button_id": "btn-research"}},
            {"id": "met_place_ore", "hint": "Place a Hematite Seam from the Build menu.", "advance_condition": {"type": "FieldPurchasedMin", "field_id": "hematite_field", "min_count": 1}, "on_enter": {"dialogue_id": "met_tut_ore", "pulse_button_id": "btn-place-hematite_field"}},
            {"id": "met_place_harvester", "hint": "Place a Metal Harvester on the Hematite Seam.", "advance_condition": {"type": "BuildingMin", "building_id": "metal_harvester", "min_count": 1}, "on_enter": {"dialogue_id": "met_tut_harvester", "pulse_button_id": "btn-place-metal_harvester", "highlight_target": "hematite_field", "highlight_mode": "Full"}},
            {"id": "met_to_demon", "hint": "Belt the Harvester's output to Maxwell's Demon.", "advance_condition": {"type": "ConveyorLinked", "from_building_id": "metal_harvester", "building_id": "maxwells_demon", "min_count": 1}, "on_enter": {"pulse_button_id": "btn-place-conveyor"}},
            {"id": "met_next", "hint": "", "advance_condition": {"type": "UiEvent", "ui_event_id": "dialogue_complete"}, "on_enter": {"dialogue_id": "met_tut_next"}},
        ]})
    last = "intro_world_biology"
    for d in MET_DIALOGUES:
        if not gd.has("dialogues", d["id"]):
            gd.insert_after("dialogues", last, d)
        last = d["id"]
    # Metallurgy's delivery tasks (made in Biology): a new top-level list before world_tasks
    if '"more_world_tasks"' not in gd.text:
        from gamedata_edit import fmt_obj
        key = '\n  "world_tasks": {'
        i = gd.text.index(key)
        gd.text = gd.text[:i] + '\n  "more_world_tasks": [\n' + fmt_obj(METAL_TASKS) + '\n  ],' + gd.text[i:]
    # research blocks
    old = {b["id"]: b for b in data["research_blocks"]}
    order = ["ignition", "foundations", "first_fusion", "stellar_forge", "chemistry_entry", "deep_burning",
             "chem_small_molecules", "actinide_descent", "chem_organics", "chem_sulfur_phosphorus", "metal_descent",
             "chem_building_blocks", "iron_age", "chem_molecules_of_life", "bio_biomolecules", "bio_cells",
             "deep_physics", "bio_multicellular", "bio_ecosystems", "metallurgy_ores", "metallurgy_alloys",
             "metallurgy_advanced", "synthetic_frontier"]
    phase_of = {v: k for k, v in {**BIO_PHASE_BLOCK, **METAL_PHASE_BLOCK}.items()}
    blocks = []
    for n, bid in enumerate(order, start=1):
        if bid in phase_of:
            ph = next(p for p in plan["phases"] if p["id"] == phase_of[bid])
            name, desc = P3_BLOCK_TEXT[bid]
            src = old.get("biology_world") or blocks[-1]
            b = {"id": bid, "name": name, "order": n,
                 "target_hours": round((ph["end"] - ph["start"]) * plan["activeHoursPerDay"], 2),
                 "anchor": False, "planned": True, "description": desc}
            for k in ("start_prestige_count", "start_prestige_currency_earned", "start_permanent_upgrades",
                      "start_entropy", "start_sites"):
                b[k] = src.get(k, [] if k in ("start_permanent_upgrades", "start_sites") else 0)
        else:
            b = dict(old[bid], order=n)
        blocks.append(b)
    preset_keys = ("start_prestige_count", "start_prestige_currency_earned", "start_permanent_upgrades", "start_entropy")
    for prev, b in zip(blocks, blocks[1:]):
        if (b.get("start_prestige_currency_earned") or 0) < (prev.get("start_prestige_currency_earned") or 0):
            for k in preset_keys:
                b[k] = prev.get(k)
        if (b.get("start_entropy") or 0) < (prev.get("start_entropy") or 0):
            b["start_entropy"] = prev.get("start_entropy")
    _replace_blocks(gd, data["research_blocks"], blocks)
    gd.save()


if __name__ == "__main__":
    # Phases build on each other: start from release/0.6's game_data.json (git show origin/release/0.6:...), apply 1, 2, ... in order,
    # then run scripts/rebalance_curve.py --apply for the content not planned yet.
    phase = int(sys.argv[sys.argv.index("--phase") + 1]) if "--phase" in sys.argv else 1
    gd_json = json.loads((ep.ROOT / "Assets" / "Data" / "game_data.json").read_text(encoding="utf-8"))
    {1: phase1, 2: phase2, 3: phase3}[phase](gd_json, "--apply" in sys.argv)
