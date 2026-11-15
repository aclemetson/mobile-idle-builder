"""Build docs/balance/prestige-pacing.xlsx: a Physics-world prestige pacing plan.

Reads the research curve the game is balanced on (scripts/progression_curve.py: unlock order,
cost, best earning rate before each node) plus timers and prestige-memory discounts from
Assets/Data/game_data.json, then writes a workbook whose plan is driven by live formulas:
edit the yellow input cells in Excel and every run recalculates.

    python scripts/prestige_plan.py            # writes docs/balance/prestige-pacing.xlsx and .html

The .html is the same model as an interactive page (scripts/prestige_plan_template.html) that opens from
disk or publishes as a Claude artifact; edits there stay in the viewer's browser.

Re-run after a rebalance (research costs or rates changed). It overwrites the workbook, so copy
any plan edits you want to keep into PLAN below first.
"""
import datetime
import json
import os
import subprocess
import sys

from openpyxl import Workbook
from openpyxl.comments import Comment
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.workbook.defined_name import DefinedName
from openpyxl.worksheet.datavalidation import DataValidation

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "docs", "balance", "prestige-pacing.xlsx")
OUT_HTML = os.path.join(ROOT, "docs", "balance", "prestige-pacing.html")
TEMPLATE = os.path.join(ROOT, "scripts", "prestige_plan_template.html")
SHORT = {"memory_resonance": "Mem Res", "research_overdrive": "Overdrive", "assembly_line": "Assembly",
         "quantum_yield": "Quantum Y", "entropy_echo": "Echo", "entropy_headstart": "Headstart",
         "idle_time_cap": "Idle cap", "idle_collection_rate": "Idle rate"}
PHYSICS_BLOCKS = ("ignition", "foundations", "stellar_forge", "actinide_frontier")

# ── Default plan (one row per run). Levels are TOTAL owned at the start of the run. ─────────
# target, MR, RO, AL, QY, Echo, Headstart, IdleCap, IdleRate, manager income mult
PLAN = [
    ("nucleon_harvesting",           0, 0, 0, 0, 0, 0, 0, 0, 1.0),
    ("light_elements",               1, 0, 1, 0, 0, 1, 0, 0, 1.2),
    ("fusion_ii",                    2, 0, 2, 1, 0, 1, 1, 0, 1.2),
    ("fusion_iii",                   3, 0, 3, 1, 0, 1, 1, 0, 1.4),
    ("radioactive_isotopes",         3, 0, 3, 2, 0, 1, 2, 1, 1.6),
    ("slow_neutron_capture",         4, 0, 3, 3, 0, 1, 2, 1, 1.6),
    ("exotic_metals",                4, 0, 4, 3, 0, 1, 3, 1, 1.8),
    ("lanthanides_and_heavy_metals", 5, 0, 4, 3, 0, 1, 3, 1, 2.0),
]
# Upgrades the plan tracks (column order in Plan), with per-level ✦ costs from
# PersistentUpgradeService.All.
UPGRADES = [
    ("memory_resonance",     "Memory Resonance (-5% research cost)",   [10, 20, 40, 80, 160]),
    ("research_overdrive",   "Research Overdrive (-5% timer)",          [30, 50, 85, 140, 230, 380, 625, 1030, 1700, 2800]),
    ("assembly_line",        "Assembly Line (+10% craft speed)",        [15, 30, 60, 120, 240]),
    ("quantum_yield",        "Quantum Yield (+10% output)",             [40, 80, 160, 320, 640]),
    ("entropy_echo",         "Entropy Echo (+5% prestige currency)",    [200, 600, 1800, 5400]),
    ("entropy_headstart",    "Entropy Headstart (+250e start)",         [5, 10, 20, 40, 80]),
    ("idle_time_cap",        "Dormant Resonance (+30 min idle cap)",    [35, 40, 46, 53, 61, 70, 81, 93, 107, 123,
                                                                         142, 163, 188, 216, 249, 286, 329, 379, 436, 502]),
    ("idle_collection_rate", "Idle Efficiency (+5% idle rate)",         [80, 120, 180, 270, 405, 608, 912, 1368, 2052, 3078]),
]

# ── Styles ───────────────────────────────────────────────────────────────────────────────
F = "Arial"
BLUE = Font(name=F, color="0000FF")
GREEN = Font(name=F, color="008000")
BLACK = Font(name=F)
BOLD = Font(name=F, bold=True)
TITLE = Font(name=F, bold=True, size=14)
HDR = Font(name=F, bold=True, color="FFFFFF")
HDR_FILL = PatternFill("solid", fgColor="1F3864")
YELLOW = PatternFill("solid", fgColor="FFFF00")
GREY = PatternFill("solid", fgColor="F2F2F2")
WRAP = Alignment(wrap_text=True, vertical="top")
CENTER = Alignment(horizontal="center", vertical="center", wrap_text=True)
THIN = Border(bottom=Side(style="thin", color="BFBFBF"))


