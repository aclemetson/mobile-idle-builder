"""Generate Playtests/tutorial-walkthrough.md from the tutorial asset and game data.

  python scripts/generate-tutorial-walkthrough.py

Sources (read-only):
  Assets/Data/tutorial/tutorial_flow.asset   step order, hints, advance and gating rules
  Assets/Data/game_data.json                 item, research, building and recipe names
  Assets/**/*.asset + their .meta files      dialogue text, resolved from the GUIDs in the steps

Each step gets a section: what it says, what advances it, what it blocks, and how to get there.
Steps that block field collection while asking for items are flagged, since the player can only
finish them with stock they already hold.
"""
import json
import re
import sys
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parent.parent
TUTORIAL = ROOT / "Assets" / "Data" / "tutorial" / "tutorial_flow.asset"
GAME_DATA = ROOT / "Assets" / "Data" / "game_data.json"
OUT = ROOT / "Playtests" / "tutorial-walkthrough.md"

# Matches the enums in Assets/Scripts/Enums/GameEnums.cs (ConditionType order).
CONDITION_TYPES = [
    "Auto", "InventoryMin", "InventoryZero", "UiEvent", "ResearchUnlocked",
    "BuildingMin", "PrestigeRunMin", "PrestigeAvailable", "ConveyorLinked",
    "BuildingUpgradeMin", "FieldPurchasedMin",
]
HIGHLIGHT_MODES = ["None", "Full", "VisualOnly"]
INTERACTION_GATES = ["None", "BlockAll", "EntropySinkOnly", "AtomicAssemblerOnly"]


class UnityLoader(yaml.SafeLoader):
    """SafeLoader that ignores Unity's !u!114 style tags."""


def _keep_value(loader, suffix, node):
    """Unity tags (!u!114 etc.) carry no meaning here; keep the value under them."""
    if isinstance(node, yaml.MappingNode):
        return loader.construct_mapping(node, deep=True)
    if isinstance(node, yaml.SequenceNode):
        return loader.construct_sequence(node, deep=True)
    return loader.construct_scalar(node)


UnityLoader.add_multi_constructor("!", _keep_value)
UnityLoader.add_multi_constructor("tag:unity3d.com,2011:", _keep_value)


def load_unity_yaml(path):
    with open(path, encoding="utf-8") as f:
        text = f.read()
    # Unity's hex int[] strings (e.g. 03000000) would be read as octal ints; quote them so they stay text.
    text = re.sub(r"^(\s*\w+: )([0-9a-f]{8,})$", r'\1"\2"', text, flags=re.M)
    return yaml.load(text, Loader=UnityLoader)


def unity_int_array(value):
    """Unity writes int[] as a hex string of little-endian 32-bit ints (empty = no entries)."""
    if not value:
        return []
    if isinstance(value, list):
        return value
    raw = bytes.fromhex(str(value))
    return [int.from_bytes(raw[i:i + 4], "little", signed=True) for i in range(0, len(raw), 4)]


def enum_name(values, index):
    if isinstance(index, int) and 0 <= index < len(values):
        return values[index]
    return f"?{index}"


def build_guid_index():
    """Map asset GUID to the asset file path, from the .meta files under Assets/."""
    index = {}
    for meta in (ROOT / "Assets").rglob("*.meta"):
        with open(meta, encoding="utf-8", errors="replace") as f:
            for line in f:
                m = re.match(r"guid:\s*([0-9a-f]{32})", line)
                if m:
                    index[m.group(1)] = meta.with_suffix("")
                    break
    return index


def dialogue_lines(guid, guid_index):
    path = guid_index.get(guid)
    if path is None or not path.exists():
        return None, []
    data = load_unity_yaml(path) or {}
    body = data.get("MonoBehaviour") or data
    lines = body.get("lines") or []
    return path.name, lines


