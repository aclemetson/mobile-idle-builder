"""Validates live-ops/gamedata.json and builds the gamedata.* Remote Config payload (#210).

live-ops/gamedata.json is the reviewed source for the game-data keys players fetch at launch:

    {
      "version": 1,                                   -> gamedata.version (int, analytics label)
      "notice": { "title": "...", "message": "..." }, -> gamedata.noticeTitle / gamedata.noticeMessage
      "overrides": { "config": [...], "items": [...], "research": [...] }   -> gamedata.overrides (string)
    }

The overrides schema is the one GameDataOverrides.cs reads (docs/agents/feature-flags.md, "Game-data
overrides"). The allowed fields are read from the Set*Field switches in GameDataOverrides.cs, and item and
research ids from Assets/Data/game_data.json, so this check never drifts from what the game honours.

    python scripts/gamedata_publish.py validate [file]
    python scripts/gamedata_publish.py build <out.json> [file] [--announce]

build writes the key -> value map for scripts/set-remote-config.ps1 -JsonFile (one PUT for all keys).
--announce also sets gamedata.updatedUtc to now, so every player sees the update notice once on next launch.
"""
import json
import math
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_FILE = ROOT / "live-ops" / "gamedata.json"
OVERRIDES_CS = ROOT / "Assets" / "Scripts" / "RemoteConfig" / "GameDataOverrides.cs"
GAME_DATA = ROOT / "Assets" / "Data" / "game_data.json"

SECTIONS = {"config": "SetConfigField", "items": "SetItemField", "research": "SetResearchField"}
MAX_TITLE = 60
MAX_MESSAGE = 600
# Remote Config string values are capped well above this; keep the blob small so the launch fetch stays cheap.
MAX_OVERRIDES_CHARS = 20000


def allowed_fields(cs_text: str) -> dict:
    """Section -> set of field names from the matching `static bool Set*Field` switch."""
    out = {}
    for section, method in SECTIONS.items():
        m = re.search(r"static\s+bool\s+" + method + r"\s*\(.*?\{(.*?)\n\s*default:", cs_text, re.S)
        if not m:
            raise ValueError(f"{OVERRIDES_CS.name}: could not find the {method} switch")
        out[section] = set(re.findall(r'case\s+"(\w+)"\s*:', m.group(1)))
        if not out[section]:
            raise ValueError(f"{OVERRIDES_CS.name}: {method} has no cases")
    return out


def known_ids(game_data: dict) -> dict:
    return {
        "items": {i["id"] for i in game_data.get("items", []) if i.get("id")},
        "research": {r["id"] for r in game_data.get("research", []) if r.get("id")},
    }


