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


if __name__ == "__main__":
    phase = int(sys.argv[sys.argv.index("--phase") + 1]) if "--phase" in sys.argv else 1
    gd_json = json.loads((ep.ROOT / "Assets" / "Data" / "game_data.json").read_text(encoding="utf-8"))
    if phase != 1:
        sys.exit("only phase 1 is written so far")
    phase1(gd_json, "--apply" in sys.argv)
