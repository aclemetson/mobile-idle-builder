#!/usr/bin/env python3
"""Deletes the test players the Editor leaves behind (docs/runbooks/player-support.md, "Purge test players").

Every "clear all save" or lost Editor sign-in creates a new anonymous UGS player that nobody can sign in as
again. This lists the project's players and deletes only the ones that are clearly tests:

  KEEP  any linked identity (Google, username, ...): a real sign-in
  KEEP  any Cloud Save data in production: a store install
  KEEP  a development save whose `platforms` stamp lists Android or iOS (SavePlatform.cs): an internal-track phone
  KEEP  a development save with no stamp (written before the stamp existed) unless --include-unstamped
  KEEP  outside --created-after / --created-before, when given
  PURGE the rest: no linked identity, nothing in production, and no save or an Editor-only save

Deleting a player removes only its sign-in account (Unity docs), so its Cloud Save data in both environments
is deleted first. Deletions cannot be undone. Dry run by default: prints what it would do and changes nothing.

    python scripts/purge_test_players.py                                  # dry run, every player
    python scripts/purge_test_players.py --created-before 2026-11-20      # dry run, older players only
    python scripts/purge_test_players.py --confirm --max 50               # delete up to 50
    python scripts/purge_test_players.py --ids ids.txt                    # only these ids (one per line; still filtered)

Env: UGS_PURGE_SERVICE_KEY_ID / UGS_PURGE_SERVICE_KEY_SECRET (service account: Player Authentication admin +
Cloud Save Editor, see the runbook), UGS_PROJECT_ID, UGS_ENV_ID_DEVELOPMENT, UGS_ENV_ID_PRODUCTION.
Every run writes purge-<timestamp>.jsonl (one line per player and decision) to the current folder.
"""
import argparse
import base64
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone

API = "https://services.api.unity.com"
SAVE_KEY = "save_v1"                       # UGSCloudSaveService.k_SaveKey
PHONE_PLATFORMS = {"Android", "iOS"}       # SavePlatform.Android / SavePlatform.IOS
ACCESS_PATHS = {                           # Cloud Save access class -> items path under .../players/{id}
    "default": "items",
    "public": "public/items",
    "protected": "protected/items",
    "private": "private/items",
}


# ── Decisions (pure, unit-tested in scripts/test_purge_test_players.py) ────────────────────────────────────

def parse_created(value):
    """createdAt as a UTC datetime. The API documents a numeric string; ISO 8601 is accepted too. None if unreadable."""
    if value is None or value == "":
        return None
    s = str(value).strip()
    if s.isdigit():
        n = int(s)
        if n > 10**14:
            n //= 1000          # microseconds -> ms
        if n > 10**11:
            n /= 1000           # ms -> s
        return datetime.fromtimestamp(n, tz=timezone.utc)
    try:
        return datetime.fromisoformat(s.replace("Z", "+00:00")).astimezone(timezone.utc)
    except ValueError:
        return None


def save_platforms(save_value):
    """The `platforms` stamp of a stored save (JSON string or object). None = no stamp (or unreadable)."""
    if save_value is None:
        return None
    data = save_value
    if isinstance(save_value, str):
        try:
            data = json.loads(save_value)
        except ValueError:
            return None
    if not isinstance(data, dict):
        return None
    platforms = data.get("platforms")
    return list(platforms) if isinstance(platforms, list) and platforms else None


def decide(player, in_production, dev_save, created_after=None, created_before=None, include_unstamped=False):
    """("purge" | "keep", reason). dev_save: None = no development save, else the platforms list or None for unstamped."""
    if player.get("externalIds"):
        providers = ",".join(sorted({str(x.get("providerId", "?")) for x in player["externalIds"]}))
        return "keep", f"linked identity ({providers})"
    if in_production:
        return "keep", "has production data"
    created = parse_created(player.get("createdAt"))
    if created_after or created_before:
        if created is None:
            return "keep", "creation date unreadable"
        if created_after and created < created_after:
            return "keep", "created before --created-after"
        if created_before and created >= created_before:
            return "keep", "created after --created-before"
    if dev_save == "none":
        return "purge", "no save anywhere"
    if dev_save is None:
        return ("purge", "unstamped development save (--include-unstamped)") if include_unstamped \
            else ("keep", "development save without a platform stamp")
    phones = PHONE_PLATFORMS.intersection(dev_save)
    if phones:
        return "keep", f"save was on {', '.join(sorted(phones))}"
    return "purge", f"save only from {', '.join(dev_save)}"


