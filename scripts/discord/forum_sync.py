"""Discord forum <-> GitHub issue sync (#533). Runs from .github/workflows/discord-sync.yml. Standard library only.

There is no hosted bot process. A scheduled GitHub Action runs this script with the Discord bot's token, does
one pass and exits. It keeps no state of its own:
  - GitHub is the record. Each synced issue carries the label `from-discord` and a hidden marker
    `<!-- discord-thread:<id> -->` in its body, so a rerun never opens a second issue for the same post.
  - Discord forum tags show the status. Status tags are moderator-only, so players can't set them.

Bug forums (#bug-reports, #beta-feedback):
  1. New post missing a template field   -> reply with the template + tag "Needs info".
     Still missing after 72 hours        -> closing note, then lock and archive the post.
  2. Template complete                   -> open a GitHub issue, reply with its link, tag "Tracked".
  3. Issue closed as completed           -> tag "Fixed", reply "fixed, ships in the next update", archive.
     Issue closed as not planned         -> tag "Won't fix", reply, archive.
     Issue reopened                      -> remove "Fixed" / "Won't fix".
Suggestions forum (#suggestions):
  1. A moderator tags a post "Planned"   -> open a GitHub issue (enhancement), reply with its link.
  2. Issue closed as completed           -> tag "Done"; not planned -> tag "Not planned".

Environment (all required unless noted; a missing DISCORD_BOT_TOKEN prints "skip" and exits 0):
  DISCORD_BOT_TOKEN       secret: the bot's token
  DISCORD_GUILD_ID        the server ID
  DISCORD_BUG_FORUM_ID    #bug-reports        (optional; unset skips that forum)
  DISCORD_BETA_FORUM_ID   #beta-feedback      (optional)
  DISCORD_IDEA_FORUM_ID   #suggestions        (optional)
  GITHUB_TOKEN, GITHUB_REPOSITORY             set by Actions
  DRY_RUN=1               (optional) print what would change, write nothing

Discord text is untrusted input. It is only ever sent as JSON to the GitHub API, never put in a shell command.
Copied text has @mentions, #refs and HTML comments defused, and is capped in length. Beta posts are not copied
at all (the beta is private and the repo is public): their issue gets the version, device and a link.
"""
from __future__ import annotations

import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from datetime import datetime, timezone

DISCORD_API = "https://discord.com/api/v10"
GITHUB_API = "https://api.github.com"
USER_AGENT = "DiscordBot (https://github.com, 1.0)"
MARKER_RE = re.compile(r"<!-- discord-thread:(\d+) -->")
NEEDS_INFO_GRACE_HOURS = 72
MAX_NEW_ISSUES_PER_RUN = 10
MAX_BODY_CHARS = 5000
DISCORD_EPOCH_MS = 1420070400000

# ── Templates ────────────────────────────────────────────────────────────────

BUG_FIELDS = ["Version", "Device", "What happened", "Steps to reproduce", "Expected"]
BUG_REQUIRED = ["Version", "Device", "What happened", "Steps to reproduce"]
BUG_TEMPLATE = (
    "**Version:** \n"
    "**Device:** \n"
    "**What happened:** \n"
    "**Steps to reproduce:** \n"
    "**Expected:** "
)
VERSION_RE = re.compile(r"\b\d+\.\d+\.\d+\b")


@dataclass
class Forum:
    key: str                 # "bug" | "beta" | "idea"
    channel_id: str
    channel_name: str
    labels: list[str]
    template: bool           # enforce BUG_FIELDS
    copy_body: bool          # copy the post text into the public issue
    tags: dict[str, str] = field(default_factory=dict)   # lower-case tag name -> tag id


def forums_from_env(env) -> list[Forum]:
    out = []
    for key, var, name, labels, template, copy_body in [
        ("bug", "DISCORD_BUG_FORUM_ID", "#bug-reports", ["bug", "from-discord"], True, True),
        ("beta", "DISCORD_BETA_FORUM_ID", "#beta-feedback", ["beta", "from-discord"], True, False),
        ("idea", "DISCORD_IDEA_FORUM_ID", "#suggestions", ["enhancement", "from-discord"], False, True),
    ]:
        cid = (env.get(var) or "").strip()
        if cid:
            if not cid.isdigit():
                sys.exit(f"{var} must be a numeric channel ID")
            out.append(Forum(key, cid, name, labels, template, copy_body))
    return out


def _plain(line: str) -> str:
    return re.sub(r"[*_`>]", "", line).strip()


