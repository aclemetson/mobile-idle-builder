"""One-shot content edit (2026-11-22): give every research its world, for the Research panel's tabs.

A research's world is its branch for Chemistry/Biology; otherwise the single world of what it unlocks
(buildings, recipes, items), else Physics. `required_world` stays the gate; `world` only picks the tab.
Run from scripts/:  python research_worlds.py
"""
from gamedata_edit import GameData
import json

g = GameData()
d = json.loads(g.text)
bw = {b["id"]: (b.get("world") or "") for b in d["buildings"]}
iw = {i["id"]: (i.get("world") or "") for i in d["items"]}
rw = {r["id"]: {bw.get(b, "") for b in (r.get("valid_buildings") or [])} for r in d["recipes"]}


def home(r):
    if r.get("branch") == "Chemistry": return "world_chemistry"
    if r.get("branch") == "Biology": return "world_biology"
    ws = set()
    for b in r.get("unlocks_buildings") or []: ws.add(bw.get(b, ""))
    for x in r.get("unlocks_recipes") or []: ws |= rw.get(x, set())
    for i in r.get("unlocks_items") or []: ws.add(iw.get(i, ""))
    ws.discard("")
    return next(iter(ws)) if len(ws) == 1 else "world_physics"


for r in d["research"]:
    w = home(r)
    if r.get("world") != w:
        g.set_line("research", r["id"], "world", w) if '"world"' in json.dumps(r) else g.update("research", r["id"], world=w)
g.save()
print("research worlds set")
