"""One-shot content edit (2026-11-22): Chemistry streams.

Chemistry's rule tweak: no eV power. Belts there are water streams that start at a Spring;
buildings sit beside a stream, take only what their recipe uses and drop products back in.
Every building on a stream draws flow from it, upstream first. A Calorimeter at the mouth
burns what arrives for entropy (Maxwell's Demon is not used in Chemistry).

Run from the repo root:  python scripts/chemistry_streams.py
Then Import Game Data in Unity.
"""
from gamedata_edit import GameData

g = GameData()

# --- World rule -------------------------------------------------------------
g.update("worlds", "world_chemistry", stream_transport=True)

# --- Chemistry buildings: no power, flow draw, one bank side ----------------
# Bank side = South: the stream runs along the building's south edge. The input port faces
# North (items travel north off the stream into the building), the output faces South.
BANK_PORTS = [
    {"port_type": "Input", "local_cell": [0, 0], "local_facing": "North"},
    {"port_type": "Output", "local_cell": [0, 0], "local_facing": "South"},
]
FLOW = {
    "molecular_synthesizer": 3.0,
    "compound_synthesizer": 3.0,
    "catalytic_reactor": 4.0,
    "materials_forge": 4.0,
    "organic_synthesizer": 5.0,
}
for bid, draw in FLOW.items():
    b = g.get("buildings", bid)
    b["requires_power"] = False
    b["base_power_cost_ev"] = 0.0
    b["flow_draw"] = draw
    b["ports"] = BANK_PORTS
    b["input_slot_count"] = 1
    b["input_slot_labels"] = ["Stream"]
    for u in b.get("upgrade_levels", []):
        u["power_cost_ev"] = 0.0
    g.put("buildings", b)

for bid in ("element_harvester", "metal_harvester"):
    g.update("buildings", bid, flow_draw=1.0)

# No power in Chemistry: generators and relays are Physics-only.
for bid in ("basic_generator", "power_relay"):
    g.update("buildings", bid, world="world_physics")

# --- New buildings ----------------------------------------------------------
template = g.get("buildings", "splitter_2")


def building(**kw):
    o = dict(template)
    o.pop("_comment", None)
    o.update(upgrade_levels=[], storage_upgrade_levels=[], input_upgrade_levels=[],
             is_splitter=False, structure_kind="None", supported_recipes=[], input_slot_count=0, input_slot_labels=[],
             world="world_chemistry", required_research="chemistry_lab", available_from_start=False)
    o.update(kw)
    return o


if not g.has("buildings", "spring"):
    g.insert_after("buildings", "splitter_3", building(
        id="spring", display_name="Spring", building_id=29, category="Core",
        description="Wells up clean water. Draw a channel from it and the stream carries whatever "
                    "you drop in past every building on its banks. Its flow is shared by those buildings, upstream first.",
        is_spring=True, stream_flow=10.0, flow_draw=0.0, footprint=[1, 1],
        ports=[{"port_type": "Output", "local_cell": [0, 0], "local_facing": "South"}],
        entropy_cost=2000, base_max_output_items=0, base_max_input_items_per_slot=0,
        tutorial_note="Chemistry streams: every channel starts at a Spring.",
        codex_entry="A spring is groundwater forced up through rock. Its flow sets how many "
                    "reactors one stream can feed."))

if not g.has("buildings", "calorimeter"):
    ports = []
    for x in (0, 1):
        ports.append({"port_type": "Input", "local_cell": [x, 0], "local_facing": "North"})
        ports.append({"port_type": "Input", "local_cell": [x, 1], "local_facing": "South"})
    for y in (0, 1):
        ports.append({"port_type": "Input", "local_cell": [0, y], "local_facing": "East"})
        ports.append({"port_type": "Input", "local_cell": [1, y], "local_facing": "West"})
    g.insert_after("buildings", "spring", building(
        id="calorimeter", display_name="Calorimeter", building_id=30, category="Core",
        description="Sits at a stream's mouth and burns whatever reaches it, turning the energy "
                    "stored in its bonds into entropy. Finished compounds pay far more than raw gas.",
        is_entropy_sink=True, is_placed_sink=True, flow_draw=0.0, footprint=[2, 2], ports=ports,
        entropy_cost=5000, base_max_output_items=0, base_max_input_items_per_slot=50,
        tutorial_note="Chemistry's sink. Without one, a stream's mouth sells leftovers at the surplus rate.",
        codex_entry="A bomb calorimeter measures the heat a substance releases when it burns: "
                    "its enthalpy of combustion."))

# --- Recipes ------------------------------------------------------------------
# Fix: the four feedstock collect recipes copied collect_sodium's recipe_id 167.
for rid, new_id in (("collect_hydrogen_gas", 291), ("collect_oxygen_gas", 292),
                    ("collect_graphite", 293), ("collect_nitrogen_gas", 294)):
    g.update("recipes", rid, recipe_id=new_id)

if not g.has("recipes", "methane_combustion"):
    r = g.get("recipes", "carbon_dioxide")
    r.update(id="methane_combustion", display_name="Carbon Dioxide", recipe_id=295,
             inputs=[{"item": "methane", "quantity": 1}, {"item": "oxygen_gas", "quantity": 2}],
             output_item="carbon_dioxide", output_quantity=1,
             byproducts=[{"item": "water", "quantity": 2}],
             base_craft_time=30.0, required_research="nitrogen_fixation",
             method_label="Combustion")
    g.insert_after("recipes", "carbon_dioxide", r)
    b = g.get("buildings", "molecular_synthesizer")
    if "methane_combustion" not in b["supported_recipes"]:
        b["supported_recipes"].append("methane_combustion")
        g.put("buildings", b)

# --- Dialogue -----------------------------------------------------------------
d = g.get("dialogues", "intro_world_chemistry")
if len(d["lines"]) == 2:
    line = dict(d["lines"][1])
    d["lines"].append(dict(line, text=(
        "There is no power here. Water does the work. Place a Spring and draw a channel from it: "
        "the stream carries whatever your Extractors drop in, and every building on its bank takes "
        "only what its reaction needs and lets the rest flow on.")))
    d["lines"].append(dict(line, text=(
        "Each Spring has a limited flow, shared upstream first, so a long stream starves its last "
        "buildings. Put a Calorimeter at the mouth to burn what arrives for entropy; anything that "
        "reaches an open mouth is sold off cheaply.")))
    g.put("dialogues", d)

g.save()
print("chemistry streams applied")
