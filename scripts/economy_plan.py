"""The Item Timeline's pacing and pricing model, in Python (2026-11-23).

The page (docs/item-timeline.html, scripts/item_timeline_template.html) runs the same model in JavaScript;
this module is what the game data is built from (scripts/apply_item_timeline.py). Keep the two in step:
`python scripts/economy_plan.py` prints a sample the page should match.

    place(plan)  -> each live row gets "d" (calendar day it unlocks)
    price(plan)  -> each live row gets "wage", "value", "research", "mult", "work", "slowest"

Pacing: each phase spaces its rows between start and end day: `burst` quick rows `burstGapH` active hours
apart, then gaps growing x`growth` per row with the last one x`finale`; a wall's repeats stretch the gap
before its row. Pricing: wage(H) = wage0 x 2^((H - anchorH) / doubleH) entropy per building-second at active
hour H; value = (inputs + wage x craft) / output quantity, tutorial items keep their values; research = buildings
x wage x (1 + wallGain)^(walls cleared) x active seconds since the previous unlock in any world (one wallet).
"""
import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PLAN = ROOT / "docs" / "balance" / "item-timeline-plan.json"
LIMIT = 2 ** 53


def nice(v):
    """Round up to 3 significant figures (same as rebalance_curve.nice and the page)."""
    if v is None or not math.isfinite(v) or v <= 0:
        return v
    if v < 100:
        return float(math.ceil(v))
    p = 10 ** (math.floor(math.log10(v)) - 2)
    return float(math.ceil(v / p - 1e-9) * p)


def load(path=PLAN):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def place(plan):
    ahpd = plan.get("activeHoursPerDay") or 2.0
    wall_by = {w["before"]: w for w in plan["walls"]}
    by_phase = {}
    for r in plan["proposal"]:
        by_phase.setdefault(r["phase"], []).append(r)
    for ph in plan["phases"]:
        rows = by_phase.get(ph["id"], [])
        if ph.get("start") is None or ph.get("end") is None:
            for r in rows:
                r["d"] = None
            continue
        burst = min(len(rows), max(0, int(ph.get("burst") or 0)))
        bg = (ph.get("burstGapH") or 0) / ahpd
        t = ph["start"]
        for i, r in enumerate(rows[:burst]):
            r["d"] = ph["start"] + i * bg
            t = r["d"]
        rest = rows[burst:]
        if not rest:
            continue
        w = [(ph.get("growth") or 1) ** i * (1 + float(wall_by.get(r["id"], {}).get("repeats") or 0))
             for i, r in enumerate(rest)]
        w[-1] *= ph.get("finale") or 1
        g = max(0.0, ph["end"] - t) / sum(w)
        for wi, r in zip(w, rest):
            t += g * wi
            r["d"] = t
    for r in plan["proposal"]:
        if r.get("pinDay") not in (None, ""):
            r["d"] = float(r["pinDay"])
    return plan


def wage_at(prices, h):
    return prices["wage0"] * 2 ** ((h - prices["anchorH"]) / prices["doubleH"])


def price(plan):
    P, ahpd = plan["prices"], plan.get("activeHoursPerDay") or 2.0
    by_id = {r["id"]: r for r in plan["proposal"]}
    phase_ix = {ph["id"]: i for i, ph in enumerate(plan["phases"])}
    live = [r for _, _, _, r in sorted((round(r["d"], 6), phase_ix.get(r["phase"], 0), n, r)
                                       for n, r in enumerate(plan["proposal"]) if r.get("d") is not None)]
    wall_days = [by_id[w["before"]]["d"] for w in plan["walls"] if by_id.get(w["before"], {}).get("d") is not None]
    last_h = None          # one wallet: a research gap runs from the previous unlock in any world
    for r in live:
        H = r["d"] * ahpd
        w = max(1.0, wage_at(P, H))
        r["wage"] = w
        craft = r.get("craft")
        if r["phase"] == "p_ignition" and r.get("builtValue") is not None:
            r["value"] = r["builtValue"]
        elif craft in (None, ""):
            if r["id"] == "megastructure_material":
                r["value"] = None
            elif r.get("builtValue") is not None and (r.get("z") == 0 or r.get("how") == "byproduct particle"):
                r["value"] = r["builtValue"]
            else:
                r["value"] = float(math.ceil(w * 60))
        else:
            ins = sum((by_id.get(i["item"], {}).get("value") or 0) * i["q"] for i in r.get("inputs") or [])
            r["value"] = nice((ins + w * float(craft)) / (r.get("outQ") or 1))
        walls = sum(1 for d in wall_days if d < r["d"])
        mult = (1 + P["wallGain"]) ** walls
        r["mult"] = mult
        if r["phase"] == "p_ignition" or r.get("how") == "byproduct particle" or r["id"] == "megastructure_material":
            r["research"] = None
            continue
        gap_s = max(P["minPaybackMin"] * 60, (H - (H if last_h is None else last_h)) * 3600)
        r["research"] = nice(P["buildings"] * w * mult * gap_s)
        last_h = H
    for r in plan["proposal"]:
        if r.get("d") is None:
            r["value"] = r["research"] = r["wage"] = None
    for r in live:
        r["work"], r["slowest"], r["slowestAt"] = pipeline(r, by_id)
    return plan


def pipeline(r, by_id):
    """Total building-seconds per finished unit, and the slowest step with one building per step."""
    demand = {}

    def walk(it, q, depth, stack):
        if it is None or depth > 30 or it["id"] in stack:
            return
        demand[it["id"]] = demand.get(it["id"], 0) + q / (it.get("outQ") or 1)
        stack.add(it["id"])
        for i in it.get("inputs") or []:
            walk(by_id.get(i["item"]), i["q"] * q / (it.get("outQ") or 1), depth + 1, stack)
        stack.discard(it["id"])

    walk(r, 1, 0, set())
    work, worst, at = 0.0, 0.0, None
    for iid, q in demand.items():
        c = float(by_id[iid].get("craft") or 0)
        work += q * c
        if q * c > worst:
            worst, at = q * c, iid
    return work, worst, at


def build(plan):
    return price(place(plan))


if __name__ == "__main__":
    plan = build(load(sys.argv[1] if len(sys.argv) > 1 else PLAN))
    sample = ["oxygen", "silicon", "manganese", "uranium", "lead", "iron", "water", "atp", "ecosystem",
              "steel", "metamaterial", "dyson_node"]
    by_id = {r["id"]: r for r in plan["proposal"]}
    for iid in sample:
        r = by_id.get(iid)
        if r:
            print(f"{iid:20s} day {r['d']:6.2f}  value {r['value']:>16,.0f}  research {r['research'] or 0:>18,.0f}")
    over = [r["id"] for r in plan["proposal"] if (r.get("value") or 0) > LIMIT or (r.get("research") or 0) > LIMIT]
    print("over 2^53:", over or "none")