def load_curve():
    """Physics nodes in unlock order: (id, block, cost, best rate before, timer s, memory discount)."""
    out = subprocess.run([sys.executable, os.path.join(ROOT, "scripts", "progression_curve.py"), "--report"],
                         capture_output=True, text=True, cwd=ROOT, check=True).stdout
    data = json.load(open(os.path.join(ROOT, "Assets", "Data", "game_data.json"), encoding="utf-8"))
    research = {r["id"]: r for r in data["research"]}
    rows = []
    for line in out.splitlines():
        parts = line.split()
        if len(parts) < 6 or not parts[0].isdigit():
            continue
        rid = parts[1]
        r = research[rid]
        if r["block"] not in PHYSICS_BLOCKS:
            continue
        rows.append((rid, r["block"], float(parts[2].replace(",", "")), float(parts[5].replace(",", "")),
                     r["duration_seconds"], r["prestige_memory_discount"]))
    return rows, data["game_config"]


def write_html(curve, inputs):
    """Interactive page with the same model as the Plan sheet (see compute() in the template)."""
    data = json.load(open(os.path.join(ROOT, "Assets", "Data", "game_data.json"), encoding="utf-8"))
    names = {r["id"]: r.get("display_name") or r["id"] for r in data["research"]}
    research = []
    for k, (rid, block, cost, rate, timer, mem) in enumerate(curve):
        research.append({"n": k + 1, "id": rid, "name": names.get(rid, rid), "block": block, "cost": cost,
                         "rate": rate, "timer": timer, "prevTimer": curve[k - 1][4] if k else 0, "mem": mem})
    page = {
        "generated": datetime.date.today().isoformat(),
        "research": research,
        "upgrades": [{"id": uid, "label": label, "short": SHORT[uid], "costs": costs} for uid, label, costs in UPGRADES],
        "plan": [{"target": t, "levels": list(lv[:-1]), "mgr": lv[-1]} for t, *lv in PLAN],
        "inputs": [{"label": it[0]} if it[1] is None else
                   {"key": it[0], "label": it[1], "value": it[2], "unit": it[3], "note": it[4]} for it in inputs],
    }
    html = open(TEMPLATE, encoding="utf-8").read().replace("/*DATA*/null", json.dumps(page, ensure_ascii=False))
    with open(OUT_HTML, "w", encoding="utf-8", newline="\n") as f:
        f.write(html)
    print(OUT_HTML)


def header(ws, row, labels, widths=None):
    for c, label in enumerate(labels, 1):
        cell = ws.cell(row=row, column=c, value=label)
        cell.font, cell.fill, cell.alignment = HDR, HDR_FILL, CENTER
    if widths:
        for c, w in enumerate(widths, 1):
            ws.column_dimensions[ws.cell(row=1, column=c).column_letter].width = w


def name(wb, nm, ref):
    wb.defined_names[nm] = DefinedName(nm, attr_text=ref)


