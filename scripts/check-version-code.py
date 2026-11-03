#!/usr/bin/env python3
"""Guard that the committed AndroidBundleVersionCode is safe to upload.

Google Play requires the version code to be strictly increasing and globally
unique for the lifetime of the app. We keep the version code in source
(ProjectSettings.asset, owned by the local pre-commit hook), so this script is
the gate that rejects a value which:

  * already exists on Google Play (an earlier PR/upload used it), or
  * is not greater than the version code currently on the base release branch
    (catches two in-flight PRs that picked the same code before either merged).

It is read-only against Google Play: it opens an edit to list bundles, then
deletes the edit. It never commits anything.

Exit 0 when the proposed code is safe, exit 1 otherwise.

Environment:
  GOOGLE_PLAY_JSON_KEY  Service-account JSON (plaintext). Required.
  PACKAGE_NAME          App id. Default: com.clemtek.mobileidlebuilder
  PROJECT_SETTINGS      Path to ProjectSettings.asset.
                        Default: ProjectSettings/ProjectSettings.asset
  BASE_REF              Optional. Base branch (e.g. release/0.3) to compare the
                        tip version code against. Best-effort: skipped if the
                        ref is not fetched locally.
"""

import json
import os
import re
import subprocess
import sys

VERSION_CODE_RE = re.compile(r"^\s*AndroidBundleVersionCode:\s*(\d+)\s*$", re.MULTILINE)
SCOPE = "https://www.googleapis.com/auth/androidpublisher"


def fail(msg):
    print(f"::error::{msg}")
    sys.exit(1)


def parse_version_code(text, source):
    match = VERSION_CODE_RE.search(text)
    if not match:
        fail(f"Could not find AndroidBundleVersionCode in {source}")
    return int(match.group(1))


def read_proposed_code(settings_path):
    try:
        with open(settings_path, "r", encoding="utf-8") as fh:
            text = fh.read()
    except OSError as exc:
        fail(f"Could not read {settings_path}: {exc}")
    return parse_version_code(text, settings_path)


def read_base_branch_code(base_ref, settings_path):
    """Return the version code on the tip of base_ref, or None if unavailable."""
    if not base_ref:
        return None
    for ref in (f"origin/{base_ref}", base_ref):
        try:
            text = subprocess.check_output(
                ["git", "show", f"{ref}:{settings_path}"],
                stderr=subprocess.DEVNULL,
                text=True,
            )
        except subprocess.CalledProcessError:
            continue
        return parse_version_code(text, f"{ref}:{settings_path}")
    print(
        f"::warning::Base ref '{base_ref}' not available locally; "
        "skipping base-branch comparison (Google Play check still applies)."
    )
    return None


def google_play_max_code(package_name):
    try:
        from google.oauth2 import service_account
        from googleapiclient.discovery import build
        from googleapiclient.errors import HttpError
    except ImportError:
        fail(
            "Missing deps. Install with: "
            "pip install google-api-python-client google-auth"
        )

    raw = os.environ.get("GOOGLE_PLAY_JSON_KEY")
    if not raw:
        fail("GOOGLE_PLAY_JSON_KEY is not set")

    try:
        info = json.loads(raw)
    except json.JSONDecodeError as exc:
        fail(f"GOOGLE_PLAY_JSON_KEY is not valid JSON: {exc}")

    creds = service_account.Credentials.from_service_account_info(info, scopes=[SCOPE])
    service = build("androidpublisher", "v3", credentials=creds, cache_discovery=False)
    edits = service.edits()

    edit_id = None
    try:
        edit_id = edits.insert(packageName=package_name, body={}).execute()["id"]
        bundles = edits.bundles().list(
            packageName=package_name, editId=edit_id
        ).execute().get("bundles", [])
    except HttpError as exc:
        fail(f"Google Play API error: {exc}")
    finally:
        if edit_id:
            try:
                edits.delete(packageName=package_name, editId=edit_id).execute()
            except Exception:  # cleanup only; never mask the real result
                pass

    codes = [b["versionCode"] for b in bundles]
    return max(codes) if codes else 0


def main():
    package_name = os.environ.get("PACKAGE_NAME", "com.clemtek.mobileidlebuilder")
    settings_path = os.environ.get(
        "PROJECT_SETTINGS", "ProjectSettings/ProjectSettings.asset"
    )
    base_ref = os.environ.get("BASE_REF", "").strip()

    proposed = read_proposed_code(settings_path)
    play_max = google_play_max_code(package_name)
    base_code = read_base_branch_code(base_ref, settings_path)

    floor = max(play_max, base_code or 0)
    print(f"Proposed version code     : {proposed}")
    print(f"Max on Google Play        : {play_max}")
    print(f"Base branch ({base_ref or 'n/a'}) tip : {base_code if base_code is not None else 'n/a'}")
    print(f"Must be strictly greater than: {floor}")

    if proposed <= floor:
        fail(
            f"AndroidBundleVersionCode {proposed} is not greater than {floor}. "
            "Bump AndroidBundleVersionCode in ProjectSettings/ProjectSettings.asset."
        )

    print(f"OK: version code {proposed} is safe to upload.")


if __name__ == "__main__":
    main()