def parse_template(text: str, fields=BUG_FIELDS) -> dict[str, str]:
    """Reads "Field: value" blocks. Bold, italics and quote markers around the label are ignored; a value runs
    until the next field label. Returns {field: value} for every field found (value may be empty)."""
    label_re = re.compile(r"^(" + "|".join(re.escape(f) for f in fields) + r")\s*:\s*(.*)$", re.I)
    canon = {f.lower(): f for f in fields}
    found: dict[str, list[str]] = {}
    current = None
    for raw in (text or "").replace("\r\n", "\n").split("\n"):
        m = label_re.match(_plain(raw))
        if m:
            current = canon[m.group(1).lower()]
            found[current] = [m.group(2).strip()]
        elif current:
            found[current].append(raw.rstrip())
    return {k: "\n".join(v).strip() for k, v in found.items()}


def _is_placeholder(value: str) -> bool:
    v = _plain(value)
    return not v or re.fullmatch(r"\(.*\)|\[.*\]|-+|n/?a|\?+", v, re.I) is not None


def missing_fields(values: dict[str, str]) -> list[str]:
    missing = [f for f in BUG_REQUIRED if _is_placeholder(values.get(f, ""))]
    if "Version" not in missing and not VERSION_RE.search(values["Version"]):
        missing.insert(0, "Version")
    return missing


# ── Message text ─────────────────────────────────────────────────────────────

def needs_info_reply(missing: list[str]) -> str:
    return (
        "Thanks for the report! A few details are missing: **" + ", ".join(missing) + "**.\n"
        "Please **edit your first message** (not a reply) so it follows this template. The version is at the "
        "bottom of **Settings** in the game.\n```\n" + BUG_TEMPLATE + "\n```\n"
        f"Posts still missing details after {NEEDS_INFO_GRACE_HOURS // 24} days are closed."
    )


def tracked_reply(issue_url: str) -> str:
    return f"Thanks! This is now tracked as {issue_url}. We'll post here when it's fixed."


def closed_reply(kind: str, completed: bool) -> str:
    if kind == "idea":
        return ("This is done and ships in the next update. Watch #patch-notes." if completed
                else "We've decided not to do this one for now. Thanks for the idea!")
    return ("Fixed! The fix ships in the next update. Watch #patch-notes." if completed
            else "We've closed this without a fix (it may be working as intended, a duplicate, or not "
                 "reproducible). Reply here if you still see it.")


EXPIRED_REPLY = "Closing this post because the details above weren't added. Feel free to post again with the template."


# ── Sanitising (Discord text -> public GitHub issue) ─────────────────────────

ZWSP = "​"


def defuse(text: str) -> str:
    text = text.replace("<!--", "&lt;!--").replace("-->", "--&gt;")
    text = re.sub(r"@(?=[A-Za-z0-9_-])", "@" + ZWSP, text)            # no GitHub @mentions
    text = re.sub(r"(?<!&)#(?=\d)", "#" + ZWSP, text)                   # no issue cross-references
    text = re.sub(r"<@[!&]?\d+>", "@someone", text)                    # Discord mention markup
    return text


def quote(text: str) -> str:
    text = defuse(text.strip())
    if len(text) > MAX_BODY_CHARS:
        text = text[:MAX_BODY_CHARS] + "\n...(truncated, see the Discord post)"
    return "\n".join("> " + ln for ln in text.split("\n"))


def issue_title(forum: Forum, name: str) -> str:
    prefix = {"bug": "[Discord bug]", "beta": "[Beta]", "idea": "[Discord idea]"}[forum.key]
    name = re.sub(r"\s+", " ", defuse(name)).strip()
    return f"{prefix} {name}"[:120]


def issue_body(forum: Forum, thread_url: str, thread_id: str, text: str, values: dict[str, str],
               attachments: int) -> str:
    lines = [f"Reported in Discord {forum.channel_name}: {thread_url}", ""]
    if forum.template:
        for f in ("Version", "Device"):
            lines.append(f"- **{f}:** {defuse(_plain(values.get(f, '')))[:200] or '?'}")
        lines.append("")
    if forum.copy_body:
        lines += ["Original post:", "", quote(text), ""]
    else:
        lines += ["The full report is in the private beta channel (link above).", ""]
    if attachments:
        lines.append(f"The post has {attachments} attachment(s); open it in Discord to see them.")
        lines.append("")
    lines += ["_Copied automatically by `scripts/discord/forum_sync.py`. Discord text is untrusted input._",
              f"<!-- discord-thread:{thread_id} -->"]
    return "\n".join(lines)


# ── Status tags ──────────────────────────────────────────────────────────────