# ── HTTP ───────────────────────────────────────────────────────────────────────────────────────────────────

class Api:
    def __init__(self, key_id, secret):
        self.header = "Basic " + base64.b64encode(f"{key_id}:{secret}".encode()).decode()

    def call(self, method, path, query=None, attempts=6):
        """(status, parsed JSON or None). Retries 429 with Retry-After and transient 5xx."""
        url = API + path + ("?" + urllib.parse.urlencode(query, doseq=True) if query else "")
        for i in range(attempts):
            req = urllib.request.Request(url, method=method, headers={"Authorization": self.header})
            try:
                with urllib.request.urlopen(req, timeout=30) as r:
                    raw = r.read()
                    return r.status, (json.loads(raw) if raw else None)
            except urllib.error.HTTPError as e:
                if e.code == 429 or (e.code >= 500 and i < attempts - 1):
                    wait = float(e.headers.get("Retry-After") or 2 ** i)
                    print(f"  HTTP {e.code} on {method} {path}: waiting {wait:.0f}s")
                    time.sleep(min(wait, 120))
                    continue
                return e.code, None
            except urllib.error.URLError as e:
                if i < attempts - 1:
                    time.sleep(2 ** i)
                    continue
                return f"network error ({e.reason})", None
        return 429, None


def list_players(api, project):
    players, page = [], None
    while True:
        q = {"limit": 1000}
        if page:
            q["page"] = page
        status, data = api.call("GET", f"/player-identity/v1/projects/{project}/users", q)
        if status != 200:
            sys.exit(f"Listing players failed: HTTP {status}. Does the key have player_auth.users.list?")
        players += data.get("results") or []
        page = data.get("next")
        if not page:
            return players


def players_with_data(api, project, env_id):
    """player id -> {access class: numKeys} for every player with Cloud Save data in this environment."""
    out, start = {}, None
    while True:
        q = {"limit": 100}
        if start:
            q["start"] = start
        status, data = api.call("GET", f"/cloud-save/v1/data/projects/{project}/environments/{env_id}/players", q)
        if status == 404:
            return out
        if status != 200:
            sys.exit(f"Listing Cloud Save players failed: HTTP {status}. Does the key have Cloud Save Viewer/Editor?")
        results = data.get("results") or []
        for r in results:
            classes = r.get("accessClasses") or {}
            out[r["id"]] = {c: (classes.get(c) or {}).get("numKeys", 0) for c in ACCESS_PATHS}
        nxt = (data.get("links") or {}).get("next")
        if not nxt or not results:
            return out
        new_start = next_start(nxt, results[-1]["id"])
        if new_start == start:
            return out   # no progress: stop rather than loop
        start = new_start


def next_start(link, last_id):
    """The `start` for the next Cloud Save players page: from a link's start= parameter, a bare player id, or
    (a link without one) the last id on this page. The docs show links.next only as "string"."""
    link = str(link)
    if "/" in link or "?" in link:
        q = urllib.parse.parse_qs(urllib.parse.urlparse(link).query)
        return q.get("start", [last_id])[0]
    return link


def read_save(api, project, env_id, player_id):
    status, data = api.call("GET", f"/cloud-save/v1/data/projects/{project}/environments/{env_id}/players/{player_id}/items",
                            {"keys": SAVE_KEY})
    if status != 200:
        return None
    for item in (data or {}).get("results") or []:
        if item.get("key") == SAVE_KEY:
            return item.get("value")
    return None


def delete_player(api, project, envs, player_id, data_by_env):
    """Cloud Save data first (every class with keys, in every environment), then the account. Returns errors."""
    errors = []
    for env_name, env_id in envs.items():
        counts = data_by_env[env_name].get(player_id) or {}
        for cls, n in counts.items():
            if not n:
                continue
            path = f"/cloud-save/v1/data/projects/{project}/environments/{env_id}/players/{player_id}/{ACCESS_PATHS[cls]}"
            status, _ = api.call("DELETE", path)
            if status not in (200, 204, 404):
                errors.append(f"{env_name} {cls} data: HTTP {status}")
    if errors:
        return errors   # keep the account so the data can still be found and retried
    status, _ = api.call("DELETE", f"/player-identity/v1/projects/{project}/users/{player_id}")
    if status not in (200, 204, 404):
        errors.append(f"account: HTTP {status}")
    return errors