def main():
    game = json.loads(GAME_DATA.read_text(encoding="utf-8"))
    items_by_num = {it["item_id"]: it for it in game["items"] if "item_id" in it}
    items_by_id = {it["id"]: it for it in game["items"]}
    research_by_id = {r["id"]: r for r in game["research"]}
    buildings_by_num = {b["building_id"]: b for b in game["buildings"] if "building_id" in b}
    recipes = game["recipes"]

    def item_name(num):
        it = items_by_num.get(num)
        return it["display_name"] if it else f"item#{num}"

    def research_name(rid):
        r = research_by_id.get(rid)
        return f"{r['display_name']} (`{rid}`)" if r else f"`{rid}`"

    def building_name(num):
        if num is None or num < 0:
            return None
        b = buildings_by_num.get(num)
        return f"{b['display_name']} (`{b['id']}`)" if b else f"building#{num}"

    def recipes_for(item_id):
        return [r for r in recipes if r.get("output_item") == item_id]

    def recipe_line(r):
        inputs = ", ".join(
            f"{i['quantity']} x {items_by_id[i['item']]['display_name'] if i['item'] in items_by_id else i['item']}"
            for i in r.get("inputs", [])
        )
        if r.get("can_craft_manually"):
            how = "craft manually from the recipe panel (btn-recipes)"
        else:
            how = "needs a building: " + ", ".join(
                f"`{b}`" for b in r.get("valid_buildings", [])
            )
        return f"{r['display_name']}: {inputs}; {how}"

    asset = load_unity_yaml(TUTORIAL)
    asset = asset.get("MonoBehaviour", asset)
    steps = asset["steps"]
    guid_index = build_guid_index()
    step_index = {s["id"]: i + 1 for i, s in enumerate(steps)}

    out = []
    out.append("# Tutorial walkthrough")
    out.append("")
    out.append(
        "Generated by `scripts/generate-tutorial-walkthrough.py` from "
        "`Assets/Data/tutorial/tutorial_flow.asset` and `Assets/Data/game_data.json`. "
        "Do not edit by hand; regenerate after changing either source."
    )
    out.append("")
    out.append(f"{len(steps)} steps, in the order the tutorial runs them. Each section lists what the")
    out.append("tutorial says, what advances the step, what it blocks, and how to get the items it asks for.")
    out.append("")
    out.append("## Flags")
    out.append("")
    out.append(
        "**Collection-blocked item steps.** The step blocks field collection and waits for items. "
        "The player can only finish it with stock already in the inventory. "
        "If earlier steps spent or sold those items, the step cannot be finished. "
        "Each one is marked **[check stock]** below."
    )
    out.append("")

    # Shared rule for every ConveyorLinked step. Matches TutorialRouteGuide.cs: a belt counts only if
    # it starts on the source's output front and its chain ends on the target's input front.
    CONVEYOR_HOWTO = (
        "a belt links only when it starts on the glowing tile that the source's output faces, and "
        "ends on the glowing tile in front of the target's input. Tap the start tile, then the end "
        "tile, then check and Done. Use the glowing tiles; do not pick tiles by guessing coordinates. "
        "Maxwell's Demon is 3x3 with input ports along its edges, and a belt that ends at its corner "
        "did not link in playtest. A belt that links nothing shows the warning \"Belt not linked\" about "
        "half a second after check, and the camera returns to the glowing tiles: the belt is wrong. "
        "Extend it from its end onto the glowing tile, or remove it and redraw it."
    )

    warnings = []
    body = []
    for n, step in enumerate(steps, start=1):
        sid = step["id"]
        adv = step.get("advanceCondition") or {}
        enter = step.get("onEnter") or {}
        skip = step.get("skipCondition") or {}
        ctype = enum_name(CONDITION_TYPES, adv.get("type", 0))
        block_collection = bool(enter.get("blockCollection"))
        flags = []

        body.append(f"### {n}. `{sid}`")
        if step.get("isSubstep"):
            body[-1] += "  _(substep)_"
        body.append("")
        hint = (step.get("hintText") or "").strip()
        body.append(f"- **Hint:** {hint if hint else '_none_'}")

        # What the step asks for
        if ctype == "Auto":
            goal = "advances immediately on entry"
        elif ctype == "InventoryMin":
            reqs = []
            for it in adv.get("items") or []:
                name = item_name(it["itemId"])
                reqs.append(f"{it['quantity']} x {name}")
            joiner = " or " if adv.get("anyOf") else " and "
            goal = "hold " + joiner.join(reqs) if reqs else "hold items"
        elif ctype == "InventoryZero":
            goal = "inventory empty of: " + ", ".join(
                item_name(i["itemId"]) for i in adv.get("items") or []
            )
        elif ctype == "UiEvent":
            goal = f"UI event `{adv.get('uiEventId')}`"
        elif ctype == "ResearchUnlocked":
            goal = "research done: " + research_name(adv.get("researchId"))
        elif ctype == "BuildingMin":
            goal = f"have at least {adv.get('minCount')} x {building_name(adv.get('buildingType')) or 'any building'}"
        elif ctype == "PrestigeRunMin":
            goal = f"complete at least {adv.get('minCount')} prestige run(s)"
        elif ctype == "PrestigeAvailable":
            goal = "prestige is available"
        elif ctype == "ConveyorLinked":
            goal = f"link at least {adv.get('minCount')} conveyor(s) into {building_name(adv.get('buildingType')) or 'the target'}"
        elif ctype == "BuildingUpgradeMin":
            goal = f"upgrade {building_name(adv.get('buildingType')) or 'a building'} to level {adv.get('minLevel')}"
        elif ctype == "FieldPurchasedMin":
            goal = f"buy field `{adv.get('fieldId')}` at least {adv.get('minCount')} time(s)"
        else:
            goal = ctype
        body.append(f"- **Advances when:** {goal}")

        # Dialogue shown on entry
        dlg_name, lines = dialogue_lines((enter.get("dialogue") or {}).get("guid", ""), guid_index)
        if lines:
            body.append(f"- **Dialogue** (`{dlg_name}`):")
            for line in lines:
                speaker = (line.get("speakerName") or "").strip()
                text = " ".join((line.get("text") or "").split())
                body.append(f"  - {speaker + ': ' if speaker else ''}{text}")
        elif (enter.get("dialogue") or {}).get("guid"):
            body.append("- **Dialogue:** (asset not found)")

        # What it blocks and gates
        gates = []
        if block_collection:
            gates.append("field collection is **blocked**")
            if ctype == "InventoryMin":
                flags.append("check stock")
        cf = enter.get("collectionFilter")
        if cf and cf != "None":
            gates.append(f"collection limited to `{cf}`")
        ig = enum_name(INTERACTION_GATES, enter.get("buildingInteractionGate", 0))
        if ig != "None":
            gates.append(f"building taps: `{ig}`")
        locked_research = [research_name(r) for r in step.get("lockedResearchIds") or []]
        if locked_research:
            gates.append("research locked: " + ", ".join(locked_research))
        for lm in enter.get("lockedMessages") or []:
            if lm.get("message"):
                gates.append(f"tapping {lm.get('triggerId')} shows: \"{lm['message']}\"")
        if gates:
            body.append("- **Blocks:** " + "; ".join(gates))

        # Where to tap
        tap = []
        pulse = enter.get("pulseButtonId")
        if pulse:
            tap.append(f"pulses button `{pulse}`")
        hl = enter.get("highlightTarget")
        if hl:
            tap.append(f"highlights `{hl}` ({enum_name(HIGHLIGHT_MODES, enter.get('highlightMode', 0))})")
        demon_items = unity_int_array(enter.get("demonHighlightItemIds"))
        if demon_items:
            tap.append("demon highlights " + ", ".join(item_name(i) for i in demon_items))
        if tap:
            body.append("- **Tap:** " + "; ".join(tap))
        if ctype == "ConveyorLinked":
            body.append(f"- **Conveyor placement:** {CONVEYOR_HOWTO}")

        # How to get the items
        if ctype == "InventoryMin":
            for it in adv.get("items") or []:
                num = it["itemId"]
                item = items_by_num.get(num)
                if not item:
                    continue
                recs = recipes_for(item["id"])
                if recs and item.get("category") not in ("RawResource",):
                    body.append(f"- **How to get {item['display_name']}:**")
                    for r in recs:
                        body.append(f"  - {recipe_line(r)}")
                elif item.get("is_harvested"):
                    body.append(f"- **How to get {item['display_name']}:** collect from a `{item.get('field_type', 'field')}` field")
        if ctype == "ResearchUnlocked":
            rid = adv.get("researchId")
            r = research_by_id.get(rid)
            if r:
                body.append(f"- **Cost:** {r.get('cost_base_currency')} entropy, {r.get('duration_seconds')} s")

        # Skip rules
        if skip.get("researchId") or skip.get("skipToId"):
            parts = []
            if skip.get("researchId"):
                parts.append(f"skipped if {research_name(skip['researchId'])} is done")
            if skip.get("skipToId"):
                parts.append(f"otherwise jumps to `{skip['skipToId']}`")
            body.append("- **Skip:** " + "; ".join(parts))

        if "check stock" in flags:
            body.append("- **[check stock]** this step blocks collection while asking for items; carry the items in from earlier steps.")
            warnings.append((n, sid))

        body.append("")

    out.append("## Collection-blocked item steps")
    out.append("")
    if warnings:
        for n, sid in warnings:
            out.append(f"- Step {n}: `{sid}`")
    else:
        out.append("None.")
    out.append("")
    out.append("## Steps")
    out.append("")
    out.extend(body)

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(out), encoding="utf-8")
    print(f"Wrote {OUT} ({len(steps)} steps, {len(warnings)} flagged)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