def wanted_tags(forum: Forum, current: list[str], issue: dict | None, needs_info: bool) -> list[str]:
    """Pure: the tag IDs a post should carry. Keeps the player's topic tags, sets the status tags."""
    t = forum.tags
    status_names = ["tracked", "needs info", "fixed", "won't fix", "done", "not planned"]
    status_ids = {t[n] for n in status_names if n in t}
    keep = [x for x in current if x not in status_ids]
    add: list[str] = []
    if forum.key == "idea":
        if issue and issue["state"] == "closed":
            add.append("done" if issue.get("state_reason") == "completed" else "not planned")
            keep = [x for x in keep if x != t.get("planned")]
    else:
        if needs_info:
            add.append("needs info")
        if issue:
            add.append("tracked")
            if issue["state"] == "closed":
                add.append("fixed" if issue.get("state_reason") == "completed" else "won't fix")
    ids = [t[n] for n in add if n in t]
    # Discord allows 5 tags per post; status tags win over topic tags.
    return (ids + keep)[:5]


def snowflake_time(sid: str) -> datetime:
    return datetime.fromtimestamp(((int(sid) >> 22) + DISCORD_EPOCH_MS) / 1000, tz=timezone.utc)


# ── HTTP ─────────────────────────────────────────────────────────────────────

class Api:
    def __init__(self, discord_token: str, github_token: str, repo: str, dry_run: bool):
        self.discord_token, self.github_token, self.repo, self.dry = discord_token, github_token, repo, dry_run

    def _call(self, method, url, headers, body=None):
        data = json.dumps(body).encode("utf-8") if body is not None else None
        for attempt in range(4):
            req = urllib.request.Request(url, data=data, method=method, headers=headers)
            try:
                with urllib.request.urlopen(req, timeout=30) as resp:
                    raw = resp.read()
                    return json.loads(raw) if raw else None
            except urllib.error.HTTPError as e:
                payload = e.read()
                if e.code == 429 and attempt < 3:
                    wait = float((json.loads(payload or b"{}") or {}).get("retry_after", 2))
                    time.sleep(min(wait, 60) + 0.5)
                    continue
                raise RuntimeError(f"{method} {url.split('?')[0]} -> HTTP {e.code}: {payload[:300]!r}") from None

    def discord(self, method, path, body=None, write=False):
        if write and self.dry:
            print(f"  DRY RUN {method} {path} {json.dumps(body)[:200] if body else ''}")
            return None
        return self._call(method, DISCORD_API + path, {
            "Authorization": f"Bot {self.discord_token}", "User-Agent": USER_AGENT,
            "Content-Type": "application/json"}, body)

    def github(self, method, path, body=None, write=False):
        if write and self.dry:
            print(f"  DRY RUN {method} {path} {json.dumps(body)[:200] if body else ''}")
            return {"number": 0, "html_url": "https://github.com/(dry-run)"}
        return self._call(method, GITHUB_API + path, {
            "Authorization": f"Bearer {self.github_token}", "User-Agent": "forum-sync",
            "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28",
            "Content-Type": "application/json"}, body)

    def reply(self, thread_id, content):
        self.discord("POST", f"/channels/{thread_id}/messages",
                     {"content": content, "allowed_mentions": {"parse": []}}, write=True)

    def patch_thread(self, thread_id, **changes):
        self.discord("PATCH", f"/channels/{thread_id}", changes, write=True)


# ── The pass ─────────────────────────────────────────────────────────────────

def load_issue_map(api: Api) -> dict[str, dict]:
    out: dict[str, dict] = {}
    page = 1
    while True:
        batch = api.github("GET", f"/repos/{api.repo}/issues?labels=from-discord&state=all&per_page=100&page={page}")
        for issue in batch:
            if "pull_request" in issue:
                continue
            m = MARKER_RE.search(issue.get("body") or "")
            if m and m.group(1) not in out:
                out[m.group(1)] = issue
        if len(batch) < 100:
            return out
        page += 1


def load_threads(api: Api, guild_id: str, forums: list[Forum]) -> dict[str, dict]:
    ids = {f.channel_id for f in forums}
    threads = {t["id"]: t for t in api.discord("GET", f"/guilds/{guild_id}/threads/active")["threads"]
               if t.get("parent_id") in ids}
    for f in forums:   # recently archived posts, so a late fix still reaches them
        data = api.discord("GET", f"/channels/{f.channel_id}/threads/archived/public?limit=50")
        for t in data.get("threads", []):
            threads.setdefault(t["id"], t)
    return threads


