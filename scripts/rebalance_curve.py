"""Derive item values and research costs from one progression curve (2026-11-02 rebalance).

    python scripts/rebalance_curve.py            # preview: print the proposed curve, change nothing
    python scripts/rebalance_curve.py --apply    # write values and costs into game_data.json

The rule, in unlock order (the order scripts/progression_curve.py reports):
1. The tutorial is the anchor. Every item priced by a recipe unlocked at or before ANCHOR (Stellar Fusion) keeps
   its value, every research up to it keeps its cost, and the particles keep their fixed values.
2. Each later research n has a wage w(n) = WAGE_AT_ANCHOR x WAGE_GROWTH^(n - anchor), in entropy per
   building-second. A recipe it unlocks that makes a not-yet-priced item is priced so that
       outputs = MARKUP x inputs + w(n) x craft time
   so every craft gains at least MARKUP and one building on the newest recipe earns about w(n) a second.
3. Research costs come from the research blocks (game phases, `research_blocks` in game_data.json). Each block
   has target_hours: how long a player running BUILDINGS of their best building should spend clearing it. The
   block's budget is spread over its nodes in unlock order, each node's share BLOCK_RAMP x the one before, and
   cost(n) = that share in minutes x 60 x the best earning rate just before n. Anchor blocks (the tutorial) keep
   their hand-tuned costs. A node never costs less than 1.05 x its most expensive prerequisite.
"""
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import progression_curve as pc          # noqa: E402
from gamedata_edit import GameData       # noqa: E402

ANCHOR = "fusion_i"
IRON = "fusion_iii"
MARKUP = 1.00          # value added comes from the wage alone, so a long chain does not compound
WAGE_GROWTH = 1.19     # x per research node
COST_MAX = 2 ** 53     # research cost is a C# long, stored in JSON as a double: keep it exactly representable
BLOCK_RAMP = 1.20      # within a block, each research takes this many times longer to pay for than the one before
BUILDINGS = pc.BUILDINGS
WORLD_BRANCHES = {"Chemistry", "Biology"}
FIXED = {"alpha_particle", "beta_particle", "neutron", "proton", "electron", "neutrino", "gamma_photon",
         "up_quark", "down_quark"}


def nice(v):
    """Round UP to 3 significant figures so values read like designed numbers and a craft never rounds below its
    inputs (a 1.9M wage on a 9.95B Population step would otherwise vanish)."""
    if v < 100: return float(math.ceil(v))
    p = 10 ** (int(math.log10(v)) - 2)
    return float(math.ceil(v / p - 1e-9) * p)


def off_curve(gd):
    """Research flagged "advances_curve": false (e.g. Transportation II, a belt-speed upgrade) is a side upgrade: it
    does not move the wage curve, so adding one never reprices every later item and research."""
    return {r["id"] for r in gd["research"] if r.get("advances_curve") is False}


def curve_steps(ids, a, skip):
    """Wage-curve position of each node in unlock order: n - anchor, not counting off-curve nodes after the anchor."""
    steps, k = [], 0
    for n, rid in enumerate(ids):
        if n > a and rid not in skip:
            k += 1
        steps.append(n - a if n <= a else k)
    return steps


