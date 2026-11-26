"""One-shot content edit (2026-11-22): Chemistry's own tutorial.

Chemistry runs its own short tutorial the first time the player is there (world_tutorials ->
WorldSO.tutorialFlow); the Physics tutorial never runs in Chemistry. The world-unlock intro
dialogue goes back to a short announcement, and the tutorial teaches streams step by step.

Run from scripts/:  python chemistry_tutorial.py   then Import Game Data in Unity.
"""
import json

from gamedata_edit import GameData, fmt_obj, _scan_close

g = GameData()
ARCH = "The Architect"


def line(text, pause=False):
    return {"speaker_name": ARCH, "text": text, "portrait_path": "TODO", "pause_game": pause,
            "highlight_target": "", "action": ""}


DIALOGUES = {
    "chem_tut_welcome": [
        line("Welcome to the lab. Chemistry plays by different rules: there is no power here, and no "
             "Maxwell's Demon. Water does the work.", pause=True),
        line("Every reaction is an equation. Two hydrogen and one oxygen make two water, nitrogen and "
             "three hydrogen make two ammonia. Your buildings follow those recipes exactly."),
    ],
    "chem_tut_spring": [
        line("First, water. A Spring wells it up, and a Channel drawn from it becomes a stream that "
             "carries whatever you drop in."),
    ],
    "chem_tut_extractors": [
        line("Raw elements come as nature leaves them: hydrogen and oxygen gas seep from vents. Put an "
             "Extractor on a Hydrogen Seep and one on an Oxygen Vent, beside your channel, and point "
             "their output at the water."),
    ],
    "chem_tut_bank": [
        line("Now a reaction. Buildings sit on the bank of the stream. As items float past, each one "
             "takes only what its recipe needs and lets everything else flow on, then drops what it "
             "makes back into the water."),
        line("Place a Molecular Synthesizer with its bank side on the channel, downstream of your "
             "Extractors, so the gases reach it."),
    ],
    "chem_tut_calorimeter": [
        line("Where the stream ends, place a Calorimeter. It burns whatever reaches it and turns the "
             "energy in its bonds into entropy; finished compounds pay far more than raw gas. An open "
             "mouth only sells leftovers cheaply."),
    ],
    "chem_tut_flow": [
        line("The stream is working. One more rule: a Spring's flow is limited, and buildings share it "
             "nearest the source first. The bar at the top shows how much is used.", pause=True),
        line("When the last buildings run short of water, add another Spring and start a new stream. "
             "The lab is yours."),
    ],
}

for did, lines in DIALOGUES.items():
    if not g.has("dialogues", did):
        g.insert_after("dialogues", "intro_world_chemistry", {"id": did, "lines": lines})

# The unlock intro is an announcement again; the tutorial teaches streams on arrival.
intro = g.get("dialogues", "intro_world_chemistry")
intro["lines"] = intro["lines"][:1] + [dict(intro["lines"][1], text=(
    "Chemistry is open. Travel there from the Worlds panel whenever you wish; every world keeps "
    "producing while you are away. I will show you how things work when you arrive."))]
g.put("dialogues", intro)


def step(id_, hint, cond, on_enter=None, **extra):
    s = {"id": id_, "hint": hint, "advance_condition": cond, "on_enter": on_enter or {}}
    s.update(extra)
    return s


STEPS = [
    step("chem_welcome", "", {"type": "UiEvent", "ui_event_id": "dialogue_complete"},
         {"dialogue_id": "chem_tut_welcome"}),
    step("chem_research_lab", "Open Research and study the Chemistry Lab.",
         {"type": "ResearchUnlocked", "research_id": "chemistry_lab"},
         {"pulse_button_id": "btn-research"}),
    step("chem_place_spring", "Open the Builder and place a Spring.",
         {"type": "BuildingMin", "building_id": "spring", "min_count": 1},
         {"dialogue_id": "chem_tut_spring", "pulse_button_id": "btn-place-spring"}),
    step("chem_draw_channel",
         "Place a Channel: tap the tile in front of the Spring, then where the stream should end, and confirm.",
         {"type": "UiEvent", "ui_event_id": "conveyor_placed"},
         {"pulse_button_id": "btn-place-conveyor"}),
    step("chem_place_extractors",
         "Place Extractors on a Hydrogen Seep and an Oxygen Vent beside the channel, output toward the water.",
         {"type": "BuildingMin", "building_id": "element_harvester", "min_count": 2},
         {"dialogue_id": "chem_tut_extractors", "pulse_button_id": "btn-place-element_harvester",
          "highlight_target": "hydrogen_gas_field", "highlight_mode": "VisualOnly"}),
    step("chem_place_synth", "Place a Molecular Synthesizer on the bank, downstream of the Extractors.",
         {"type": "BuildingMin", "building_id": "molecular_synthesizer", "min_count": 1},
         {"dialogue_id": "chem_tut_bank", "pulse_button_id": "btn-place-molecular_synthesizer"}),
    step("chem_set_water", "Tap the Synthesizer and set it to Water: 2 H2 + O2 -> 2 H2O.",
         {"type": "BuildingRecipeSet", "building_id": "molecular_synthesizer", "recipe_id": 31},
         {"highlight_target": "molecular_synthesizer", "highlight_mode": "VisualOnly"}),
    step("chem_place_calorimeter", "Place a Calorimeter at the end of the channel.",
         {"type": "BuildingMin", "building_id": "calorimeter", "min_count": 1},
         {"dialogue_id": "chem_tut_calorimeter", "pulse_button_id": "btn-place-calorimeter"}),
    step("chem_first_water", "Watch the stream carry hydrogen and oxygen to the Synthesizer.",
         {"type": "ItemCraftedMin", "items": [{"item_id": 33, "quantity": 2}]}),
    step("chem_flow", "", {"type": "UiEvent", "ui_event_id": "dialogue_complete"},
         {"dialogue_id": "chem_tut_flow"}),
]

if '"world_tutorials"' not in g.text:
    tut = {"id": "tutorial_chemistry", "world": "world_chemistry",
           "completion_message": "Chemistry tutorial complete. The lab is yours.", "steps": STEPS}
    key = '\n  "tutorial_steps": ['
    s = g.text.index(key) + len(key) - 1
    e = _scan_close(g.text, s)
    body = json.dumps([tut], indent=2, ensure_ascii=True)
    body = "\n".join("  " + l if i else l for i, l in enumerate(body.splitlines()))
    insert = ',\n  "_world_tutorials_note": "Per-world tutorials (2026-11-22): run the first time the player is in that ' \
             'world, instead of the Physics tutorial. Same step format as tutorial_steps; progress is saved per world ' \
             '(SaveData.worldTutorials).",\n  "world_tutorials": ' + body
    g.text = g.text[:e + 1] + insert + g.text[e + 1:]

g.save()
print("chemistry tutorial applied")
