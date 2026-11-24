"""Builds the what's-new Remote Config payload (#502) from docs/whats-new.md.

The release workflow runs this after the Android build and passes the JSON to
scripts/set-remote-config.ps1 -JsonFile, which writes whatsnew.notes and whatsnew.version in one PUT.
The game shows the notes once on a build at least that version (Assets/Scripts/RemoteConfig/WhatsNew.cs).

    python scripts/whats_new.py <version> <out.json>     # prints "skip" and writes nothing if there are no notes

docs/whats-new.md holds the player-facing notes for the next release. HTML comments are for maintainers and
are stripped; everything else is shown as written (plain text, one point per line).
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
NOTES = ROOT / "docs" / "whats-new.md"
MAX_CHARS = 1200   # the card scrolls, but keep it a quick read


def notes_text(md: str) -> str:
    text = re.sub(r"<!--.*?-->", "", md, flags=re.S)
    lines = [ln.rstrip() for ln in text.replace("\r\n", "\n").split("\n")]
    # collapse runs of blank lines left by stripped comments
    out, blank = [], False
    for ln in lines:
        if not ln.strip():
            if out and not blank:
                out.append("")
            blank = True
            continue
        out.append(ln)
        blank = False
    return "\n".join(out).strip()


def main(argv):
    if len(argv) != 3:
        sys.exit(__doc__)
    version, out = argv[1].strip(), Path(argv[2])
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        sys.exit(f"bad version {version!r}")
    text = notes_text(NOTES.read_text(encoding="utf-8")) if NOTES.exists() else ""
    if not text:
        print("skip")
        return
    if len(text) > MAX_CHARS:
        sys.exit(f"docs/whats-new.md is {len(text)} characters; keep it under {MAX_CHARS}")
    out.write_text(json.dumps({"whatsnew.notes": text, "whatsnew.version": version}, ensure_ascii=False),
                   encoding="utf-8")
    print(f"wrote {out} ({len(text)} chars) for {version}")


if __name__ == "__main__":
    main(sys.argv)
