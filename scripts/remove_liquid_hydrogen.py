"""One-shot content edit (2026-11-22): retire Liquid Hydrogen.

Liquid Hydrogen (5 H2 -> 1 LH2, item 32, recipe 30) was a filler product in Chemistry's first research. It is
gone: the Chemistry Lab now opens with water only, and the Semiconductor Wafer takes hydrogen gas instead
(5 silica + 4 H2). Run scripts/rebalance_curve.py --apply afterwards to reprice the Chemistry tree.

Run from scripts/:  python remove_liquid_hydrogen.py
"""
from gamedata_edit import GameData

g = GameData()

if g.has("recipes", "liquid_hydrogen"):
    g.delete("recipes", "liquid_hydrogen")
if g.has("items", "liquid_hydrogen"):
    g.delete("items", "liquid_hydrogen")

lab = g.get("research", "chemistry_lab")
lab["unlocks_items"] = [i for i in lab["unlocks_items"] if i != "liquid_hydrogen"]
lab["unlocks_recipes"] = [r for r in lab["unlocks_recipes"] if r != "liquid_hydrogen"]
g.put("research", lab)

syn = g.get("buildings", "molecular_synthesizer")
syn["supported_recipes"] = [r for r in syn["supported_recipes"] if r != "liquid_hydrogen"]
g.put("buildings", syn)

wafer = g.get("recipes", "semiconductor_wafer")
wafer["inputs"] = [{"item": "silica", "quantity": 5}, {"item": "hydrogen_gas", "quantity": 4}]
g.put("recipes", wafer)

g.save()
print("liquid hydrogen retired")