def derive(gd):
    order, free, value, items = pc.analyse(gd)
    ids = [o["id"] for o in order]
    a, iron = ids.index(ANCHOR), ids.index(IRON)
    step = curve_steps(ids, a, off_curve(gd))
    recipes_by_node = {}
    for r in gd["recipes"]:
        recipes_by_node.setdefault(r.get("required_research"), []).append(r)

    priced = set(FIXED)
    anchored = set()      # items the tutorial prices; nothing later may change them
    new = dict(value)
    wage_at_anchor = order[a]["new_best_rate"] or order[a]["best_rate_before"]
    pending = list(free)
    branch = {r["id"]: r.get("branch", "") for r in gd["research"]}
    is_world = lambda r: branch.get(r.get("required_research"), "") in WORLD_BRANCHES
    # Items the physics chain makes are priced by it; a Chemistry/Biology harvester only reuses that price.
    physics_made = {r["output_item"] for r in gd["recipes"] if not is_world(r)}

    def outs_of(r):
        return [(r["output_item"], r.get("output_quantity") or 1)] + \
               [(b["item"], b["quantity"]) for b in r.get("byproducts") or []]

    # A recipe is priced when its last input becomes available, at the wage of that moment. In the tutorial
    # zone (n <= anchor) a Physics recipe keeps today's value instead; Chemistry/Biology are never anchored.
    for n, node in enumerate(ids):
        pending += recipes_by_node.get(node, [])
        # In the tutorial zone the wage is what the player really earns there, not the curve extrapolated back.
        w = order[n]["best_rate_before"] if n <= a else wage_at_anchor * WAGE_GROWTH ** step[n]
        progress = True
        while progress:
            progress, left = False, []
            for r in pending:
                if any(i["item"] not in priced for i in r["inputs"]) or \
                        (is_world(r) and r["output_item"] in physics_made and r["output_item"] not in priced):
                    left.append(r); continue
                progress = True
                unpriced = [(i, q) for i, q in outs_of(r) if i not in priced]
                if not unpriced:
                    continue
                if n <= a and branch.get(r.get("required_research"), "") not in WORLD_BRANCHES:
                    priced.update(i for i, _ in unpriced); anchored.update(i for i, _ in unpriced); continue
                t = float(r.get("base_craft_time") or 1.0)
                target = MARKUP * sum(new[i["item"]] * i["quantity"] for i in r["inputs"]) + w * t
                known = sum(new[i] * q for i, q in outs_of(r) if i in priced)
                share = max(target - known, w * t) / sum(q for _, q in unpriced)
                for i, _ in unpriced:
                    new[i] = nice(share); priced.add(i)
            pending = left
    for r in pending:
        print(f"  unpriced (inputs never available): {r['id']}", file=sys.stderr)

    # Back-pricing: a recipe whose products were priced by a dearer route (Plutonium fission makes the same
    # fragments as U-235 fission) would earn far more than its wage. When one earns over twice the wage, raise its
    # harvested (raw) inputs so it earns the wage; the collect-time rule below then slows their harvesters to match.
    raw = {r["output_item"] for r in gd["recipes"] if not r["inputs"]} - FIXED - anchored   # harvested, not tutorial-priced
    for r in gd["recipes"]:
        gate = r.get("required_research")
        if not r["inputs"] or gate not in ids or ids.index(gate) <= a:
            continue
        w = wage_at_anchor * WAGE_GROWTH ** step[ids.index(gate)]
        t = float(r.get("base_craft_time") or 1.0)
        out = sum(new.get(i, 0) * q for i, q in outs_of(r))
        inp = sum(new.get(i["item"], 0) * i["quantity"] for i in r["inputs"])
        excess = out - inp - w * t
        mine = [i for i in r["inputs"] if i["item"] in raw]
        if excess > w * t and mine:
            per = excess / sum(i["quantity"] for i in mine)
            for i in mine:
                new[i["item"]] = nice(new[i["item"]] + per)

    # Raw collection (no inputs) must not out-earn the wage of the research that unlocks it (#332: the
    # Chemistry world mined Iron every 2s). Stretch its craft time to value / wage.
    times = {}
    for r in gd["recipes"]:
        gate = r.get("required_research")
        if r["inputs"] or not gate or gate not in ids or ids.index(gate) <= a:
            continue
        w = wage_at_anchor * WAGE_GROWTH ** step[ids.index(gate)]
        t0 = float(_ORIG_TIME.get(r["id"]) or r.get("base_craft_time") or 1.0)   # against the file, not a prior pass
        need = new.get(r["output_item"], 0) * (r.get("output_quantity") or 1) / w
        if need > t0 * 1.05:
            times[r["id"]] = float(round(need)) if need < 100 else nice(need)
    return order, ids, a, iron, new, times


