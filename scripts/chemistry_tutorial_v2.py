"""One-shot content edit (2026-11-22): Chemistry tutorial v2 + world items.

- Items get a `world` (the one world whose buildings or fields make them; items made in several worlds,
  or by shared buildings, stay shared). Inventory views hide items from other worlds.
- Hydrogen Seeps and Oxygen Vents are no longer scattered on the map: the player buys and places them
  from the Build menu (after the Chemistry Lab).
- site_chem_lab starts each run with a Calorimeter in the centre (9,9)-(10,10).
- Chemistry reaction buildings face their bank north (input facing South, output facing North), so at
  the default rotation they sit just south of a stream running east, like the Extractors (output North).
- The Chemistry tutorial is rewritten around that layout: a Spring at (5,11), a stream along row 10 from
  (5,10) to (8,10) into the Calorimeter, a Hydrogen Seep at (6,9), an Oxygen Vent at (7,9), a Molecular
  Synthesizer at (8,9). Highlights use the fixed tiles (cells:x,y). An up-front entropy grant covers the
  Chemistry Lab and every building until the first water reaches the Calorimeter.

Run from scripts/:  python chemistry_tutorial_v2.py   then Import Game Data in Unity.
"""
import collections
import json

from gamedata_edit import GameData, _scan_close

g = GameData()
data = json.loads(g.text)

# --- World of each item -------------------------------------------------------
bworld = {b["id"]: (b.get("world") or "") for b in data["buildings"]}
made_in = collections.defaultdict(set)
for r in data["recipes"]:
    worlds = {bworld.get(b, "") for b in (r.get("valid_buildings") or [])} or {""}
    for out in [r["output_item"]] + [b["item"] for b in (r.get("byproducts") or [])]:
        made_in[out] |= worlds
for f in data["fields"]:
    for drop in f.get("drops") or []:
        made_in[drop["item"]].add(f.get("world") or "")
for it in data["items"]:
    ws = made_in.get(it["id"], set())
    world = next(iter(ws)) if len(ws) == 1 else ""
    if (it.get("world") or "") != world:
        it["world"] = world
        g.put("items", it)

# --- Fields the player places ---------------------------------------------------
for fid, cost in (("hydrogen_gas_field", 1000), ("oxygen_gas_field", 1000)):
    g.update("fields", fid, purchase_research="chemistry_lab", purchase_cost_base=cost,
             purchase_cost_multiplier=1.5, purchase_group_size=1)

site = g.get("sites", "site_chem_lab")
for ov in site["field_overrides"]:
    if ov["field"] in ("hydrogen_gas_field", "oxygen_gas_field"):
        ov["density_multiplier"] = 0.0
site["starting_buildings"] = [{"building": "calorimeter", "x": 9, "y": 9, "rotation": 0}]
g.put("sites", site)

# --- Bank faces north -------------------------------------------------------------
BANK = [
    {"port_type": "Input", "local_cell": [0, 0], "local_facing": "South"},
    {"port_type": "Output", "local_cell": [0, 0], "local_facing": "North"},
]
for bid in ("molecular_synthesizer", "compound_synthesizer", "catalytic_reactor", "materials_forge",
            "organic_synthesizer"):
    g.update("buildings", bid, ports=BANK)

# --- Dialogues ---------------------------------------------------------------------
ARCH = "The Architect"


def line(text, pause=False):
    return {"speaker_name": ARCH, "text": text, "portrait_path": "TODO", "pause_game": pause,
            "highlight_target": "", "action": ""}


DIALOGUES = {
    "chem_tut_welcome": [
        line("Welcome to the lab. Chemistry plays by different rules: there is no power here, and no "
             "Maxwell's Demon. Water does the work, and the Calorimeter in the middle turns finished "
             "chemicals into entropy.", pause=True),
        line("Every reaction is an equation. Two hydrogen and one oxygen make two water. I have set aside "
             "enough entropy for your first stream; spend it on the Chemistry Lab first."),
    ],
    "chem_tut_spring": [
        line("First, water. A Spring wells it up. Place one on the glowing tile, up and to the left of "
             "the Calorimeter."),
    ],
    "chem_tut_channel": [
        line("Now draw a Channel from the tile below the Spring straight to the Calorimeter's side. "
             "That is your stream: it carries whatever is dropped into it down to the Calorimeter."),
        line("Remember this: the stream runs past buildings, in front of them, never into them. "
             "Everything you build goes beside the water."),
    ],
    "chem_tut_fields": [
        line("Hydrogen and oxygen seep up from the ground as gas. Place a Hydrogen Seep on the glowing "
             "tile just below the stream, then an Oxygen Vent on the tile beside it."),
    ],
    "chem_tut_extractors": [
        line("An Extractor on each vent draws the gas out and drops it into the water in front of it. "
             "Its arrow must point up at the stream; that is the side it drops into."),
    ],
    "chem_tut_bank": [
        line("Now a reaction. Place a Molecular Synthesizer on the glowing tile below the stream, "
             "downstream of the vents. Its arrows face the water too: it takes what its recipe needs "
             "as the stream passes, lets everything else float on, and drops what it makes back in."),
    ],
    "chem_tut_calorimeter": [
        line("Water is flowing to the Calorimeter. It burns whatever reaches it for entropy; finished "
             "compounds pay far more than raw gas. Tap it any time to burn what you carry, just like "
             "the Demon."),
    ],
    "chem_tut_flow": [
        line("One more rule: a Spring's flow is limited, and buildings share it nearest the source "
             "first. The bar at the top shows how much is used.", pause=True),
        line("When the last buildings run short of water, add another Spring and start a new stream. "
             "The lab is yours."),
    ],
}
for did, lines in DIALOGUES.items():
    if g.has("dialogues", did):
        g.update("dialogues", did, lines=lines)
    else:
        g.insert_after("dialogues", "intro_world_chemistry", {"id": did, "lines": lines})