def sync_thread(api: Api, guild_id: str, forum: Forum, thread: dict, issue: dict | None,
                budget: list[int], now: datetime) -> None:
    tid, name = thread["id"], thread.get("name", "")
    meta = thread.get("thread_metadata", {})
    archived, locked = meta.get("archived", False), meta.get("locked", False)
    current = thread.get("applied_tags", [])
    t = forum.tags
    url = f"https://discord.com/channels/{guild_id}/{tid}"

    if issue:
        want = wanted_tags(forum, current, issue, needs_info=False)
        if set(want) == set(current):
            return
        closed = issue["state"] == "closed"
        newly_closed = closed and not ({t.get("fixed"), t.get("won't fix"), t.get("done"),
                                         t.get("not planned")} & set(current))
        print(f"{forum.channel_name} '{name}': issue #{issue['number']} {issue['state']} -> retag")
        if archived:
            api.patch_thread(tid, archived=False)
        if newly_closed:
            api.reply(tid, closed_reply(forum.key, issue.get("state_reason") == "completed"))
        api.patch_thread(tid, applied_tags=want, archived=closed or archived)
        return

    if locked or archived:
        return
    if forum.key == "idea" and t.get("planned") not in current:
        return

    msg = api.discord("GET", f"/channels/{tid}/messages/{tid}")   # a forum post's first message has the thread's ID
    text = (msg or {}).get("content", "")
    values = parse_template(text) if forum.template else {}
    missing = missing_fields(values) if forum.template else []
    has_needs_info = t.get("needs info") in current

    if missing:
        age_h = (now - snowflake_time(tid)).total_seconds() / 3600
        if not has_needs_info:
            print(f"{forum.channel_name} '{name}': missing {missing} -> Needs info")
            api.reply(tid, needs_info_reply(missing))
            api.patch_thread(tid, applied_tags=wanted_tags(forum, current, None, needs_info=True))
        elif age_h > NEEDS_INFO_GRACE_HOURS:
            print(f"{forum.channel_name} '{name}': still missing after {age_h:.0f} h -> lock")
            api.reply(tid, EXPIRED_REPLY)
            api.patch_thread(tid, locked=True, archived=True)
        return

    if budget[0] <= 0:
        print(f"{forum.channel_name} '{name}': issue budget for this run used up, next run")
        return
    budget[0] -= 1
    created = api.github("POST", f"/repos/{api.repo}/issues", {
        "title": issue_title(forum, name),
        "body": issue_body(forum, url, tid, text, values, len((msg or {}).get("attachments", []))),
        "labels": forum.labels}, write=True)
    print(f"{forum.channel_name} '{name}': opened issue #{created['number']}")
    api.reply(tid, tracked_reply(created["html_url"]))
    api.patch_thread(tid, applied_tags=wanted_tags(forum, current, {"state": "open", "number": created["number"]},
                                                     needs_info=False))


def main() -> int:
    env = os.environ
    token = (env.get("DISCORD_BOT_TOKEN") or "").strip()
    if not token:
        print("skip: DISCORD_BOT_TOKEN is not set")
        return 0
    guild_id = (env.get("DISCORD_GUILD_ID") or "").strip()
    if not guild_id.isdigit():
        sys.exit("DISCORD_GUILD_ID must be the numeric server ID")
    forums = forums_from_env(env)
    if not forums:
        print("skip: no forum channel IDs set")
        return 0
    api = Api(token, env["GITHUB_TOKEN"], env["GITHUB_REPOSITORY"], env.get("DRY_RUN") == "1")

    for f in forums:
        chan = api.discord("GET", f"/channels/{f.channel_id}")
        if chan.get("type") != 15:
            sys.exit(f"{f.channel_name} ({f.channel_id}) is not a forum channel")
        f.tags = {tag["name"].strip().lower(): tag["id"] for tag in chan.get("available_tags", [])}
        need = (["planned", "done", "not planned"] if f.key == "idea"
                else ["tracked", "needs info", "fixed", "won't fix"])
        missing = [n for n in need if n not in f.tags]
        if missing:
            print(f"::warning::{f.channel_name} has no tag(s) {missing}; create them (moderator-only) in the forum")

    issues = load_issue_map(api)
    threads = load_threads(api, guild_id, forums)
    by_id = {f.channel_id: f for f in forums}
    budget = [MAX_NEW_ISSUES_PER_RUN]
    now = datetime.now(timezone.utc)
    errors = 0
    for tid, thread in sorted(threads.items()):
        try:
            sync_thread(api, guild_id, by_id[thread["parent_id"]], thread, issues.get(tid), budget, now)
        except RuntimeError as e:   # one bad post never stops the rest
            errors += 1
            print(f"::warning::post {tid}: {e}")
    print(f"done: {len(threads)} posts checked, {MAX_NEW_ISSUES_PER_RUN - budget[0]} issues opened, {errors} errors")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
