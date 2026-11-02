"""Progression curve: how item values and earning rates grow in the order the player unlocks them.

Reads Assets/Data/game_data.json (the single source of truth) and writes docs/progression-curve.html.

    python scripts/progression_curve.py            # write the HTML
    python scripts/progression_curve.py --report   # print the table and the anomalies instead

Model (deliberately simple, so the numbers are easy to reason about):
- Unlock order: starting from the free recipes, repeatedly buy the research whose prerequisites are met, from
  the earliest research block (game phase, `research_blocks`) first, cheapest first within it. Worlds (Chemistry, Biology) are research nodes like any other.
- A recipe counts from the moment it is unlocked AND its inputs can be made; one unlocked before its inputs
  (Water before Oxygen) is listed under the research that makes its last input available.
- Value added by a recipe = (output + byproducts - inputs) sell value per craft, divided by craft time:
  what one building running that recipe adds to the economy per second, with its inputs supplied.
- Best rate at a node = the highest value added per second among every recipe unlocked so far.
- Payback = research cost / best rate before it: how many minutes one building on the best recipe needs
  to pay for the node. A healthy curve rises smoothly; a sudden spike is a wall, a sudden drop is free.
- Estimated time = running total of payback / BUILDINGS (a player running several of their best building).
"""
import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "Assets" / "Data" / "game_data.json"
OUT = ROOT / "docs" / "progression-curve.html"
BUILDINGS = 5          # default "buildings working the best recipe" for the time estimate
JUMP = 4               # a payback this many times above (or below) the recent trend is a wall (or free)


def load():
    return json.loads(DATA.read_text(encoding="utf-8"))


def analyse(gd):
    items = {i["id"]: i for i in gd["items"]}
    value = {k: float(v.get("base_sell_value") or 0) for k, v in items.items()}
    research = {r["id"]: r for r in gd["research"]}
    block_order = {b["id"]: b.get("order", i) for i, b in enumerate(gd.get("research_blocks") or [])}
    recipes = gd["recipes"]

    by_research = {}
    free = []
    for r in recipes:
        gate = r.get("required_research")
        (by_research.setdefault(gate, []) if gate else free).append(r)

    def rate(r):
        t = float(r.get("base_craft_time") or 0) or 1.0
        out = value.get(r["output_item"], 0) * (r.get("output_quantity") or 1)
        out += sum(value.get(b["item"], 0) * b["quantity"] for b in r.get("byproducts") or [])
        inp = sum(value.get(i["item"], 0) * i["quantity"] for i in r["inputs"])
        return out - inp, (out / inp if inp else None), t

    def recipe_row(r):
        added, gain, t = rate(r)
        return {"id": r["id"], "out": r["output_item"], "value": value.get(r["output_item"], 0),
                "added": added, "gain": gain, "time": t, "rate": added / t,
                "building": (r.get("valid_buildings") or ["?"])[0]}

    # A recipe is usable once it is unlocked AND every input is something the player can already make.
    producible, unlocked, used = set(), list(free), set()

    def newly_usable():
        rows, progress = [], True
        while progress:
            progress = False
            for r in unlocked:
                if r["id"] in used or any(i["item"] not in producible for i in r["inputs"]):
                    continue
                used.add(r["id"]); progress = True
                producible.add(r["output_item"])
                producible.update(b["item"] for b in r.get("byproducts") or [])
                rows.append(recipe_row(r))
        return rows

    owned, order = set(), []
    available = newly_usable()
    best = max(available, key=lambda x: x["rate"])
    best_value = max(x["value"] for x in available)
    elapsed = 0.0
    while True:
        ready = [r for k, r in research.items() if k not in owned
                 and all(p in owned or p not in research for p in r.get("prerequisites") or [])]
        if not ready:
            break
        node = min(ready, key=lambda r: (block_order.get(r.get("block"), 0), r.get("cost_base_currency") or 0, r["id"]))
        cost = float(node.get("cost_base_currency") or 0)
        payback = cost / best["rate"] / 60 if best["rate"] > 0 else math.inf
        elapsed += payback / BUILDINGS
        unlocked.extend(by_research.get(node["id"], []))
        rows = newly_usable()
        new_best = max(rows, key=lambda x: x["rate"]) if rows else None
        top_value = max((x["value"] for x in rows), default=0)
        order.append({
            "n": len(order) + 1, "id": node["id"], "name": node.get("display_name", node["id"]),
            "branch": node.get("branch", ""), "block": node.get("block", ""), "cost": cost, "payback_min": payback,
            "elapsed_h": elapsed / 60, "best_rate_before": best["rate"], "best_recipe_before": best["id"],
            "recipes": rows, "top_value": top_value, "best_value_before": best_value,
            "new_best_rate": new_best["rate"] if new_best else 0,
        })
        owned.add(node["id"])
        available += rows
        if new_best and new_best["rate"] > best["rate"]:
            best = new_best
        best_value = max(best_value, top_value)
    return order, free, value, items


def status(order):
    """Per node: 'wall' if its payback jumps past JUMP x the recent trend, 'free' if it falls below 1/JUMP of it.
    Relative, so the flags follow whatever pace the timeline targets."""
    out, trend = {}, None
    for o in order:
        p = o["payback_min"]
        s = "ok"
        if o["cost"] > 0 and trend is not None and o["n"] > 5:
            if p > trend * JUMP: s = "wall"
            elif p < trend / JUMP: s = "free"
        out[o["id"]] = s
        if o["cost"] > 0 and math.isfinite(p) and p > 0:
            trend = p if trend is None else math.exp(0.5 * math.log(trend) + 0.5 * math.log(p))
    return out


def anomalies(order):
    out, st = [], status(order)
    for o in order:
        if st[o["id"]] == "wall":
            out.append(("wall", o["id"], f"payback {o['payback_min']:,.0f} building-min, over {JUMP}x the nodes before it"))
        elif st[o["id"]] == "free":
            out.append(("free", o["id"], f"payback {o['payback_min']:,.2f} building-min, under 1/{JUMP} of the nodes before it"))
        for r in o["recipes"]:
            if r["gain"] is not None and r["gain"] < 1.0:
                out.append(("loses value", r["id"], f"x{r['gain']:.2f} per craft"))
        if o["top_value"] and o["best_value_before"] and o["top_value"] < o["best_value_before"] / 1000:
            out.append(("late but cheap", o["id"],
                        f"best new item {o['top_value']:,.0f} vs {o['best_value_before']:,.0f} already available"))
    return out


def report(order):
    print(f"{'#':>3} {'research':30} {'cost':>14} {'payback':>11} {'hours':>8} {'best e/s before':>16} {'top new value':>16}")
    for o in order:
        print(f"{o['n']:>3} {o['id'][:30]:30} {o['cost']:>14,.0f} {o['payback_min']:>10,.1f}m {o['elapsed_h']:>8,.1f}"
              f" {o['best_rate_before']:>16,.1f} {o['top_value']:>16,.0f}")
    print()
    for kind, what, why in anomalies(order):
        print(f"[{kind}] {what}: {why}")


if __name__ == "__main__":
    gd = load()
    order, free, value, items = analyse(gd)
    if "--report" in sys.argv:
        report(order)
    else:
        from progression_curve_page import write_page   # noqa: E402  (kept separate: page template)
        write_page(OUT, gd, order, anomalies(order), status(order), BUILDINGS, JUMP)
        print(f"wrote {OUT.relative_to(ROOT)}")