# --- Tutorial steps ---------------------------------------------------------------------
def step(id_, hint, cond, on_enter=None, **extra):
    s = {"id": id_, "hint": hint, "advance_condition": cond, "on_enter": on_enter or {}}
    s.update(extra)
    return s


FULL = "Full"
# Chemistry Lab 676,000 + Spring 2,000 + Hydrogen Seep 1,000 + Oxygen Vent 1,000 + 2 Extractors 10,000
# + Molecular Synthesizer 5,000, with room for a little more.
GRANT = 700000

STEPS = [
    step("chem_welcome", "", {"type": "UiEvent", "ui_event_id": "dialogue_complete"},
         {"dialogue_id": "chem_tut_welcome"}),
    step("chem_grant", "", {"type": "Auto"}, reward_entropy=GRANT),
    step("chem_research_lab", "Open Research and study the Chemistry Lab.",
         {"type": "ResearchUnlocked", "research_id": "chemistry_lab"},
         {"pulse_button_id": "btn-research"}),
    step("chem_place_spring", "Place a Spring on the glowing tile.",
         {"type": "BuildingMin", "building_id": "spring", "min_count": 1},
         {"dialogue_id": "chem_tut_spring", "pulse_button_id": "btn-place-spring",
          "highlight_target": "cells:5,11", "highlight_mode": FULL}),
    step("chem_draw_channel",
         "Place a Channel: tap the glowing tile under the Spring, then the one beside the Calorimeter, and confirm.",
         {"type": "ConveyorLinked", "from_building_id": "spring", "building_id": "calorimeter", "min_count": 1},
         {"dialogue_id": "chem_tut_channel", "pulse_button_id": "btn-place-conveyor",
          "highlight_target": "cells:5,10>8,10", "highlight_mode": FULL}),
    step("chem_place_h2_field", "Place a Hydrogen Seep on the glowing tile below the stream.",
         {"type": "FieldPurchasedMin", "field_id": "hydrogen_gas_field", "min_count": 1},
         {"dialogue_id": "chem_tut_fields", "pulse_button_id": "btn-place-hydrogen_gas_field",
          "highlight_target": "cells:6,9", "highlight_mode": FULL}),
    step("chem_place_o2_field", "Place an Oxygen Vent on the glowing tile next to the Hydrogen Seep.",
         {"type": "FieldPurchasedMin", "field_id": "oxygen_gas_field", "min_count": 1},
         {"pulse_button_id": "btn-place-oxygen_gas_field",
          "highlight_target": "cells:7,9", "highlight_mode": FULL}),
    step("chem_place_extractors",
         "Place an Extractor on each vent with its arrow pointing up at the stream.",
         {"type": "BuildingMin", "building_id": "element_harvester", "min_count": 2},
         {"dialogue_id": "chem_tut_extractors", "pulse_button_id": "btn-place-element_harvester",
          "highlight_target": "hydrogen_gas_field", "highlight_mode": FULL}),
    step("chem_place_synth", "Place a Molecular Synthesizer on the glowing tile below the stream.",
         {"type": "BuildingMin", "building_id": "molecular_synthesizer", "min_count": 1},
         {"dialogue_id": "chem_tut_bank", "pulse_button_id": "btn-place-molecular_synthesizer",
          "highlight_target": "cells:8,9", "highlight_mode": FULL}),
    step("chem_set_water", "Tap the Synthesizer and set it to Water: 2 H2 + O2 -> 2 H2O.",
         {"type": "BuildingRecipeSet", "building_id": "molecular_synthesizer", "recipe_id": 31},
         {"highlight_target": "molecular_synthesizer", "highlight_mode": "VisualOnly"}),
    step("chem_first_water", "Watch the stream carry hydrogen and oxygen to the Synthesizer.",
         {"type": "ItemCraftedMin", "items": [{"item_id": 33, "quantity": 2}]}),
    step("chem_calorimeter", "", {"type": "UiEvent", "ui_event_id": "dialogue_complete"},
         {"dialogue_id": "chem_tut_calorimeter", "highlight_target": "calorimeter", "highlight_mode": "VisualOnly"}),
    step("chem_flow", "", {"type": "UiEvent", "ui_event_id": "dialogue_complete"},
         {"dialogue_id": "chem_tut_flow"}),
]

# Replace the steps inside world_tutorials (written by chemistry_tutorial.py).
key = '\n  "world_tutorials": ['
s = g.text.index(key) + len(key) - 1
e = _scan_close(g.text, s)
tut = {"id": "tutorial_chemistry", "world": "world_chemistry",
       "completion_message": "Chemistry tutorial complete. The lab is yours.", "steps": STEPS}
body = json.dumps([tut], indent=2, ensure_ascii=True)
body = "\n".join("  " + l if i else l for i, l in enumerate(body.splitlines()))
g.text = g.text[:s] + body + g.text[e + 1:]

g.save()
print("chemistry tutorial v2 applied")