def build():
    curve, cfg = load_curve()
    n = len(curve)
    wb = Workbook()

    # ── Inputs ───────────────────────────────────────────────────────────────────────────
    inp = wb.active
    inp.title = "Inputs"
    inp["A1"], inp["A1"].font = "Inputs and assumptions", TITLE
    inp["A2"] = ("Yellow cells are the levers. Blue text = typed value, black = formula. "
                 "Every other sheet reads these by name.")
    inp["A2"].font = Font(name=F, italic=True)
    header(inp, 4, ["Input", "Value", "Unit", "Source / note"], [44, 14, 14, 90])
    inputs = [
        ("Scenario", None, None, None),
        ("Wired", "Research discounts applied (1 = live game, 0 = pre-fix build)", 1, "flag",
         "Live: ResearchService.EffectiveCost applies Memory Resonance and each node's "
         "prestige_memory_discount (ResearchCost.Effective). 0 models the old build that charged raw cost."),
        ("LiveBonuses", "Assembly Line / Quantum Yield affect live production (1 = live game)", 1, "flag",
         "Live: PrestigeData.Speed/OutputMultiplier feed ProductionSystem and CollectorSystem. 0 models "
         "the old build where they only changed idle earnings."),
        ("Production model", None, None, None),
        ("Buildings", "Buildings earning on the newest recipe", 5, "count",
         "research_blocks target_hours assume 5 buildings (game_data.json _research_blocks_note)."),
        ("Overhead", "Extra entropy beyond research (buildings, upgrades, unspent)", 0.2, "fraction",
         "Assumption. Scales both time and net worth. progression_curve.py counts research only (0)."),
        ("Prestige formula", None, None, None),
        ("PBase", "prestige_base_value", cfg["prestige_base_value"], "e", "game_data.json game_config"),
        ("PScale", "prestige_currency_scale", cfg["prestige_currency_scale"], "✦ per x10",
         "game_data.json game_config. Try 75 to see a richer prestige economy."),
        ("PWall", "prestige_wall_multiplier", cfg["prestige_wall_multiplier"], "x base",
         "Prestige unlocks at net worth >= base x wall (50,000e)."),
        ("StartE", "starting_entropy", cfg["starting_entropy"], "e", "game_data.json game_config"),
        ("Player pattern", None, None, None),
        ("ActiveMin", "Active play per day", 90, "min", "Assumption: an engaged mobile idle player."),
        ("Sessions", "Sessions per day", 5, "count",
         "Assumption. Offline time is split evenly between sessions."),
        ("GapTarget", "Longest acceptable active gap between unlocks", 60, "min",
         "Design target for 'engaged': no research should take more than this much active play."),
        ("Ads: Entropy Surge (shipped)", None, None, None),
        ("AdReward", "Reward per ad, in minutes of current automation income", 10, "min",
         "AdRewardCalculator.Placements (EntropySurge): IncomeMinutes 10, floor 500e."),
        ("AdCooldown", "Cooldown between ads", 15, "min", "AdRewardCalculator.Placements (EntropySurge)."),
        ("AdCap", "Daily cap", 8, "ads/day", "AdRewardCalculator.Placements (EntropySurge), 00:00 UTC reset."),
        ("AdWatch", "Share of available ads a player watches", 0.5, "fraction", "Assumption."),
        ("Ads: current Entropy Boost (for comparison)", None, None, None),
        ("CurBoostFrac", "Entropy Boost reward, fraction of net worth", 0.1, "fraction",
         "AdRewardCalculator.Placements (EntropyBoost)."),
        ("CurBoostCap", "Entropy Boost daily cap", 5, "ads/day", "AdRewardCalculator.Placements."),
    ]
    r = 5
    for item in inputs:
        if item[1] is None:
            inp.cell(row=r, column=1, value=item[0]).font = BOLD
            inp.cell(row=r, column=1).fill = GREY
            for c in range(2, 5):
                inp.cell(row=r, column=c).fill = GREY
        else:
            nm, label, val, unit, note = item
            inp.cell(row=r, column=1, value=label).font = BLACK
            v = inp.cell(row=r, column=2, value=val)
            v.font, v.fill = BLUE, YELLOW
            v.number_format = "0%" if unit == "fraction" else "#,##0.##"
            inp.cell(row=r, column=3, value=unit).font = BLACK
            inp.cell(row=r, column=4, value=note).font = BLACK
            inp.cell(row=r, column=4).alignment = WRAP
            name(wb, nm, f"Inputs!$B${r}")
        r += 1
    r += 1
    inp.cell(row=r, column=1, value="Derived").font = BOLD
    r += 1
    derived = [
        ("GapH", "Offline gap between sessions", "=(24-ActiveMin/60)/Sessions", "h"),
        ("AdsPerDay", "Entropy Surge ads watched per day",
         "=MIN(AdCap,(Sessions+INT(ActiveMin/AdCooldown))*AdWatch)", "ads/day"),
        ("AdHoursPerDay", "Entropy Surge income per day", "=AdsPerDay*AdReward/60", "h of income"),
    ]
    for nm, label, f, unit in derived:
        inp.cell(row=r, column=1, value=label).font = BLACK
        c = inp.cell(row=r, column=2, value=f)
        c.font, c.number_format = BLACK, "0.00"
        inp.cell(row=r, column=3, value=unit).font = BLACK
        name(wb, nm, f"Inputs!$B${r}")
        r += 1
    inp.cell(row=r, column=4,
             value="An ad is ready at every session start (the cooldown ran out while away), plus one per "
                   "cooldown during active play.").alignment = WRAP
    inp.freeze_panes = "A5"

    # ── Research ─────────────────────────────────────────────────────────────────────────
    rs = wb.create_sheet("Research")
    rs["A1"], rs["A1"].font = "Physics research in unlock order", TITLE
    rs["A2"] = ("Source: scripts/progression_curve.py (unlock order, cost, best rate) and game_data.json "
                "(timer, memory discount). Regenerate with python scripts/prestige_plan.py after a rebalance.")
    rs["A2"].font = Font(name=F, italic=True)
    header(rs, 4, ["#", "Research", "Block", "Cost (e)", "Best rate before (e/s per building)",
                   "Timer (s)", "Previous node's timer (s)", "Memory discount on repeat runs",
                   "Building-seconds to earn (cost / rate)", "Discounted share (col I x col H)",
                   "Cost x discount (e)"], [5, 30, 18, 15, 18, 10, 14, 14, 18, 18, 16])
    first, last = 5, 4 + n
    for k, (rid, block, cost, rate, timer, mem) in enumerate(curve):
        row = first + k
        vals = [k + 1, rid, block, cost, rate, timer, None, mem]
        for c, v in enumerate(vals, 1):
            cell = rs.cell(row=row, column=c, value=v)
            cell.font = BLUE if c in (4, 5, 6, 8) else BLACK
        rs.cell(row=row, column=7, value=f"=F{row - 1}" if k else 0).font = BLACK
        rs.cell(row=row, column=9, value=f"=D{row}/E{row}").font = BLACK
        rs.cell(row=row, column=10, value=f"=I{row}*H{row}").font = BLACK
        rs.cell(row=row, column=11, value=f"=D{row}*H{row}").font = BLACK
        for c, fmt in ((4, "#,##0"), (5, "#,##0.0"), (6, "#,##0"), (7, "#,##0"), (8, "0%"),
                       (9, "#,##0"), (10, "#,##0"), (11, "#,##0")):
            rs.cell(row=row, column=c).number_format = fmt
    for nm, col in (("RIdx", "A"), ("RId", "B"), ("RCost", "D"), ("RTimer", "F"), ("RPrevTimer", "G"),
                    ("RBsec", "I"), ("RBsecMem", "J"), ("RCostMem", "K")):
        name(wb, nm, f"Research!${col}${first}:${col}${last}")
    rs.freeze_panes = "C5"

    # ── Shop ─────────────────────────────────────────────────────────────────────────────
    sh = wb.create_sheet("Shop")
    sh["A1"], sh["A1"].font = "Prestige shop costs (✦)", TITLE
    sh["A2"] = "Source: PersistentUpgradeService.All. Top table = cost of each level; bottom = total to own that level."
    sh["A2"].font = Font(name=F, italic=True)
    maxlv = 20
    header(sh, 4, ["Upgrade", "id"] + [f"Lv {i}" for i in range(1, maxlv + 1)],
           [40, 22] + [8] * maxlv)
    for u, (uid, label, costs) in enumerate(UPGRADES):
        row = 5 + u
        sh.cell(row=row, column=1, value=label).font = BLACK
        sh.cell(row=row, column=2, value=uid).font = BLACK
        for i, c in enumerate(costs):
            sh.cell(row=row, column=3 + i, value=c).font = BLUE
    cum0 = 5 + len(UPGRADES) + 2
    sh.cell(row=cum0 - 1, column=1, value="Total ✦ to own level (Lv 0 = 0)").font = BOLD
    header(sh, cum0, ["Upgrade", "Lv 0"] + [f"Lv {i}" for i in range(1, maxlv + 1)])
    cum_rows = {}
    for u, (uid, label, costs) in enumerate(UPGRADES):
        row = cum0 + 1 + u
        src = 5 + u
        cum_rows[uid] = row
        sh.cell(row=row, column=1, value=label).font = BLACK
        sh.cell(row=row, column=2, value=0).font = BLACK
        for i in range(maxlv):
            col = 3 + i
            prev = sh.cell(row=row, column=col - 1).coordinate
            costc = sh.cell(row=src, column=col).coordinate
            # Levels past the upgrade's max show as blank in the cost table; carry the total over.
            sh.cell(row=row, column=col, value=f"={prev}+N({costc})").font = BLACK
    sh.freeze_panes = "C5"

    # ── Plan ─────────────────────────────────────────────────────────────────────────────
    pl = wb.create_sheet("Plan")
    pl["A1"], pl["A1"].font = "Physics prestige plan, one row per run", TITLE
    pl["A2"] = ("Yellow = choose per run: where the run prestiges and the upgrade levels owned at its start "
                "(bought with ✦ from earlier runs). Everything else is calculated. Hours are active-play hours "
                "with every building running; days add offline earnings between sessions.")
    pl["A2"].font = Font(name=F, italic=True)
    pl.merge_cells("A2:Q2")
    pl.row_dimensions[2].height = 30
    pl["A2"].alignment = WRAP
    cols = [
        ("Run", 5), ("Prestige after research", 28), ("Nodes unlocked", 9),
        ("Memory Res. lv", 8), ("Overdrive lv", 8), ("Assembly lv", 8), ("Quantum Y. lv", 8),
        ("Echo lv", 7), ("Headstart lv", 8), ("Idle cap lv", 7), ("Idle rate lv", 7),
        ("Manager income mult", 9),
        ("✦ spent at run start", 9), ("Production mult (live)", 9), ("Research cost factor", 9),
        ("Active hours", 9), ("Active hours, pre-fix build", 9),
        ("Longest gap between unlocks (active min)", 11), ("Avg active min per unlock", 9),
        ("Gap check", 8), ("Timer wait while active (h)", 9), ("Timer floor (h)", 8),
        ("Net worth at prestige (e)", 15), ("✦ earned", 8), ("✦ bank after run", 9),
        ("Affordable?", 9), ("Idle rate", 7), ("Idle cap (h)", 7),
        ("Daily income, no ads (active-h equiv.)", 11), ("Days, no ads", 8),
        ("Days with Entropy Surge ads", 9), ("Run time saved by ads", 9),
        ("Days, pre-fix build (no ads)", 9), ("Cumulative days (with ads)", 10),
        ("Current Entropy Boost: one ad mid-run (h of income)", 13),
    ]
    header(pl, 4, [c[0] for c in cols], [c[1] for c in cols])
    pl.row_dimensions[4].height = 75
    dv = DataValidation(type="list", formula1=f"=Research!$B${first}:$B${last}", allow_blank=False)
    pl.add_data_validation(dv)

    up_cols = ["D", "E", "F", "G", "H", "I", "J", "K"]  # same order as UPGRADES
    p0 = 5
    for i, (target, *levels) in enumerate(PLAN):
        row = p0 + i
        mgr = levels[-1]
        levels = levels[:-1]
        pl.cell(row=row, column=1, value=i + 1).font = BLACK
        c = pl.cell(row=row, column=2, value=target)
        c.font, c.fill = BLUE, YELLOW
        dv.add(c)
        for j, lv in enumerate(levels):
            cc = pl.cell(row=row, column=4 + j, value=lv)
            cc.font, cc.fill = BLUE, YELLOW
        cc = pl.cell(row=row, column=12, value=mgr)
        cc.font, cc.fill, cc.number_format = BLUE, YELLOW, "0.0x"

        R = row
        k = f"$C{R}"
        f = {}
        f["C"] = f"=INDEX(RIdx,MATCH(B{R},RId,0))"
        spend = []
        for uc, (uid, _, _) in zip(up_cols, UPGRADES):
            cr = cum_rows[uid]
            rng = f"Shop!$B${cr}:$W${cr}"
            prev = f"{uc}{R - 1}" if i else "0"
            spend.append(f"INDEX({rng},{uc}{R}+1)-INDEX({rng},{prev}+1)")
        f["M"] = "=" + "+".join(spend)
        f["N"] = f"=L{R}*IF(LiveBonuses=1,(1+0.1*F{R})*(1+0.1*G{R}),1)"
        f["O"] = f"=1-IF(Wired=1,0.05*D{R},0)"
        # Prestige memory discount only applies to research finished on an EARLIER run: nodes up to the
        # furthest target any previous run reached (none on run 1). Mirrors SaveData.researchCompletedEver.
        prev_max = f"MAX($C$4:C{R - 1})"
        mem = f"Wired*(RIdx<={prev_max})"
        earn_arr = f"(RBsec-{mem}*RBsecMem)*O{R}*(1+Overhead)/(Buildings*N{R})"
        f["P"] = f"=SUMPRODUCT((RIdx<={k})*(RBsec-{mem}*RBsecMem))*O{R}*(1+Overhead)/(Buildings*N{R})/3600"
        f["Q"] = f"=SUMPRODUCT((RIdx<={k})*RBsec)*(1+Overhead)/(Buildings*L{R})/3600"
        f["R"] = f"=SUMPRODUCT(MAX((RIdx<={k})*{earn_arr}))/60"
        f["S"] = f"=P{R}*60/C{R}"
        f["T"] = f'=IF(R{R}<=GapTarget,"OK","LONG")'
        wait = f"(RPrevTimer*(1-0.05*E{R})-{earn_arr})"
        f["U"] = f"=SUMPRODUCT((RIdx<={k})*({wait}>0)*{wait})/3600"
        f["V"] = f"=SUMPRODUCT((RIdx<={k})*RTimer)*(1-0.05*E{R})/3600"
        f["W"] = (f"=SUMPRODUCT((RIdx<={k})*(RCost-{mem}*RCostMem))*O{R}*(1+Overhead)"
                  f"+StartE+250*I{R}")
        f["X"] = f"=IF(W{R}>=PBase*PWall,INT(INT(LOG10(W{R}/PBase)*PScale)*(1+0.05*H{R})),0)"
        f["Y"] = (f"=Y{R - 1}-M{R}+X{R}" if i else f"=-M{R}+X{R}")
        f["Z"] = (f'=IF(Y{R - 1}-M{R}>=0,"OK","SHORT")' if i else f'=IF(M{R}=0,"OK","SHORT")')
        f["AA"] = f"=MIN(1,0.2+0.05*K{R})"
        f["AB"] = f"=MIN(12,2+0.5*J{R})"
        f["AC"] = f"=ActiveMin/60+Sessions*MIN(GapH,AB{R})*AA{R}"
        f["AD"] = f"=MAX(P{R}/AC{R},V{R}/24)"
        f["AE"] = f"=MAX(P{R}/(AC{R}+AdHoursPerDay),V{R}/24)"
        f["AF"] = f"=1-AE{R}/AD{R}"
        f["AG"] = f"=MAX(Q{R}/(ActiveMin/60+Sessions*MIN(GapH,2)*0.2),V{R}/24)"
        f["AH"] = f"=AH{R - 1}+AE{R}" if i else f"=AE{R}"
        f["AI"] = f"=CurBoostFrac*P{R}/2"
        fmts = {"C": "0", "M": "#,##0", "N": "0.00x", "O": "0%", "P": "0.0", "Q": "0.0", "R": "0",
                "S": "0", "U": "0.00", "V": "0.0", "W": "#,##0", "X": "#,##0", "Y": "#,##0",
                "AA": "0%", "AB": "0.0", "AC": "0.0", "AD": "0.0", "AE": "0.0", "AF": "0%", "AG": "0.0",
                "AH": "0.0", "AI": "0.00"}
        for col, formula in f.items():
            cell = pl[f"{col}{R}"]
            cell.value = formula
            cell.font = BLACK
            if col in fmts:
                cell.number_format = fmts[col]
            cell.border = THIN
    pend = p0 + len(PLAN) - 1
    tr = pend + 1
    pl.cell(row=tr, column=1, value="Total").font = BOLD
    for col in ("M", "P", "Q", "X", "AD", "AE", "AG"):
        c = pl[f"{col}{tr}"]
        c.value = f"=SUM({col}{p0}:{col}{pend})"
        c.font = BOLD
        c.number_format = "#,##0.0" if col not in ("M", "X") else "#,##0"
    pl.freeze_panes = "C5"
    notes = [
        ("B4", "Run ends with a prestige right after this research completes. Pick from the dropdown."),
        ("L4", "Live income multiplier from managers on the 5 buildings. One 2x OutputQuantity manager on one "
               "of 5 buildings = (4+2)/5 = 1.2x."),
        ("P4", "Active-play hours to earn every research up to the target: sum of cost/rate, with this run's "
               "discounts and multipliers, plus Overhead."),
        ("Q4", "Same run with nothing wired (Wired=0, LiveBonuses=0): only managers speed it up."),
        ("R4", "The single longest research grind in the run. This is the lull the player feels."),
        ("U4", "Time an always-on player waits on a research timer with nothing left to earn for. Near 0 means "
               "timers never gate progress: the grind does."),
        ("W4", "Net worth = entropy earned this run (NetWorthSystem counts spent entropy too)."),
        ("O4", "Memory Resonance part only. Each node's own memory discount applies only to research finished on "
               "an earlier run (up to the furthest earlier target), as ResearchCost.Effective charges it."),
        ("X4", "floor(log10(NW / base) x scale), then x(1 + 5% per Echo level). 0 below the wall."),
        ("Z4", "SHORT = the levels at this run's start cost more ✦ than earlier runs earned."),
        ("AC4", "Hours of full-speed income per day: active play plus offline earnings at the idle rate, "
                "each offline gap capped by the idle cap."),
        ("AG4", "Days per run with nothing wired, base idle stats and no ads."),
        ("AI4", "Today's Entropy Boost pays 10% of net worth. Net worth is everything earned this run, so the "
                "ad's value grows with run length instead of staying a fixed slice of time."),
    ]
    for ref, text in notes:
        pl[ref].comment = Comment(text, "prestige_plan.py")

    # ── Ads ──────────────────────────────────────────────────────────────────────────────
    ad = wb.create_sheet("Ads")
    ad["A1"], ad["A1"].font = "Ads for the post-prestige lull", TITLE
    ad.column_dimensions["A"].width = 34
    for col in "BCDEFG":
        ad.column_dimensions[col].width = 16
    proposal = [
        ("Shipped placement", "Entropy Surge: watch an ad for X minutes of your current automation income."),
        ("Reward", "=\"\"&AdReward&\" min of income (Statistics panel e/s x 60 x minutes)\""),
        ("Availability", "=\"Every \"&AdCooldown&\" min, up to \"&AdCap&\" per day, reset 00:00 UTC\""),
        ("Why income, not net worth",
         "Net worth includes everything spent this run, so a %-of-net-worth reward grows with run length "
         "(column F below). Income-minutes buys the same slice of time at every stage, which is what a "
         "15-minute cadence needs."),
        ("Where it shows", "HUD button with a cooldown ring; also on the idle-return modal (an ad is "
                           "always ready after being away 15 min)."),
        ("Relation to Entropy Boost", "Both ship today. Entropy Boost (10% of net worth, 5/day) still grows "
                                      "with run length; consider capping it at 1/day as a bigger treat."),
    ]
    r = 3
    for label, val in proposal:
        ad.cell(row=r, column=1, value=label).font = BOLD
        c = ad.cell(row=r, column=2, value=val)
        c.font = BLACK
        c.alignment = WRAP
        ad.merge_cells(start_row=r, start_column=2, end_row=r, end_column=7)
        ad.row_dimensions[r].height = 32
        r += 1
    r += 1
    ad.cell(row=r, column=1, value="Per run (from Plan)").font = BOLD
    r += 1
    header_row = r
    labels = ["Run", "Active hours", "Ad income per day (h)", "Days, no ads", "Days with Surge",
              "Current Boost: one ad mid-run (h)", "Current Boost: 5/day (h of income per day)"]
    for c, label in enumerate(labels, 1):
        cell = ad.cell(row=r, column=c, value=label)
        cell.font, cell.fill, cell.alignment = HDR, HDR_FILL, CENTER
    ad.row_dimensions[r].height = 45
    for i in range(len(PLAN)):
        R = p0 + i
        r += 1
        vals = [f"=Plan!A{R}", f"=Plan!P{R}", "=AdHoursPerDay", f"=Plan!AD{R}", f"=Plan!AE{R}",
                f"=Plan!AI{R}", f"=Plan!AI{R}*CurBoostCap"]
        for c, v in enumerate(vals, 1):
            cell = ad.cell(row=r, column=c, value=v)
            cell.font = GREEN if c != 3 else BLACK
            cell.number_format = "0" if c == 1 else "0.00"
    r += 2
    ad.cell(row=r, column=1,
            value="Read: compare column G with the run's daily income (Plan col AC). Once the 5 daily "
                  "Boosts pay more than a day of play, the run is bought rather than played.").alignment = WRAP
    ad.merge_cells(start_row=r, start_column=1, end_row=r, end_column=7)
    ad.row_dimensions[r].height = 32

    # ── Tests ────────────────────────────────────────────────────────────────────────────
    ts = wb.create_sheet("Tests")
    ts["A1"], ts["A1"].font = "How we test the ad and the pacing", TITLE
    header(ts, 3, ["Area", "Test", "Given", "Expect", "Kind"], [16, 34, 50, 34, 22])
    tests = [
        ("Ad reward", "Reward = income x minutes", "income 100 e/s, AdReward",
         "=\"\"&TEXT(100*60*AdReward,\"#,##0\")&\" e\"", "EditMode unit (calculator)"),
        ("Ad reward", "No income gives the floor, not 0", "income 0 e/s (nothing automated yet)",
         "floor (e.g. 500 e, same as Boost)", "EditMode unit"),
        ("Ad cooldown", "Not ready inside the cooldown", "last watch 14 min ago, cooldown 15",
         "not ready; ready at 15:00", "EditMode unit (inject now)"),
        ("Ad cooldown", "Ready after being away", "last watch 3 h ago",
         "ready on resume", "EditMode unit"),
        ("Ad cap", "Cap stops further watches", "=\"\"&AdCap&\" watched today\"",
         "not ready until 00:00 UTC", "EditMode unit (existing cap pattern)"),
        ("Ad persistence", "Cooldown survives relaunch", "save, reload",
         "same ready time (ISO-UTC field, like speedBoostExpiryUtc)", "EditMode round-trip + legacy load"),
        ("Ad grant", "Grant reaches live ECS", "watch while playing",
         "entropy rises now and after the next save (ECSLoadBridge.AddEntropy)", "EditMode, AdService test"),
        ("Pacing", "Every run's longest gap <= GapTarget", "Plan, Wired=1",
         "all Gap check = OK", "Workbook + scripts/prestige_plan.py --check"),
        ("Pacing", "Plan is affordable", "Plan", "all Affordable? = OK", "Workbook"),
        ("Pacing", "Live run time matches the model", "AI playtest: active-grinder from a run-2 save",
         "time to target within 25% of Plan col P", "Playtest brief (MCP harness)"),
        ("Ads in play", "Surge shortens a lull run", "dev console: advance time, watch ads",
         "run time down by ~Plan col AF", "Playtest brief + telemetry ad_watched"),
    ]
    for i, row in enumerate(tests):
        for c, v in enumerate(row, 1):
            cell = ts.cell(row=4 + i, column=c, value=v)
            cell.font = BLACK
            cell.alignment = WRAP

    # ── Summary (first tab) ──────────────────────────────────────────────────────────────
    sm = wb.create_sheet("Summary", 0)
    sm["A1"], sm["A1"].font = "Physics prestige pacing", TITLE
    sm.column_dimensions["A"].width = 6
    sm.column_dimensions["B"].width = 30
    for col in "CDEFGHIJ":
        sm.column_dimensions[col].width = 13
    findings = [
        "Why the game slowed after the first prestige (found 2026-11-11, all three fixes now live):",
        "1. Research costs never got cheaper. Fixed: ResearchService.EffectiveCost applies Memory Resonance "
        "and each node's prestige_memory_discount on repeat runs.",
        "2. Assembly Line and Quantum Yield only changed idle earnings. Fixed: they now multiply live "
        "production (ProductionSystem / CollectorSystem).",
        "3. Research timers almost never gate progress (Plan col U); the wait is the entropy grind between "
        "unlocks (Plan col R). Entropy Surge (10 min of income, 15 min cooldown, 8/day) shipped to soften it.",
        "4. ✦ per run grows slowly (log formula). Columns below come from Plan; flip Inputs!Wired / "
        "LiveBonuses to 0 to compare with the pre-fix build (cols 'pre-fix').",
    ]
    for i, text in enumerate(findings):
        c = sm.cell(row=3 + i, column=1, value=text)
        c.font = BOLD if i == 0 else BLACK
        c.alignment = WRAP
        sm.merge_cells(start_row=3 + i, start_column=1, end_row=3 + i, end_column=10)
        sm.row_dimensions[3 + i].height = 15 if i == 0 else 30
    hr = 3 + len(findings) + 1
    labels = ["Run", "Prestige after", "Active h", "Active h pre-fix", "Longest gap (min)",
              "Gap check", "✦ earned", "Days (no ads)", "Days with ads", "Days pre-fix"]
    src = ["A", "B", "P", "Q", "R", "T", "X", "AD", "AE", "AG"]
    for c, label in enumerate(labels, 1):
        cell = sm.cell(row=hr, column=c, value=label)
        cell.font, cell.fill, cell.alignment = HDR, HDR_FILL, CENTER
    sm.row_dimensions[hr].height = 32
    fm = {"P": "0.0", "Q": "0.0", "R": "0", "X": "#,##0", "AD": "0.0", "AE": "0.0", "AG": "0.0"}
    for i in range(len(PLAN)):
        R = p0 + i
        for c, col in enumerate(src, 1):
            cell = sm.cell(row=hr + 1 + i, column=c, value=f"=Plan!{col}{R}")
            cell.font = GREEN
            if col in fm:
                cell.number_format = fm[col]
    trs = hr + 1 + len(PLAN)
    sm.cell(row=trs, column=2, value="Total").font = BOLD
    for c, col in ((3, "P"), (4, "Q"), (7, "X"), (8, "AD"), (9, "AE"), (10, "AG")):
        cell = sm.cell(row=trs, column=c, value=f"=Plan!{col}{tr}")
        cell.font = Font(name=F, bold=True, color="008000")
        cell.number_format = "#,##0" if col == "X" else "0.0"
    legend = trs + 2
    sm.cell(row=legend, column=1, value="How to use").font = BOLD
    for i, text in enumerate([
        "Edit yellow cells only: global levers on Inputs, per-run targets and upgrade levels on Plan.",
        "Blue text = typed value, black = formula, green = pulled from another sheet.",
        "Research numbers are regenerated from the game data by: python scripts/prestige_plan.py "
        "(overwrites this file).",
    ]):
        c = sm.cell(row=legend + 1 + i, column=1, value=text)
        c.font = BLACK
        sm.merge_cells(start_row=legend + 1 + i, start_column=1, end_row=legend + 1 + i, end_column=10)

    for ws in wb.worksheets:
        ws.sheet_view.showGridLines = ws.title not in ("Summary", "Ads", "Tests")
        for row in ws.iter_rows():
            for cell in row:
                if cell.font and cell.font.name != F:
                    cell.font = Font(name=F, bold=cell.font.bold, italic=cell.font.italic,
                                     color=cell.font.color, size=cell.font.sz)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    wb.save(OUT)
    print(OUT)
    write_html(curve, inputs)


if __name__ == "__main__":
    build()
