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
3. Research costs come from a timeline: the cumulative hours at which a player running BUILDINGS of their best
   building reaches each node rise from the tutorial end to IRON_HOURS at Fusion III and END_HOURS at the last
   node (log-spaced). cost(n) = minutes it should take x 60 x the best earning rate just before n.
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
WAGE_GROWTH = 1.19     # x per research node; 1.19 keeps the last research cost inside a C# int (2,147,483,647)
INT_MAX = 2_147_483_647
IRON_HOURS = 24.0
END_HOURS = 168.0
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


def derive(gd):
    order, free, value, items = pc.analyse(gd)
    ids = [o["id"] for o in order]
    a, iron = ids.index(ANCHOR), ids.index(IRON)
    recipes_by_node = {}
    for r in gd["recipes"]:
        recipes_by_node.setdefault(r.get("required_research"), []).append(r)

    priced = set(FIXED)
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
        w = order[n]["best_rate_before"] if n <= a else wage_at_anchor * WAGE_GROWTH ** (n - a)
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
                    priced.update(i for i, _ in unpriced); continue
                t = float(r.get("base_craft_time") or 1.0)
                target = MARKUP * sum(new[i["item"]] * i["quantity"] for i in r["inputs"]) + w * t
                known = sum(new[i] * q for i, q in outs_of(r) if i in priced)
                share = max(target - known, w * t) / sum(q for _, q in unpriced)
                for i, _ in unpriced:
                    new[i] = nice(share); priced.add(i)
            pending = left
    for r in pending:
        print(f"  unpriced (inputs never available): {r['id']}", file=sys.stderr)

    # Raw collection (no inputs) must not out-earn the wage of the research that unlocks it (#332: the
    # Chemistry world mined Iron every 2s). Stretch its craft time to value / wage.
    times = {}
    for r in gd["recipes"]:
        gate = r.get("required_research")
        if r["inputs"] or not gate or gate not in ids or ids.index(gate) <= a:
            continue
        w = wage_at_anchor * WAGE_GROWTH ** (ids.index(gate) - a)
        t0 = float(r.get("base_craft_time") or 1.0)
        need = new.get(r["output_item"], 0) * (r.get("output_quantity") or 1) / w
        if need > t0 * 1.05:
            times[r["id"]] = float(round(need)) if need < 100 else nice(need)
    return order, ids, a, iron, new, times


def costs(gd, ids, a, iron):
    """Second pass: research costs from the timeline, using the best earning rate under the new values."""
    order, *_ = pc.analyse(gd)
    assert [o["id"] for o in order] == ids, "unlock order changed; re-run"
    t_a = sum(o["payback_min"] for o in order[: a + 1]) / BUILDINGS / 60

    # Payback ramps geometrically from twice the tutorial's recent payback up to a plateau, reached at Fusion III;
    # the plateau is the payback that spreads the nodes after iron evenly up to END_HOURS. The ramp's length
    # then lands Iron near IRON_HOURS (printed as a check).
    tutorial = sorted(o["payback_min"] for o in order[max(0, a - 4): a + 1] if o["cost"] > 0)
    p0 = 2 * tutorial[len(tutorial) // 2]
    m1, m2 = iron - a, len(ids) - 1 - iron
    plateau = (END_HOURS - IRON_HOURS) * 60 * BUILDINGS / m2
    r1 = (plateau / p0) ** (1 / m1)
    def payback(n):
        return p0 * r1 ** (n - a - 1) if n <= iron else plateau

    out, prev_cost = {}, order[a]["cost"]
    for n in range(a + 1, len(ids)):
        c = nice(payback(n) * 60 * order[n]["best_rate_before"])
        c = max(c, nice(prev_cost * 1.05))            # keep the unlock order: never cheaper than the node before
        out[ids[n]] = int(c)
        prev_cost = c
    return out


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
    order, ids, a, iron, new, times = derive(gd)
    for it in gd["items"]:                      # second pass needs the new values and times in place
        if it["id"] in new: it["base_sell_value"] = new[it["id"]]
    for r in gd["recipes"]:
        if r["id"] in times: r["base_craft_time"] = times[r["id"]]
    research_cost = costs(gd, ids, a, iron)
    too_big = {k: v for k, v in research_cost.items() if v > INT_MAX}
    assert not too_big, f"research cost overflows the int field: {too_big}; lower WAGE_GROWTH or END_HOURS"
    for r in gd["research"]:
        if r["id"] in research_cost: r["cost_base_currency"] = research_cost[r["id"]]
    final, *_ = pc.analyse(gd)

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
def gd_value(k): return _orig.get(k, 0)


if __name__ == "__main__":
    main()