def _is_number(v) -> bool:
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def validate(doc, fields: dict, ids: dict) -> list:
    """Returns a list of error strings; empty means the document is publishable."""
    errors = []
    if not isinstance(doc, dict):
        return ["top level must be a JSON object"]

    unknown_top = set(doc) - {"version", "notice", "overrides", "_comment"}
    if unknown_top:
        errors.append(f"unknown top-level key(s): {', '.join(sorted(unknown_top))}")

    version = doc.get("version")
    if not isinstance(version, int) or isinstance(version, bool) or version < 0:
        errors.append("version must be a non-negative integer")

    notice = doc.get("notice", {})
    if not isinstance(notice, dict):
        errors.append("notice must be an object with title and message")
    else:
        for key, cap in (("title", MAX_TITLE), ("message", MAX_MESSAGE)):
            val = notice.get(key)
            if not isinstance(val, str) or not val.strip():
                errors.append(f"notice.{key} must be a non-empty string")
            elif len(val) > cap:
                errors.append(f"notice.{key} is {len(val)} chars (max {cap})")
        extra = set(notice) - {"title", "message"}
        if extra:
            errors.append(f"notice has unknown key(s): {', '.join(sorted(extra))}")

    overrides = doc.get("overrides")
    if not isinstance(overrides, dict):
        errors.append("overrides must be an object ({} for none)")
        return errors

    extra = set(overrides) - set(SECTIONS)
    if extra:
        errors.append(f"overrides has unknown section(s): {', '.join(sorted(extra))}")

    for section in SECTIONS:
        entries = overrides.get(section, [])
        if not isinstance(entries, list):
            errors.append(f"overrides.{section} must be an array")
            continue
        seen = set()
        for n, e in enumerate(entries):
            where = f"overrides.{section}[{n}]"
            if not isinstance(e, dict):
                errors.append(f"{where} must be an object")
                continue
            needs_id = section != "config"
            allowed_keys = {"id", "field", "value"} if needs_id else {"field", "value"}
            extra = set(e) - allowed_keys
            if extra:
                errors.append(f"{where} has unknown key(s): {', '.join(sorted(extra))}")
            field = e.get("field")
            if field not in fields[section]:
                errors.append(f"{where}: field '{field}' is not overridable "
                              f"(allowed: {', '.join(sorted(fields[section]))})")
            ident = e.get("id") if needs_id else None
            if needs_id and ident not in ids[section]:
                errors.append(f"{where}: unknown {section} id '{ident}'")
            value = e.get("value")
            if not _is_number(value):
                errors.append(f"{where}: value must be a finite number")
            elif value < 0:
                errors.append(f"{where}: value must not be negative")
            key = (ident, field)
            if key in seen:
                errors.append(f"{where}: duplicate override for {ident + '.' if ident else ''}{field}")
            seen.add(key)

    blob = overrides_blob(overrides)
    if len(blob) > MAX_OVERRIDES_CHARS:
        errors.append(f"overrides is {len(blob)} chars (max {MAX_OVERRIDES_CHARS})")
    return errors


def overrides_blob(overrides: dict) -> str:
    """Compact JSON with only the non-empty sections, the shape GameDataOverrides.Parse reads."""
    trimmed = {s: overrides[s] for s in SECTIONS if overrides.get(s)}
    return json.dumps(trimmed, separators=(",", ":"), ensure_ascii=False)


def build_payload(doc: dict, announce: bool, now: datetime = None) -> dict:
    payload = {
        "gamedata.version": doc["version"],
        "gamedata.noticeTitle": doc["notice"]["title"],
        "gamedata.noticeMessage": doc["notice"]["message"],
        "gamedata.overrides": overrides_blob(doc["overrides"]),
    }
    if announce:
        now = now or datetime.now(timezone.utc)
        payload["gamedata.updatedUtc"] = now.strftime("%Y-%m-%dT%H:%M:%SZ")
    return payload


def load_and_validate(path: Path):
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as ex:
        return None, [f"{path}: {ex}"]
    fields = allowed_fields(OVERRIDES_CS.read_text(encoding="utf-8"))
    ids = known_ids(json.loads(GAME_DATA.read_text(encoding="utf-8")))
    return doc, validate(doc, fields, ids)


def main(argv):
    args = [a for a in argv[1:] if not a.startswith("--")]
    flags = {a for a in argv[1:] if a.startswith("--")}
    if not args or args[0] not in ("validate", "build") or flags - {"--announce"}:
        sys.exit(__doc__)

    if args[0] == "validate":
        path = Path(args[1]) if len(args) > 1 else DEFAULT_FILE
        out = None
    else:
        if len(args) < 2:
            sys.exit(__doc__)
        out = Path(args[1])
        path = Path(args[2]) if len(args) > 2 else DEFAULT_FILE

    doc, errors = load_and_validate(path)
    if errors:
        for e in errors:
            print(f"error: {e}")
        print(f"{path}: {len(errors)} error(s), not publishable")
        return 1

    n = sum(len(doc["overrides"].get(s, [])) for s in SECTIONS)
    print(f"{path}: OK (version {doc['version']}, {n} override(s))")
    if out:
        payload = build_payload(doc, "--announce" in flags)
        out.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"wrote {out}: {', '.join(payload)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
