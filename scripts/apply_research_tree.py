"""Apply a tree saved from the Research Tree editor (docs/research-tree.html) to Assets/Data/game_data.json.

    python scripts/apply_research_tree.py saved_tree.json            # preview the changes
    python scripts/apply_research_tree.py saved_tree.json --apply    # write them

saved_tree.json is the editor's `trees/working` document (Claude reads it from the artifact's database).
Applies display_name, branch, block, cost_base_currency, duration_seconds, description, prerequisites, the research
blocks (name, order, target_hours: rerun scripts/rebalance_curve.py afterwards to re-derive costs) and the layout
(`tree_x`, `tree_y`, for the in-game tree). New nodes are created with empty unlocks. A deleted node is removed
only if no recipe, building, world or tutorial step still refers to it; otherwise it is reported and kept.
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from gamedata_edit import GameData   # noqa: E402

FIELDS = {"name": "display_name", "branch": "branch", "block": "block", "cost": "cost_base_currency",
          "duration": "duration_seconds", "description": "description", "prereqs": "prerequisites"}


def strip_blocks(blocks):
    keys = ("id", "name", "order", "target_hours", "anchor", "description")
    return [{k: b.get(k) for k in keys} for b in blocks]


def references(gd, rid):
    """Places outside the research list that name this research id."""
    refs = [f"recipe {r['id']}" for r in gd["recipes"] if r.get("required_research") == rid]
    refs += [f"building {b['id']}" for b in gd["buildings"] if b.get("required_research") == rid]
    text = json.dumps({k: v for k, v in gd.items() if k not in ("research", "recipes", "buildings")})
    if f'"{rid}"' in text:
        refs.append("worlds/tutorial/other data")
    return refs


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    saved = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    apply = "--apply" in sys.argv
    g = GameData()
    gd = json.loads(g.text)
    current = {r["id"]: r for r in gd["research"]}
    wanted = {n["id"]: n for n in saved["nodes"]}
    report, kept = [], []

    for rid, n in wanted.items():
        if rid not in current:
            report.append(f"+ add {rid} ({n['name']})")
            continue
        r = current[rid]
        for src, dst in FIELDS.items():
            new, old = n.get(src), r.get(dst)
            if src == "prereqs":
                new, old = list(new or []), list(old or [])
                if sorted(new) == sorted(old): continue
            elif str(new) == str(old if old is not None else ""):
                continue
            report.append(f"~ {rid}.{dst}: {old!r} -> {new!r}")

    for rid in current:
        if rid not in wanted:
            refs = references(gd, rid)
            if refs:
                kept.append(f"! keep {rid}: still used by {', '.join(refs[:5])}")
            else:
                report.append(f"- delete {rid}")

    old_blocks = gd.get("research_blocks") or []
    new_blocks = saved.get("blocks")
    if new_blocks is not None and strip_blocks(old_blocks) != strip_blocks(new_blocks):
        report.append(f"~ research_blocks: {[(b['name'], b['target_hours']) for b in new_blocks]}")
    print("\n".join(report) or "no data changes")
    if kept:
        print("\n".join(kept))
    if not apply:
        print("\npreview only; add --apply to write (layout positions are always written with --apply)")
        return

    for rid, n in wanted.items():
        values = {dst: (list(n.get(src) or []) if src == "prereqs" else n.get(src)) for src, dst in FIELDS.items()}
        values["cost_base_currency"] = int(values["cost_base_currency"] or 0)
        values["duration_seconds"] = int(values["duration_seconds"] or 0)
        values.update(tree_x=int(n.get("x", 0)), tree_y=int(n.get("y", 0)))
        if rid in current:
            g.update("research", rid, **values)
        else:
            last = gd["research"][-1]["id"]
            g.insert_after("research", last, {
                "id": rid, **values, "depth_in_tree": 0, "cost_prestige_currency": 0, "unlocks_items": [],
                "unlocks_recipes": [], "unlocks_buildings": [], "unlocks_grid_expansion": 0, "unlocks_research": [],
                "resets_on_prestige": True, "prestige_memory_discount": 0.0, "codex_entry": "", "icon_path": "TODO"})
    for rid in current:
        if rid not in wanted and not references(gd, rid):
            g.delete("research", rid)
    if new_blocks is not None:
        start = g.text.index('\n  "research_blocks": [')
        end = g.text.index('\n  ],', start) + len('\n  ],')
        body = ",\n".join("    " + json.dumps(b, ensure_ascii=False)
                          for b in sorted(strip_blocks(new_blocks), key=lambda b: b["order"]))
        g.text = g.text[:start] + '\n  "research_blocks": [\n' + body + '\n  ],' + g.text[end:]
    g.save()
    print(f"\napplied to {Path(g.path).name}")


if __name__ == "__main__":
    main()
