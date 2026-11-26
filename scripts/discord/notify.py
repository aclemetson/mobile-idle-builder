"""Posts CI messages to Discord channel webhooks (#533). Standard library only.

    python scripts/discord/notify.py release --webhook-env DISCORD_BETA_WEBHOOK --version 0.6.133 \
        --kind beta [--ping-role-env DISCORD_BETA_PING_ROLE_ID] [--url https://...]
    python scripts/discord/notify.py alert --webhook-env DISCORD_STAFF_WEBHOOK --text "Release Internal failed" \
        [--url https://github.com/.../actions/runs/1]

release: posts "<version> is out" with the player-facing notes from docs/whats-new.md (the same text the
         game's what's-new card shows; scripts/whats_new.py strips the maintainer comments).
alert:   posts a one-line staff alert (CI failures).

Every value comes from an environment variable named on the command line, never from the command line itself,
so webhook URLs never reach the job log. A missing webhook prints "skip" and exits 0: Discord is optional and
never fails a release. Mentions are locked down with allowed_mentions: only the one ping role given can be
pinged, so notes text containing @everyone or a role mention pings nobody.
"""
import argparse
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from whats_new import NOTES, notes_text  # noqa: E402

WEBHOOK_RE = re.compile(r"^https://(?:canary\.|ptb\.)?discord(?:app)?\.com/api/webhooks/\d+/[\w-]+$")
TEAL = 0x22D3EE
RED = 0xF87171
STORE_LINKS = {
    "beta": "Update from the Play Store (internal testing) or TestFlight.",
    "release": "Update from Google Play or the App Store.",
}


def webhook_from_env(name: str) -> str | None:
    url = (os.environ.get(name) or "").strip()
    if not url:
        return None
    if not WEBHOOK_RE.match(url):
        sys.exit(f"{name} is not a Discord webhook URL (https://discord.com/api/webhooks/<id>/<token>)")
    return url


def role_from_env(name: str | None) -> str | None:
    if not name:
        return None
    role = (os.environ.get(name) or "").strip()
    if role and not role.isdigit():
        sys.exit(f"{name} must be a numeric Discord role ID")
    return role or None


def release_payload(version: str, kind: str, notes: str, role: str | None, url: str | None) -> dict:
    title = f"Quantum Foundry {version} " + ("beta build is ready" if kind == "beta" else "is out")
    body = notes or "Bug fixes and improvements."
    body += "\n\n" + STORE_LINKS[kind]
    if kind == "beta":
        body += " Report problems in #beta-feedback."
    embed = {"title": title, "description": body[:4000], "color": TEAL}
    if url:
        embed["url"] = url
    return {
        "content": f"<@&{role}>" if role else "",
        "embeds": [embed],
        "allowed_mentions": {"parse": [], "roles": [role] if role else []},
    }


def alert_payload(text: str, url: str | None) -> dict:
    embed = {"title": text[:250], "color": RED}
    if url:
        embed["url"] = url
    return {"content": "", "embeds": [embed], "allowed_mentions": {"parse": []}}


def post(url: str, payload: dict) -> None:
    data = json.dumps(payload).encode("utf-8")
    for attempt in range(3):
        req = urllib.request.Request(url + "?wait=true", data=data, method="POST", headers={
            "Content-Type": "application/json", "User-Agent": "DiscordBot (https://github.com, 1.0)"})
        try:
            with urllib.request.urlopen(req, timeout=30) as resp:
                print(f"posted (HTTP {resp.status})")
                return
        except urllib.error.HTTPError as e:
            if e.code == 429 and attempt < 2:
                time.sleep(float(json.loads(e.read() or b"{}").get("retry_after", 2)) + 0.5)
                continue
            # The error body never contains the webhook token; the URL is not printed.
            sys.exit(f"Discord webhook failed: HTTP {e.code} {e.read()[:300]!r}")


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("release")
    r.add_argument("--webhook-env", required=True)
    r.add_argument("--version", required=True)
    r.add_argument("--kind", choices=["beta", "release"], required=True)
    r.add_argument("--ping-role-env")
    r.add_argument("--url")
    a = sub.add_parser("alert")
    a.add_argument("--webhook-env", required=True)
    a.add_argument("--text", required=True)
    a.add_argument("--url")
    args = p.parse_args(argv)

    url = webhook_from_env(args.webhook_env)
    if not url:
        print(f"skip: {args.webhook_env} is not set")
        return
    if args.cmd == "release":
        if not re.fullmatch(r"\d+\.\d+\.\d+", args.version):
            sys.exit(f"bad version {args.version!r}")
        notes = notes_text(NOTES.read_text(encoding="utf-8")) if NOTES.exists() else ""
        payload = release_payload(args.version, args.kind, notes, role_from_env(args.ping_role_env), args.url)
    else:
        payload = alert_payload(args.text, args.url)
    post(url, payload)


if __name__ == "__main__":
    main()
