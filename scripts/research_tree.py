"""Research tree editor: writes docs/research-tree.html from Assets/Data/game_data.json.

    python scripts/research_tree.py

The page draws every research node with its prerequisites inside its research block (game phase), lets you drag
nodes, edit names, costs, times, branches, blocks, descriptions and prerequisites, add or delete nodes, rename,
re-time and reorder blocks, and Save. Saved trees live in the published
artifact's database (doc `trees/working`, history in `versions`); scripts/apply_research_tree.py writes a saved
tree back into game_data.json.
"""
import json
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "Assets" / "Data" / "game_data.json"
OUT = ROOT / "docs" / "research-tree.html"


def build(gd):
    items = {i["id"]: i.get("display_name", i["id"]) for i in gd["items"]}
    buildings = {b["id"]: b.get("display_name", b["id"]) for b in gd["buildings"]}
    nodes = []
    for r in gd["research"]:
        nodes.append({
            "id": r["id"],
            "name": r.get("display_name", r["id"]),
            "branch": r.get("branch", ""),
            "block": r.get("block", ""),
            "cost": r.get("cost_base_currency", 0),
            "duration": r.get("duration_seconds", 0),
            "description": r.get("description", ""),
            "prereqs": [p for p in r.get("prerequisites") or []],
            "unlocks": {
                "items": [items.get(i, i) for i in r.get("unlocks_items") or []],
                "buildings": [buildings.get(b, b) for b in r.get("unlocks_buildings") or []],
                "recipes": len(r.get("unlocks_recipes") or []),
            },
            **({"x": r["tree_x"], "y": r["tree_y"]} if "tree_x" in r and "tree_y" in r else {}),
        })
    branches = sorted({n["branch"] for n in nodes if n["branch"]})
    blocks = [{k: b.get(k) for k in ("id", "name", "order", "target_hours", "anchor", "description")}
              for b in sorted(gd.get("research_blocks") or [], key=lambda b: b.get("order", 0))]
    return {"nodes": nodes, "branches": branches, "blocks": blocks, "version": gd.get("_meta", {}).get("version", ""),
            "generated": datetime.now(timezone.utc).isoformat()}


def main():
    gd = json.loads(DATA.read_text(encoding="utf-8"))
    payload = build(gd)
    html = TEMPLATE.replace("/*BASELINE*/null", json.dumps(payload, separators=(",", ":"), ensure_ascii=False))
    OUT.write_text(html, encoding="utf-8", newline="\n")
    print(f"wrote {OUT.relative_to(ROOT)} ({len(payload['nodes'])} nodes)")


TEMPLATE = (Path(__file__).resolve().parent / "research_tree_template.html").read_text(encoding="utf-8")

if __name__ == "__main__":
    main()