def costs(gd):
    """Research costs from the block budgets, using the best earning rate under the current values."""
    order, *_ = pc.analyse(gd)
    blocks = {b["id"]: b for b in gd.get("research_blocks") or []}
    research = {r["id"]: r for r in gd["research"]}
    skip = off_curve(gd)
    by_block = {}
    for n, o in enumerate(order):
        by_block.setdefault(o["block"], []).append(n)
    out = {}
    for bid, members in by_block.items():
        b = blocks.get(bid)
        if b is None or b.get("anchor"):
            continue
        budget = float(b["target_hours"]) * 60 * BUILDINGS          # building-minutes
        weights = [BLOCK_RAMP ** k for k in range(len(members))]
        # Off-curve nodes take the share of their position but are not part of the block's budget, so adding one
        # leaves every other node's cost where it was (the block runs a little over its target instead).
        on = sum(w for n, w in zip(members, weights) if order[n]["id"] not in skip)
        for n, w in zip(members, weights):
            out[order[n]["id"]] = budget * w / on * 60 * order[n]["best_rate_before"]
    # A node never costs less than its prerequisites (walk in unlock order so prerequisites are final first).
    final = {}
    for o in order:
        rid = o["id"]
        c = out.get(rid, research[rid].get("cost_base_currency") or 0)
        floor = max((final.get(p, research[p].get("cost_base_currency") or 0) for p in research[rid].get("prerequisites") or []
                     if p in research), default=0) * 1.05
        final[rid] = int(nice(max(c, floor))) if rid in out else c
    return {k: v for k, v in final.items() if k in out}


def apply_values(g, gd, new):
    changed = 0
    for it in gd["items"]:
        v = new.get(it["id"])
        if v is not None and abs(v - float(it.get("base_sell_value") or 0)) >= 0.5:
            g.update("items", it["id"], base_sell_value=float(v)); changed += 1
    return changed


def main():
    apply = "--apply" in sys.argv
    gd = pc.load()
    # Values depend on unlock order and costs depend on values, and new costs can reorder unlocks; iterate to a
    # fixed point (it settles in two or three passes).
    pristine = pc.load()
    prev = None
    for _ in range(8):
        order, ids, a, iron, new, times = derive(gd)
        gd = pc.load()
        for it in gd["items"]:
            if it["id"] in new: it["base_sell_value"] = new[it["id"]]
        for r in gd["recipes"]:
            if r["id"] in times: r["base_craft_time"] = times[r["id"]]
        for r in gd["research"]:
            if prev and r["id"] in prev: r["cost_base_currency"] = prev[r["id"]]
        research_cost = costs(gd)
        for r in gd["research"]:
            if r["id"] in research_cost: r["cost_base_currency"] = research_cost[r["id"]]
        if research_cost == prev:
            break
        prev = research_cost
    too_big = {k: v for k, v in research_cost.items() if v > COST_MAX}
    assert not too_big, f"research cost too large to store exactly: {too_big}; lower WAGE_GROWTH or a block's target_hours"
    final, *_ = pc.analyse(gd)
    blocks = gd.get("research_blocks") or []
    if blocks:
        print(f"{'block':22} {'target h':>9} {'model h':>9}")
        for b in blocks:
            h = sum(o["payback_min"] for o in final if o["block"] == b["id"]) / BUILDINGS / 60
            print(f"{b['name']:22} {b['target_hours']:>9} {h:>9.1f}")
        print()

    print(f"{'#':>3} {'research':28} {'cost':>16} {'payback':>10} {'hours':>8} {'best e/s':>12} {'top new item':>14}")
    for o in final:
        print(f"{o['n']:>3} {o['id'][:28]:28} {o['cost']:>16,.0f} {o['payback_min']:>9,.0f}m {o['elapsed_h']:>8,.1f}"
              f" {o['best_rate_before']:>12,.1f} {o['top_value']:>14,.0f}")
    print()
    for rid, s in sorted(times.items()):
        print(f"  collect time {rid:24} -> {s:,.0f}s")
    for k in ["carbon", "oxygen", "silicon", "iron", "uranium", "thorium", "lead", "krypton", "gold", "water",
              "steel", "quantum_processor", "dyson_node", "oganesson", "protein", "ecosystem"]:
        if k in new: print(f"  {k:18} {gd_value(k):>22,.0f}  ->  {new[k]:>22,.0f}")
    if apply:
        g = GameData()
        n_items = apply_values(g, pc.load(), new)
        for rid, c in research_cost.items():
            g.update("research", rid, cost_base_currency=c)
        for rid, s in times.items():
            g.update("recipes", rid, base_craft_time=s, manual_craft_time=s)
        g.save()
        print(f"\napplied: {n_items} item values, {len(research_cost)} research costs, {len(times)} collect times")


_orig = {i["id"]: i["base_sell_value"] for i in pc.load()["items"]}
_ORIG_TIME = {r["id"]: r.get("base_craft_time") for r in pc.load()["recipes"]}
def gd_value(k): return _orig.get(k, 0)


if __name__ == "__main__":
    main()
