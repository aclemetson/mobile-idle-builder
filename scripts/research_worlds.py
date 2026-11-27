"""One-shot content edit (2026-11-22): give every research its world, for the Research panel's tabs.

A research's world is its branch for Chemistry/Biology; otherwise the single world of what it unlocks
(buildings, recipes, items), else Physics. `required_world` stays the gate; `world` only picks the tab.
Run from scripts/:  python research_worlds.py   (--check: list mismatches, write nothing)
"""
from gamedata_edit import GameData
import json

g = GameData()
d = json.loads(g.text)
bw = {b["id"]: (b.get("world") or "") for b in d["buildings"]}
iw = {i["id"]: (i.get("world") or "") for i in d["items"]}
rw = {r["id"]: {bw.get(b, "") for b in (r.get("valid_buildings") or [])} for r in d["recipes"]}


# A research that opens a world (in a world's prereq_unlock_ids) must not sit in that world's tab: the tab
# only shows once the world is open, so the research could never be seen (biology_lab, fixed 2026-11-26).
# It goes in its first prerequisite's world instead.
gates = {u for w in d.get("worlds", []) for u in (w.get("prereq_unlock_ids") or [])}
gated = {w["id"] for w in d.get("worlds", []) if w.get("prereq_unlock_ids")}
by_id = {r["id"]: r for r in d["research"]}


def home(r):
    if r["id"] in gates:
        pre = [home(by_id[p]) for p in (r.get("prerequisites") or []) if p in by_id]
        return next((w for w in pre if w not in gated), "world_physics")
    if r.get("branch") == "Chemistry": return "world_chemistry"
    # Biology-branch precursors researched in Chemistry (peptides .. energy_currency) keep their gate world's tab.
    if r.get("branch") == "Biology": return r.get("required_world") or "world_biology"
    ws = set()
    for b in r.get("unlocks_buildings") or []: ws.add(bw.get(b, ""))
    for x in r.get("unlocks_recipes") or []: ws |= rw.get(x, set())
    for i in r.get("unlocks_items") or []: ws.add(iw.get(i, ""))
    ws.discard("")
    return next(iter(ws)) if len(ws) == 1 else "world_physics"


import sys
if "--check" in sys.argv:
    bad = [(r["id"], r.get("world"), home(r)) for r in d["research"] if r.get("world") != home(r)]
    for b in bad: print("%s: %s -> %s" % b)
    sys.exit(1 if bad else 0)

for r in d["research"]:
    w = home(r)
    if r.get("world") != w:
        g.set_line("research", r["id"], "world", w) if '"world"' in json.dumps(r) else g.update("research", r["id"], world=w)
g.save()
print("research worlds set")
