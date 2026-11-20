"""Chemistry runs on natural feedstock (2026-11-19).

    python scripts/chemistry_feedstock.py

One-shot content edit, kept for the record. Chemistry used to mine loose atoms (hydrogen, carbon, nitrogen,
oxygen) from a "Light Element Seam". Elements are not found like that: hydrogen, oxygen and nitrogen occur as
gases (H2, O2, N2) and carbon as graphite. This script:
- adds the four feedstock items and a field for each (H2 and O2 on the Chemistry map from the start, graphite
  and N2 bought from the Build menu once their research is done) and retires the Light Element Seam;
- turns the Element Harvester into the Extractor that draws them;
- reorders the first Chemistry research: Chemistry Lab (H2, O2, water) -> Carbon Chemistry (graphite, methane,
  CO2) -> Nitrogen Fixation (N2, ammonia) -> Reaction Engineering, which now also brings the minerals
  (salt, sulfuric acid) and the Compound Synthesizer;
- rewrites every Chemistry recipe that took atomic H/C/N/O to take the feedstock, balanced;
- tags every field with its world, and the Physics production buildings with world_physics.
Run scripts/rebalance_curve.py --apply afterwards to price the new items and research.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from gamedata_edit import GameData  # noqa: E402

g = GameData()
CHEM, PHYS, BIO = "world_chemistry", "world_physics", "world_biology"


def item(id_, item_id, name, symbol, codex):
    o = dict(g.get("items", "water"))
    o.pop("_comment", None)
    o.update(id=id_, display_name=name, symbol=symbol, item_id=item_id, category="RawResource",
             is_harvested=True, field_type="Element", codex_entry=codex, base_sell_value=1.0)
    return o


def collect(id_, out, research, t):
    o = dict(g.get("recipes", "collect_sodium"))
    o.pop("_comment", None)
    o.update(id=id_, display_name=g.get("items", out)["display_name"], output_item=out, required_research=research,
             base_craft_time=t, manual_craft_time=t)
    return o


def field(id_, name, drop, color, codex, research="", cost=0):
    o = {"id": id_, "display_name": name, "field_type": "Element", "world": CHEM,
         "drops": [{"item": drop, "weight": 1.0}], "tap_cooldown_seconds": 2.0, "field_color": color,
         "field_icon_path": "TODO"}
    if research:
        o.update(purchase_research=research, purchase_cost_base=cost, purchase_cost_multiplier=3.0, purchase_group_size=1)
    o["codex_entry"] = codex
    return o


# ── Items ────────────────────────────────────────────────────────────────────
new_items = [
    item("hydrogen_gas", 187, "Hydrogen Gas", "H₂",
         "Hydrogen as it is found: paired into H₂ molecules, the lightest gas there is. Chemistry's first feedstock."),
    item("oxygen_gas", 188, "Oxygen Gas", "O₂",
         "Diatomic oxygen, the reactive fifth of the air. With hydrogen it burns to water."),
    item("graphite", 189, "Graphite", "C",
         "Carbon as it is mined: sheets of hexagonal rings, soft enough to write with. The backbone of every organic molecule."),
    item("nitrogen_gas", 190, "Nitrogen Gas", "N₂",
         "Four fifths of the air, held together by one of the strongest bonds in chemistry. Breaking it makes ammonia."),
]
after = "water"
for o in new_items:
    if not g.has("items", o["id"]):
        g.insert_after("items", after, o)
    after = o["id"]

# ── Harvest recipes ──────────────────────────────────────────────────────────
for old in ("collect_hydrogen", "collect_carbon", "collect_oxygen", "collect_nitrogen"):
    if g.has("recipes", old):
        g.delete("recipes", old)
anchor = "collect_sodium"
for rid, out, res, t in [("collect_hydrogen_gas", "hydrogen_gas", "chemistry_lab", 4.0),
                         ("collect_oxygen_gas", "oxygen_gas", "chemistry_lab", 4.0),
                         ("collect_graphite", "graphite", "molecular_synthesis", 6.0),
                         ("collect_nitrogen_gas", "nitrogen_gas", "nitrogen_fixation", 6.0)]:
    if not g.has("recipes", rid):
        o = collect(rid, out, res, t)
        # insert before the minerals: after the previous new one, else before collect_sodium's position
        j, _, _ = g._find("recipes", anchor)
        start = g.text.rfind("\n", 0, j) + 1
        from gamedata_edit import fmt_obj
        g.text = g.text[:start] + fmt_obj(o) + ",\n" + g.text[start:]

# ── Chemistry recipes: atomic H/C/N/O -> feedstock (balanced; output_quantity where it makes two) ────────────
def set_recipe(rid, inputs, out_q=1, research=None):
    ch = {"inputs": [{"item": i, "quantity": q} for i, q in inputs], "output_quantity": out_q}
    if research: ch["required_research"] = research
    g.update("recipes", rid, **ch)

set_recipe("liquid_hydrogen", [("hydrogen_gas", 5)], research="chemistry_lab")
set_recipe("water", [("hydrogen_gas", 2), ("oxygen_gas", 1)], 2, research="chemistry_lab")
set_recipe("methane", [("graphite", 1), ("hydrogen_gas", 2)])
set_recipe("ammonia", [("nitrogen_gas", 1), ("hydrogen_gas", 3)], 2, research="nitrogen_fixation")
set_recipe("carbon_dioxide", [("graphite", 1), ("oxygen_gas", 1)], research="molecular_synthesis")
g.update("recipes", "carbon_dioxide", valid_buildings=["molecular_synthesizer"])
set_recipe("silica", [("silicon", 1), ("oxygen_gas", 1)])
set_recipe("iron_oxide", [("iron", 4), ("oxygen_gas", 3)], 2)
set_recipe("table_salt", [("sodium", 1), ("chlorine", 1)], research="reaction_engineering")
set_recipe("sulfuric_acid", [("sulfur", 1), ("oxygen_gas", 2), ("hydrogen_gas", 1)], research="reaction_engineering")
set_recipe("sodium_hydroxide", [("sodium", 2), ("oxygen_gas", 1), ("hydrogen_gas", 1)], 2)
set_recipe("nitric_acid", [("nitrogen_gas", 1), ("oxygen_gas", 3), ("hydrogen_gas", 1)], 2)
set_recipe("ethane", [("graphite", 2), ("hydrogen_gas", 3)])
set_recipe("ethanol", [("ethane", 2), ("oxygen_gas", 1)], 2)
set_recipe("fatty_acid", [("octane", 2), ("oxygen_gas", 1)])
set_recipe("glucose", [("graphite", 6), ("hydrogen_gas", 6), ("oxygen_gas", 3)])
set_recipe("amino_acid", [("graphite", 4), ("hydrogen_gas", 5), ("nitrogen_gas", 1), ("oxygen_gas", 2)], 2)
set_recipe("nucleotide", [("glucose", 1), ("nitrogen_gas", 1), ("phosphorus", 1)])
for rid in ("collect_sodium", "collect_chlorine", "collect_sulfur", "collect_silicon"):
    g.update("recipes", rid, required_research="reaction_engineering")

# ── Buildings ────────────────────────────────────────────────────────────────
g.update("buildings", "element_harvester", display_name="Extractor",
         description="Draws raw feedstock from Chemistry's fields: hydrogen, oxygen and nitrogen gas, graphite, and minerals.",
         supported_recipes=["collect_hydrogen_gas", "collect_oxygen_gas", "collect_graphite", "collect_nitrogen_gas",
                            "collect_sodium", "collect_chlorine", "collect_sulfur", "collect_silicon"])
ms = g.get("buildings", "molecular_synthesizer")["supported_recipes"]
if "carbon_dioxide" not in ms:
    g.update("buildings", "molecular_synthesizer", supported_recipes=ms[:4] + ["carbon_dioxide"] + ms[4:],
             input_slot_labels=["Primary Feedstock", "Secondary Feedstock"])
g.update("buildings", "compound_synthesizer", supported_recipes=["table_salt", "sulfuric_acid"],
         description="Combines mined minerals with Chemistry's gases into salts and acids.",
         required_research="reaction_engineering")
# A building's own gate must match the research that lists it (the Build menu reads required_research).
g.update("buildings", "molecular_synthesizer", required_research="chemistry_lab")
for b in ("harvester", "nucleon_harvester", "strong_force_combiner", "atomic_assembler", "isotopic_manipulator",
          "radioactive_containment", "fusion_reactor", "fission_reactor", "neutron_capture"):
    g.update("buildings", b, world=PHYS)

# ── Research ─────────────────────────────────────────────────────────────────
g.update("research", "chemistry_lab",
         description="Open the Chemistry world: draw hydrogen and oxygen gas from its fields with the Extractor and burn them into water in the Molecular Synthesizer.",
         unlocks_items=["hydrogen_gas", "oxygen_gas", "water", "liquid_hydrogen"],
         unlocks_recipes=["collect_hydrogen_gas", "collect_oxygen_gas", "water", "liquid_hydrogen"],
         unlocks_buildings=["element_harvester", "molecular_synthesizer"],
         unlocks_research=["molecular_synthesis"])
g.update("research", "molecular_synthesis", display_name="Carbon Chemistry",
         description="Buy a Graphite Deposit and bond its carbon with hydrogen into methane, or burn it into carbon dioxide.",
         unlocks_items=["graphite", "methane", "carbon_dioxide"],
         unlocks_recipes=["collect_graphite", "methane", "carbon_dioxide"],
         unlocks_buildings=[],
         unlocks_research=["nitrogen_fixation", "advanced_molecules"])
if not g.has("research", "nitrogen_fixation"):
    o = dict(g.get("research", "molecular_synthesis"))
    o.pop("_comment", None)
    o.update(id="nitrogen_fixation", display_name="Nitrogen Fixation",
             description="Buy a Nitrogen Vent and split the N₂ triple bond with hydrogen to make ammonia.",
             prerequisites=["molecular_synthesis"], depth_in_tree=4,
             unlocks_items=["nitrogen_gas", "ammonia"], unlocks_recipes=["collect_nitrogen_gas", "ammonia"],
             unlocks_buildings=[], unlocks_research=["reaction_engineering"],
             codex_entry="Nitrogen gas is nearly inert: its triple bond is one of the strongest in chemistry. "
                         "Fixing it into ammonia, with hydrogen under heat and pressure, feeds most of the world's fertiliser.")
    g.insert_after("research", "molecular_synthesis", o)
re = g.get("research", "reaction_engineering")
g.update("research", "reaction_engineering", prerequisites=["nitrogen_fixation"],
         description="Mine minerals and metals, make salts and acids in the Compound Synthesizer, and run catalysed reactions.",
         unlocks_items=["table_salt", "sulfuric_acid"] + [i for i in re["unlocks_items"] if i not in ("table_salt", "sulfuric_acid")],
         unlocks_recipes=["collect_sodium", "collect_chlorine", "collect_sulfur", "collect_silicon", "table_salt", "sulfuric_acid"]
                         + [r for r in re["unlocks_recipes"] if r not in ("table_salt", "sulfuric_acid")],
         unlocks_buildings=["compound_synthesizer"] + [b for b in re["unlocks_buildings"] if b != "compound_synthesizer"])

# ── Fields ───────────────────────────────────────────────────────────────────
if g.has("fields", "element_field_light"):
    g.delete("fields", "element_field_light")
new_fields = [
    field("hydrogen_gas_field", "Hydrogen Seep", "hydrogen_gas", "#9AD1F5",
          "Hydrogen gas bubbling up from below. Extract it for water, methane and ammonia."),
    field("oxygen_gas_field", "Oxygen Vent", "oxygen_gas", "#E57373",
          "A vent breathing out oxygen gas. Burn it with hydrogen to make water."),
    field("graphite_field", "Graphite Deposit", "graphite", "#5F6B73",
          "A seam of graphite, nearly pure carbon. Bought from the Build menu after Carbon Chemistry.",
          research="molecular_synthesis", cost=200000),
    field("nitrogen_gas_field", "Nitrogen Vent", "nitrogen_gas", "#9FA8DA",
          "A vent of nitrogen gas. Bought from the Build menu after Nitrogen Fixation.",
          research="nitrogen_fixation", cost=500000),
]
after = "neutron_field"
for o in new_fields:
    if not g.has("fields", o["id"]):
        g.insert_after("fields", after, o)
    after = o["id"]
for f, w in [("quark_field", PHYS), ("electron_field", PHYS), ("uranium_field", PHYS), ("plutonium_field", PHYS),
             ("neutron_field", PHYS), ("element_field_metal", CHEM), ("element_field_mineral", CHEM),
             ("sugar_field", BIO), ("amino_acid_field", BIO), ("lipid_field", BIO), ("nucleotide_field", BIO)]:
    g.update("fields", f, world=w)
# Minerals and metals come with Reaction Engineering instead of lying on the map from the start.
g.update("fields", "element_field_mineral", purchase_research="reaction_engineering", purchase_cost_base=2000000,
         purchase_cost_multiplier=3.0, purchase_group_size=1)
g.update("fields", "element_field_metal", purchase_research="reaction_engineering", purchase_cost_base=4000000,
         purchase_cost_multiplier=3.0, purchase_group_size=1)

# ── Chemistry map: start with hydrogen and oxygen only ───────────────────────
# Fields bought later are listed at density 0: FieldGenerator only knows (sells, restores from a save) the fields a
# site names, and 0 places none up front.
g.update("sites", "site_chem_lab", field_overrides=[
    {"field": "quark_field", "density_multiplier": 0.0},
    {"field": "electron_field", "density_multiplier": 0.0},
    {"field": "hydrogen_gas_field", "density_multiplier": 3.0},
    {"field": "oxygen_gas_field", "density_multiplier": 2.0},
    {"field": "graphite_field", "density_multiplier": 0.0},
    {"field": "nitrogen_gas_field", "density_multiplier": 0.0},
    {"field": "element_field_mineral", "density_multiplier": 0.0},
    {"field": "element_field_metal", "density_multiplier": 0.0},
])

g.save()
print("ok")
