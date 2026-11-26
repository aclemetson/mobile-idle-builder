#!/usr/bin/env python3
"""Checks that each UGS service account can do its CI job, and nothing more.

Run by .github/workflows/ugs-key-check.yml after a key is created or rotated (docs/runbooks/secrets.md).
Prints only HTTP status codes, never response bodies (the symbols endpoint returns an upload token).

Accounts (env vars, empty = skipped):
  ci-project      UGS_PROJECT_SERVICE_KEY_ID / UGS_PROJECT_SERVICE_KEY_SECRET   Remote Config Admin (project)
  ci-development  UGS_DEV_SERVICE_KEY_ID / UGS_DEV_SERVICE_KEY_SECRET           Cloud Code Editor (development env)
  ci-symbols      UGS_SYMBOLS_SERVICE_KEY_ID / UGS_SYMBOLS_SERVICE_KEY_SECRET   Observability Symbols Uploader (project)
Also: UGS_PROJECT_ID, UGS_ENV_ID_DEVELOPMENT, UGS_ENV_ID_PRODUCTION (production checks skip without it).

--write also PUTs the development Remote Config settings back unchanged, to prove the write permission.
"""
import base64
import json
import os
import sys
import urllib.error
import urllib.request

API = "https://services.api.unity.com"
DIAG = "https://services.unity.com/api/cloud-diagnostics/crash-service/v1/projects"

ALLOWED = "allowed"
DENIED = "denied"


def env(name):
    return os.environ.get(name, "").strip()


def auth(prefix):
    key_id, secret = env(f"UGS_{prefix}_SERVICE_KEY_ID"), env(f"UGS_{prefix}_SERVICE_KEY_SECRET")
    if not key_id or not secret:
        return None
    return "Basic " + base64.b64encode(f"{key_id}:{secret}".encode()).decode()


def call(method, url, header, body=None):
    """Returns (status, parsed JSON or None). Never prints the body."""
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={"Authorization": header})
    if data is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            raw = r.read()
            try:
                return r.status, json.loads(raw) if raw else None
            except ValueError:
                return r.status, None
    except urllib.error.HTTPError as e:
        return e.code, None
    except urllib.error.URLError as e:
        return f"network error ({e.reason})", None


def outcome(status):
    if isinstance(status, int) and 200 <= status < 300:
        return ALLOWED
    if status in (401, 403):
        return DENIED
    return None  # 404, 5xx, network: inconclusive, reported as a failure


results = []


def check(label, method, url, header, expect, body=None):
    status, data = call(method, url, header, body)
    got = outcome(status)
    ok = got == expect
    results.append(ok)
    mark = "PASS" if ok else "FAIL"
    print(f"[{mark}] {label}: HTTP {status} (expected {expect})")
    return data


def main():
    write = "--write" in sys.argv
    project = env("UGS_PROJECT_ID")
    dev, prod = env("UGS_ENV_ID_DEVELOPMENT"), env("UGS_ENV_ID_PRODUCTION")
    if not project or not dev:
        print("::error::UGS_PROJECT_ID and UGS_ENV_ID_DEVELOPMENT are required.")
        return 1
    if not prod:
        print("::warning::UGS_ENV_ID_PRODUCTION not available to this job: production checks skipped.")

    rc = lambda e: f"{API}/remote-config/v1/projects/{project}/environments/{e}/configs"
    cc = lambda e: f"{API}/cloud-code/v1/projects/{project}/environments/{e}/modules"
    symbols = f"{DIAG}/{project}/symbols/token"

    keys = {"PROJECT": auth("PROJECT"), "DEV": auth("DEV"), "SYMBOLS": auth("SYMBOLS")}
    for name, header in keys.items():
        if header is None:
            print(f"::warning::UGS_{name}_SERVICE_KEY_ID / _SECRET not set: its checks are skipped.")

    if keys["PROJECT"]:
        h = keys["PROJECT"]
        print("\n== ci-project (Remote Config Admin, project)")
        configs = check("read development Remote Config", "GET", rc(dev), h, ALLOWED)
        if prod:
            check("read production Remote Config", "GET", rc(prod), h, ALLOWED)
        check("Cloud Code in development (should not have it)", "GET", cc(dev), h, DENIED)
        if write:
            settings = [c for c in (configs or {}).get("configs", []) if c.get("type") == "settings"]
            if len(settings) != 1:
                results.append(False)
                print(f"[FAIL] write development Remote Config: expected 1 settings config, found {len(settings)}")
            else:
                c = settings[0]
                check("write development Remote Config (unchanged values)", "PUT",
                      f"{API}/remote-config/v1/projects/{project}/configs/{c['id']}", h, ALLOWED,
                      body={"type": "settings", "value": c.get("value", [])})

    if keys["DEV"]:
        h = keys["DEV"]
        print("\n== ci-development (Cloud Code Editor, development environment)")
        check("list Cloud Code modules in development", "GET", cc(dev), h, ALLOWED)
        if prod:
            check("list Cloud Code modules in production (must be denied)", "GET", cc(prod), h, DENIED)
            check("read production Remote Config (must be denied)", "GET", rc(prod), h, DENIED)
        check("read development Remote Config (should not have it)", "GET", rc(dev), h, DENIED)

    if keys["SYMBOLS"]:
        h = keys["SYMBOLS"]
        print("\n== ci-symbols (Observability Symbols Uploader, project)")
        check("get a symbol upload token", "GET", symbols, h, ALLOWED)
        check("read development Remote Config (must be denied)", "GET", rc(dev), h, DENIED)
        check("list Cloud Code modules in development (must be denied)", "GET", cc(dev), h, DENIED)

    if not results:
        print("::error::No key was available, so nothing was checked.")
        return 1
    failed = results.count(False)
    print(f"\n{len(results) - failed}/{len(results)} checks passed.")
    if failed:
        print("::error::A key can't do its job, or can do more than it should. A 404/5xx means the check itself "
              "needs fixing, not the key.")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