# ── Main ───────────────────────────────────────────────────────────────────────────────────────────────────

def date_arg(s):
    return datetime.fromisoformat(s).replace(tzinfo=timezone.utc)


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--confirm", action="store_true", help="really delete (default: dry run)")
    p.add_argument("--max", type=int, default=100, help="most players to delete in one run (default 100)")
    p.add_argument("--created-after", type=date_arg, help="only players created on/after this UTC date (YYYY-MM-DD)")
    p.add_argument("--created-before", type=date_arg, help="only players created before this UTC date (YYYY-MM-DD)")
    p.add_argument("--include-unstamped", action="store_true",
                   help="also purge development saves written before the platform stamp existed")
    p.add_argument("--ids", help="file of player ids (one per line): only these are considered, still filtered")
    args = p.parse_args(argv)

    env = {k: os.environ.get(k, "").strip() for k in (
        "UGS_PURGE_SERVICE_KEY_ID", "UGS_PURGE_SERVICE_KEY_SECRET", "UGS_PROJECT_ID",
        "UGS_ENV_ID_DEVELOPMENT", "UGS_ENV_ID_PRODUCTION")}
    missing = [k for k, v in env.items() if not v]
    if missing:
        sys.exit("Missing environment variables: " + ", ".join(missing))
    project = env["UGS_PROJECT_ID"]
    envs = {"production": env["UGS_ENV_ID_PRODUCTION"], "development": env["UGS_ENV_ID_DEVELOPMENT"]}
    api = Api(env["UGS_PURGE_SERVICE_KEY_ID"], env["UGS_PURGE_SERVICE_KEY_SECRET"])

    print("Listing players...")
    players = list_players(api, project)
    if args.ids:
        with open(args.ids, encoding="utf-8") as f:
            wanted = {line.strip() for line in f if line.strip()}
        players = [pl for pl in players if pl.get("id") in wanted]
    print(f"  {len(players)} player(s)")
    data_by_env = {}
    for name, env_id in envs.items():
        data_by_env[name] = players_with_data(api, project, env_id)
        print(f"  {len(data_by_env[name])} with Cloud Save data in {name}")

    stamp = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    log_path = f"purge-{stamp}.jsonl"
    mode = "DELETING" if args.confirm else "DRY RUN (nothing is changed)"
    print(f"\n{mode}. Log: {log_path}\n")

    tally, deleted, failed = {}, 0, 0
    with open(log_path, "w", encoding="utf-8") as log:
        for pl in players:
            pid = pl.get("id")
            in_prod = pid in data_by_env["production"]
            dev_counts = data_by_env["development"].get(pid)
            dev_save = "none"
            if not pl.get("externalIds") and not in_prod and dev_counts:
                dev_save = save_platforms(read_save(api, project, envs["development"], pid)) \
                    if dev_counts.get("default") else "none"
            action, reason = decide(pl, in_prod, dev_save, args.created_after, args.created_before,
                                    args.include_unstamped)
            if action == "purge" and deleted >= args.max:
                action, reason = "keep", f"--max {args.max} reached"
            entry = {"id": pid, "createdAt": pl.get("createdAt"), "action": action, "reason": reason}
            if action == "purge":
                if args.confirm:
                    errors = delete_player(api, project, envs, pid, data_by_env)
                    entry["deleted"] = not errors
                    if errors:
                        entry["errors"] = errors
                        failed += 1
                        print(f"  FAILED {pid}: {'; '.join(errors)}")
                    else:
                        deleted += 1
                else:
                    deleted += 1
            tally[(action, reason)] = tally.get((action, reason), 0) + 1
            log.write(json.dumps(entry) + "\n")

    for (action, reason), n in sorted(tally.items()):
        print(f"  {action:5}  {n:5}  {reason}")
    verb = "deleted" if args.confirm else "would delete"
    print(f"\n{deleted} player(s) {verb}" + (f", {failed} failed (see the log)" if failed else "") + ".")
    if not args.confirm and deleted:
        print("Review the log, then re-run with --confirm to delete them.")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
