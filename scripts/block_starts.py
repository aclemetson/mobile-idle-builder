"""Fill each research block's "first arrival" state in game_data.json (used by the dev console's `block load`).

    python scripts/block_starts.py            # preview
    python scripts/block_starts.py --apply    # write

Authored per block (edit these in game_data.json `research_blocks`):
  start_prestige_count            prestiges a typical player has done when they first reach the block
  start_prestige_currency_earned  total prestige currency (✦) they have earned by then

Derived here:
  start_permanent_upgrades  the levels that ✦ buys, spent in PRIORITY order one level per upgrade per pass
                            (costs, max levels and prerequisites read from PersistentUpgradeService.cs)
  start_entropy             the cost of the block's first research in unlock order, so it can be bought at once
  start_sites               sites earlier blocks open (the Actinide Vein once Fissile Extraction is behind you)

Re-run after scripts/rebalance_curve.py, which changes research costs.
"""
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import progression_curve as pc          # noqa: E402
from gamedata_edit import GameData       # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
UPGRADES_CS = ROOT / "Assets" / "Scripts" / "Services" / "PersistentUpgradeService.cs"

# What a player buys first: production, then research speed, then idle, then convenience.
PRIORITY = ["assembly_line", "memory_resonance", "entropy_headstart", "quantum_yield", "idle_time_cap",
            "research_overdrive", "idle_collection_rate", "efficient_layouts", "surplus_resale", "expanded_vault",
            "field_cooldown", "salvage_protocols", "turnkey_builder", "entropy_echo"]
# Research that opens a site for good: once it is behind the player, the block starts with that site unlocked.
SITE_GATES = {"fissile_extraction": "site_actinide_vein"}


def catalogue():
    text = UPGRADES_CS.read_text(encoding="utf-8")
    out = {}
    for m in re.finditer(r'new UpgradeDef\(\s*"(\w+)",.*?new\[\]\{([^}]*)\}(?:,\s*new\[\]\{([^}]*)\})?\)', text, re.S):
        uid, costs, prereqs = m.group(1), m.group(2), m.group(3) or ""
        out[uid] = {"costs": [int(c) for c in re.findall(r"\d+", costs)],
                    "prereqs": [(a, int(b)) for a, b in re.findall(r'\("(\w+)",\s*(\d+)\)', prereqs)]}
    assert out, "no upgrades parsed from PersistentUpgradeService.cs"
    return out


def buy(budget, cat):
    levels, spent = {}, 0
    while True:
        bought = False
        for uid in PRIORITY:
            u = cat.get(uid)
            if not u:
                continue
            lvl = levels.get(uid, 0)
            if lvl >= len(u["costs"]) or any(levels.get(p, 0) < n for p, n in u["prereqs"]):
                continue
            cost = u["costs"][lvl]
            if spent + cost <= budget:
                levels[uid] = lvl + 1
                spent += cost
                bought = True
        if not bought:
            return levels, spent


def main():
    apply = "--apply" in sys.argv
    gd = pc.load()
    cat = catalogue()
    order, *_ = pc.analyse(gd)
    first_cost = {}
    seen = []
    for o in order:
        first_cost.setdefault(o["block"], o["cost"])
    g = GameData() if apply else None
    for b in sorted(gd["research_blocks"], key=lambda b: b["order"]):
        budget = int(b.get("start_prestige_currency_earned", 0))
        levels, spent = buy(budget, cat)
        ups = [f"{k}:{v}" for k, v in sorted(levels.items(), key=lambda kv: PRIORITY.index(kv[0]))]
        before = {o["id"] for o in order if o["block"] in seen}
        sites = sorted({s for gate, s in SITE_GATES.items() if gate in before})
        entropy = int(first_cost.get(b["id"], 0)) if not b.get("anchor") else 0
        print(f"{b['name']:20} prestiges {b.get('start_prestige_count', 0):>3}  ✦ {budget:>6} (spent {spent:>6})  "
              f"entropy {entropy:>14,}  sites {sites}\n    {', '.join(ups) or 'no upgrades'}")
        if apply:
            update_block(g, b["id"], start_permanent_upgrades=ups, start_entropy=entropy, start_sites=sites)
        seen.append(b["id"])
    if apply:
        g.save()
        print("\napplied")


def update_block(g, bid, **fields):
    """research_blocks entries are one object per line; rewrite the one with this id."""
    lines = g.text.split("\n")
    for i, line in enumerate(lines):
        s = line.strip()
        if s.startswith("{") and f'"id": "{bid}"' in s and '"target_hours"' in s:
            trail = "," if s.endswith(",") else ""
            obj = json.loads(s.rstrip(","))
            obj.update(fields)
            lines[i] = line[: len(line) - len(line.lstrip())] + json.dumps(obj, ensure_ascii=False) + trail
            g.text = "\n".join(lines)
            return
    raise KeyError(bid)


if __name__ == "__main__":
    main()
